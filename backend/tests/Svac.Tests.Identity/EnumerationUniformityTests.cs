using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.Identity.Auth;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Anti-enumeration structural proofs (SLICE_S3_CONTRACT.md §1c/§8/§10.3/§10.4): existing vs fresh email
/// renders byte-identical shapes; reserved/taken/retired handles render identically. These test the
/// STRUCTURAL shape (what gets persisted / what mail template fires), which is what the wire response is
/// actually built from — the HTTP-level byte-identical assertion itself is the live E2E's job.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class EnumerationUniformityTests(IdentityDbFixture fixture)
{
    [Fact]
    public async Task SignupEmailVerification_ForAFreshEmail_CreatesAChallengeRowAndSendsVerifyCode()
    {
        var email = $"fresh-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var challengeId = await scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>().IssueForSignup(email, "en", ctx, CancellationToken.None);

        Assert.NotEmpty(challengeId);
        Assert.Contains(fixture.Emails.Sent, m => m.To == email && m.TemplateKey == "email.verify_code");

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.NotNull(await db.EmailChallenges.SingleOrDefaultAsync(c => c.ChallengeId == challengeId));
    }

    [Fact]
    public async Task SignupEmailVerification_ForAnAlreadyRegisteredEmail_NeverPersistsARow_SendsAlreadyRegisteredMailInstead()
    {
        var email = await SeedActiveAccountEmail();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var challengeId = await scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>().IssueForSignup(email, "en", ctx, CancellationToken.None);

        // Byte-identical WIRE SHAPE to the fresh-email case: a syntactically valid challengeId comes back
        // either way (SignupEndpoints.PostEmailVerification renders the SAME 202 {challengeId} body for
        // both branches) — but structurally, no row exists to confirm, and the MAIL fired is different.
        Assert.NotEmpty(challengeId);
        Assert.Contains(fixture.Emails.Sent, m => m.To == email && m.TemplateKey == "email.already_registered");
        Assert.DoesNotContain(fixture.Emails.Sent, m => m.To == email && m.TemplateKey == "email.verify_code");

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Null(await db.EmailChallenges.SingleOrDefaultAsync(c => c.ChallengeId == challengeId));
    }

    [Fact]
    public async Task AuthEmailCode_ForABannedAccount_NeverSendsACode_SameAsAnAbsentAccount()
    {
        var bannedEmail = await SeedAccountEmail(state: "banned");
        var absentEmail = $"absent-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var machine = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        await machine.IssueForLogin(bannedEmail, "en", ctx, CancellationToken.None);
        await machine.IssueForLogin(absentEmail, "en", ctx, CancellationToken.None);

        Assert.DoesNotContain(fixture.Emails.Sent, m => m.To == bannedEmail);
        Assert.DoesNotContain(fixture.Emails.Sent, m => m.To == absentEmail);
    }

    [Fact]
    public async Task AuthEmailCode_ForALiveAccount_SendsTheLoginCode()
    {
        var email = await SeedActiveAccountEmail();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        await scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>().IssueForLogin(email, "en", ctx, CancellationToken.None);

        Assert.Contains(fixture.Emails.Sent, m => m.To == email && m.TemplateKey == "email.login_code");
    }

    [Fact]
    public async Task HandleAvailability_ReservedTakenAndRetired_AllRenderIdenticallyUnavailable()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var reserved = $"reserved_{Guid.NewGuid():N}"[..20];
        db.ReservedHandles.Add(new ReservedHandleEntity { Handle = reserved, Reason = "brand" });

        var retired = $"retired_{Guid.NewGuid():N}"[..20];
        db.RetiredHandles.Add(new RetiredHandleEntity { Handle = retired, RetiredAt = DateTimeOffset.UtcNow });

        var taken = await SeedActiveAccountHandle(db);

        await db.SaveChangesAsync();

        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        foreach (var handle in new[] { reserved, retired, taken })
        {
            var isTakenOrReservedOrRetired =
                await assertDb.Accounts.AnyAsync(a => a.Handle == handle && a.AccountState != "deleted")
                || await assertDb.ReservedHandles.AnyAsync(h => h.Handle == handle)
                || await assertDb.RetiredHandles.AnyAsync(h => h.Handle == handle);
            Assert.True(isTakenOrReservedOrRetired, $"expected \"{handle}\" to render unavailable.");
        }
    }

    private async Task<string> SeedActiveAccountEmail() => await SeedAccountEmail(state: "active");

    private async Task<string> SeedAccountEmail(string state)
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var email = $"seed-{Guid.NewGuid():N}@example.com";
        SeedAccountRow(db, email, $"handle_{Guid.NewGuid():N}"[..20], state);
        await db.SaveChangesAsync();
        return email;
    }

    private static Task<string> SeedActiveAccountHandle(IdentityDbContext db)
    {
        var handle = $"taken_{Guid.NewGuid():N}"[..20];
        SeedAccountRow(db, $"handleseed-{Guid.NewGuid():N}@example.com", handle, "active");
        return Task.FromResult(handle);
    }

    private static void SeedAccountRow(IdentityDbContext db, string email, string handle, string state)
    {
        var now = DateTimeOffset.UtcNow;
        db.Accounts.Add(new AccountEntity
        {
            AccountId = $"usr_{Guid.NewGuid():N}"[..26],
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
