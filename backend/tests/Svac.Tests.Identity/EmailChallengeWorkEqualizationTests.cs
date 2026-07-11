using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.FieldEncryption;
using Svac.Identity.Auth;
using Svac.Identity.DependencyInjection;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// MAIL-1/SREJ-2/SREJ-3 (SECURITY_REVIEW_S3.md): the silent-reject lens's "root cause" note observed that
/// TimingFloor is a floor, not a ceiling — floor-alone is insufficient if a single write/HMAC can exceed
/// it. This suite proves the OTHER half of the fix structurally, with zero wall-clock timing (no flaky CI
/// assertions): a genuinely-absent/decoy challenge row now pays the EXACT SAME keyed-HMAC cost
/// (<c>EmailCodes.Hash</c> -&gt; <c>IFieldKeyVault.GetNamedSecret</c>) a real backed-row comparison does,
/// counted via a decorator around the real (dev) vault. Before the fix: the decoy/absent branch called
/// <c>GetNamedSecret</c> ZERO times while the backed branch called it ONCE — an observable delta. After:
/// both call it exactly once.
///
/// Builds its OWN isolated DI provider against <see cref="IdentityDbFixture"/>'s ALREADY-migrated,
/// already-config-seeded shared container connection string (no new container, no re-seed) so
/// <see cref="IFieldKeyVault"/> can be swapped for a counting decorator without disturbing the shared
/// collection's own singleton vault instance.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class EmailChallengeWorkEqualizationTests(IdentityDbFixture fixture)
{
    private sealed class CountingFieldKeyVault(IFieldKeyVault inner) : IFieldKeyVault
    {
        private int _emailCodeHmacCalls;

        public int EmailCodeHmacCalls => _emailCodeHmacCalls;

        public void Reset() => Interlocked.Exchange(ref _emailCodeHmacCalls, 0);

        public Task<byte[]> WrapKey(string keyName, byte[] rawKey, CancellationToken ct = default) => inner.WrapKey(keyName, rawKey, ct);

        public Task<byte[]> UnwrapKey(string keyName, byte[] wrappedKey, CancellationToken ct = default) => inner.UnwrapKey(keyName, wrappedKey, ct);

        public Task DestroyKey(string keyName, CancellationToken ct = default) => inner.DestroyKey(keyName, ct);

        public Task<byte[]> GetNamedSecret(string keyName, CancellationToken ct = default)
        {
            if (string.Equals(keyName, EmailCodes.NamedSecretKey, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _emailCodeHmacCalls);
            }
            return inner.GetNamedSecret(keyName, ct);
        }
    }

    private (ServiceProvider Provider, CountingFieldKeyVault Vault) BuildIsolatedProvider()
    {
        var services = new ServiceCollection();
        services.AddDomainCore(fixture.ConnectionString, devSeamsEnabled: true);
        services.AddIdentityModule(fixture.ConnectionString, smtpOptions: null);
        services.RemoveAll<Svac.DomainCore.Contracts.Email.IEmailSender>();
        services.AddSingleton<Svac.DomainCore.Contracts.Email.IEmailSender>(new FakeEmailSender());

        services.RemoveAll<IFieldKeyVault>();
        var counting = new CountingFieldKeyVault(new DevKeyringFieldKeyVault());
        services.AddSingleton<IFieldKeyVault>(counting);

        return (services.BuildServiceProvider(), counting);
    }

    [Fact]
    public async Task ConfirmSignupCode_BackedWrongCode_VsUnbackedDecoy_BothComputeExactlyOneHmac()
    {
        var (provider, vault) = BuildIsolatedProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var machine = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        // Backed: a fresh email gets a REAL, persisted challenge row.
        var freshEmail = $"equalize-fresh-{Guid.NewGuid():N}@example.com";
        var backedChallengeId = await machine.IssueForSignup(freshEmail, "en", ctx, CancellationToken.None);

        vault.Reset();
        var backedOutcome = await machine.ConfirmSignupCode(backedChallengeId, "000000", ctx, CancellationToken.None);
        Assert.IsType<ChallengeConfirmResult.InvalidResult>(backedOutcome); // wrong code against a real row
        var backedCalls = vault.EmailCodeHmacCalls;
        Assert.Equal(1, backedCalls);

        // Unbacked: an already-registered email gets a DECOY challengeId — no row ever persists for it.
        var registeredEmail = SeedAccountRow(db, state: "active");
        await db.SaveChangesAsync();
        var decoyChallengeId = await machine.IssueForSignup(registeredEmail, "en", ctx, CancellationToken.None);
        Assert.Null(await db.EmailChallenges.SingleOrDefaultAsync(c => c.ChallengeId == decoyChallengeId));

        vault.Reset();
        var decoyOutcome = await machine.ConfirmSignupCode(decoyChallengeId, "000000", ctx, CancellationToken.None);
        Assert.IsType<ChallengeConfirmResult.InvalidResult>(decoyOutcome);
        var decoyCalls = vault.EmailCodeHmacCalls;

        // THE equalization proof: before this fix, the decoy branch computed ZERO HMACs (immediate
        // rollback, nothing to compare). Now it computes exactly one, matching the backed branch.
        Assert.Equal(1, decoyCalls);
        Assert.Equal(backedCalls, decoyCalls);
    }

    [Fact]
    public async Task RedeemLoginCode_PendingChallengeWrongCode_VsNoChallengeAtAll_BothComputeExactlyOneHmac()
    {
        var (provider, vault) = BuildIsolatedProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var machine = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        // Pending challenge: a live account that just requested a login code.
        var liveEmail = SeedAccountRow(db, state: "active");
        await db.SaveChangesAsync();
        await machine.IssueForLogin(liveEmail, "en", ctx, CancellationToken.None);

        vault.Reset();
        var pendingOutcome = await machine.RedeemLoginCode(liveEmail, "000000", CancellationToken.None);
        Assert.Null(pendingOutcome); // wrong code against a real pending challenge
        var pendingCalls = vault.EmailCodeHmacCalls;
        Assert.Equal(1, pendingCalls);

        // No challenge at all: an absent account never gets a login challenge row.
        var absentEmail = $"equalize-absent-{Guid.NewGuid():N}@example.com";
        await machine.IssueForLogin(absentEmail, "en", ctx, CancellationToken.None); // no-op: not eligible

        vault.Reset();
        var absentOutcome = await machine.RedeemLoginCode(absentEmail, "000000", CancellationToken.None);
        Assert.Null(absentOutcome);
        var absentCalls = vault.EmailCodeHmacCalls;

        Assert.Equal(1, absentCalls); // before the fix: 0.
        Assert.Equal(pendingCalls, absentCalls);
    }

    [Fact]
    public async Task ConfirmEmailChange_BackedWrongCode_VsUnknownChallengeId_BothComputeExactlyOneHmac()
    {
        var (provider, vault) = BuildIsolatedProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var machine = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        var accountId = SeedAccountRowReturningId(db, state: "active");
        await db.SaveChangesAsync();

        var newEmail = $"equalize-change-{Guid.NewGuid():N}@example.com";
        var backedChallengeId = await machine.IssueForEmailChange(accountId, newEmail, "en", ctx, CancellationToken.None);

        vault.Reset();
        var backedOutcome = await machine.ConfirmEmailChange(accountId, backedChallengeId, "000000", ctx, CancellationToken.None);
        Assert.IsType<EmailChangeConfirmResult.InvalidResult>(backedOutcome);
        var backedCalls = vault.EmailCodeHmacCalls;
        Assert.Equal(1, backedCalls);

        var unknownChallengeId = "chl_01HZZZZZZZZZZZZZZZZZZZZZZZ";
        vault.Reset();
        var unknownOutcome = await machine.ConfirmEmailChange(accountId, unknownChallengeId, "000000", ctx, CancellationToken.None);
        Assert.IsType<EmailChangeConfirmResult.InvalidResult>(unknownOutcome);
        var unknownCalls = vault.EmailCodeHmacCalls;

        Assert.Equal(1, unknownCalls); // before the fix: 0.
        Assert.Equal(backedCalls, unknownCalls);
    }

    private static string SeedAccountRow(IdentityDbContext db, string state)
    {
        var email = $"equalize-seed-{Guid.NewGuid():N}@example.com";
        SeedAccountRow(db, email, $"eqhandle_{Guid.NewGuid():N}"[..20], state);
        return email;
    }

    private static string SeedAccountRowReturningId(IdentityDbContext db, string state)
    {
        var accountId = $"usr_{Guid.NewGuid():N}"[..26];
        SeedAccountRow(db, $"equalize-seed-{Guid.NewGuid():N}@example.com", $"eqhandle_{Guid.NewGuid():N}"[..20], state, accountId);
        return accountId;
    }

    private static void SeedAccountRow(IdentityDbContext db, string email, string handle, string state, string? accountId = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId ?? $"usr_{Guid.NewGuid():N}"[..26],
            Handle = handle,
            Email = email,
            EmailVerifiedAt = now,
            BirthdateEnc = new byte[] { 1, 2, 3 },
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "shonen",
            Locale = "en",
            AccountState = state,
            StateChangedAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "legitimate_interest",
        });
    }
}
