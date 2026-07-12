using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.Identity.Auth;
using Svac.Identity.Contracts;
using Svac.Identity.Deletion;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// PII-3 / CONC-4 (SECURITY_REVIEW_S3.md): the grace-window identity-takeover fixes. A Phase-L
/// grace-deleted account (account_state='deleted', tombstoned_at still NULL) must keep its handle+email
/// unavailable to a third party — they free ONLY at physical purge — and CancelDeletion must still
/// succeed (restoring active) once that closes the takeover window for good. A login code for an email
/// shared between a grace-deleted row and an active row must bind to the ACTIVE account, deterministically.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class GraceWindowIdentityTakeoverTests(IdentityDbFixture fixture)
{
    private static RequestContext UserCtx(string accountId, string correlationId) => new(
        new ActorRef(OpaqueId.Parse(accountId), ActorKind.User),
        new RegionCode("US", null), RegionSource.Signup, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    private static async Task<string> SeedAccount(IServiceScope scope, string handle, string email, string accountState, DateTimeOffset? deletionEffectiveAt = null)
    {
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId, Handle = handle, Email = email, EmailVerifiedAt = now,
            BirthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "1990-01-01", CancellationToken.None),
            AttestedAdultAt = now, TermsVersion = "v1", FandomTag = "fixture", Locale = "en",
            AccountState = accountState, StateChangedAt = now,
            DeletionRequestedAt = accountState == "deleted" ? now : null,
            DeletionEffectiveAt = deletionEffectiveAt,
            CreatedAt = now, LastActiveAt = now, Region = "US", RegionSource = "Signup", LawfulBasis = "conservative_global_v0",
        });
        await db.SaveChangesAsync();
        return accountId;
    }

    // ------------------------------------------------------------------------------------------------
    // Handle: during grace, the handle is NOT available — GetHandleAvailability's own DB query (mirrored
    // here directly, since the endpoint itself is a thin wrapper over the SAME predicate) must report it
    // taken via tombstoned_at, not account_state.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DuringGrace_TheHandle_IsNotAvailable_ToAThirdParty()
    {
        using var scope = fixture.NewScope();
        var handle = $"grace_{Guid.NewGuid():N}"[..20];
        await SeedAccount(scope, handle, $"grace-handle-{Guid.NewGuid():N}@example.test", "deleted", DateTimeOffset.UtcNow.AddDays(10));

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var taken = await db.Accounts.AnyAsync(a => a.Handle == handle && a.TombstonedAt == null);
        Assert.True(taken, "a grace-deleted account's handle must still read as taken (tombstoned_at IS NULL) — it is NOT yet physically purged.");

        // The DB-level enforcement backstop: the partial unique index itself (not just the availability
        // check) must refuse a second row claiming the same handle during grace.
        var attackerId = OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();
        var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        db.Accounts.Add(new AccountEntity
        {
            AccountId = attackerId, Handle = handle, Email = $"attacker-{Guid.NewGuid():N}@example.test", EmailVerifiedAt = DateTimeOffset.UtcNow,
            BirthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(attackerId), "1990-01-01", CancellationToken.None),
            AttestedAdultAt = DateTimeOffset.UtcNow, TermsVersion = "v1", FandomTag = "fixture", Locale = "en",
            AccountState = "active", StateChangedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, LastActiveAt = DateTimeOffset.UtcNow,
            Region = "US", RegionSource = "Signup", LawfulBasis = "conservative_global_v0",
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ------------------------------------------------------------------------------------------------
    // Email: during grace, IssueForSignup treats the address as already-registered — no challenge row
    // persists, and the wire renders the SAME shape as any other "already registered" attempt (never a
    // distinguishing signal that the account is specifically mid-deletion).
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DuringGrace_TheEmail_IsTreatedAsAlreadyRegistered_ForANewSignupAttempt()
    {
        using var scope = fixture.NewScope();
        var email = $"grace-email-{Guid.NewGuid():N}@example.test";
        await SeedAccount(scope, $"gracer_{Guid.NewGuid():N}"[..20], email, "deleted", DateTimeOffset.UtcNow.AddDays(10));

        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());
        var challengeId = await challenges.IssueForSignup(email, "en", ctx, CancellationToken.None);

        // A syntactically valid (but UNBACKED) challengeId is minted either way (anti-enumeration, §1c) —
        // the real signal is that NO row was ever persisted for it.
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.EmailChallenges.AnyAsync(c => c.ChallengeId == challengeId));
        Assert.Contains(fixture.Emails.Sent, m => m.To == email && m.TemplateKey == "email.already_registered");
    }

    // ------------------------------------------------------------------------------------------------
    // Cancel still succeeds and restores active — the defense-in-depth 23505 catch in
    // AccountLifecycle.CancelDeletion never turns this into a 500, and (now that the takeover itself is
    // blocked at the index level) the ordinary case just works.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DuringGrace_CancelDeletion_StillSucceeds_AndRestoresActive()
    {
        using var scope = fixture.NewScope();
        var handle = $"gracecancel_{Guid.NewGuid():N}"[..20];
        var email = $"grace-cancel-{Guid.NewGuid():N}@example.test";
        var accountId = await SeedAccount(scope, handle, email, "deleted", DateTimeOffset.UtcNow.AddDays(10));

        var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
        await lifecycle.CancelDeletion(OpaqueId.Parse(accountId), UserCtx(accountId, "grace-cancel-restore"), CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal("active", account.AccountState);
        Assert.Null(account.DeletionEffectiveAt);
        Assert.Equal(handle, account.Handle);
        Assert.Equal(email, account.Email);
    }

    // ------------------------------------------------------------------------------------------------
    // The deterministic-ordering fix (active-row-first) is defense-in-depth for a torn-identity data
    // shape that PII-3's OWN index fix (tombstoned_at-gated ux_accounts_email) now makes structurally
    // UNREACHABLE through any real write path — attempting to seed a grace-deleted row and an active row
    // sharing one email now fails at the DB constraint itself (proven by the AnAttemptToSeedTornIdentity_
    // IsRejectedByTheIndexItself test below), which is the strongest possible proof the takeover this
    // ordering logic defends against can no longer be created. What MUST still hold, and is the real
    // regression risk of touching IssueForLogin at all, is the RATIFIED "login stays open during grace"
    // behavior (SLICE_S3_CONTRACT.md §2/table row 109) for the single-row, rightful-owner case — the
    // ordering fix must not have accidentally excluded a solo deleted-in-grace row.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DuringGrace_TheRightfulOwner_CanStillLogIn_TheOrderingFixDidNotBreakIt()
    {
        using var scope = fixture.NewScope();
        var email = $"grace-login-{Guid.NewGuid():N}@example.test";
        var accountId = await SeedAccount(scope, $"gracelogin_{Guid.NewGuid():N}"[..20], email, "deleted", DateTimeOffset.UtcNow.AddDays(10));

        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());
        await challenges.IssueForLogin(email, "en", ctx, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var issuedChallenge = await db.EmailChallenges
            .Where(c => c.Purpose == "login" && c.EmailLower == email)
            .OrderByDescending(c => c.CreatedAt)
            .FirstAsync();
        Assert.Equal(accountId, issuedChallenge.AccountId); // the sole (grace-deleted) row for this email — login is NOT excluded.

        var sentCode = fixture.Emails.Sent.Last(m => m.To == email && m.TemplateKey == "email.login_code");
        var redeemedAccountId = await challenges.RedeemLoginCode(email, sentCode.Model["code"], CancellationToken.None);
        Assert.Equal(accountId, redeemedAccountId);
    }

    // ------------------------------------------------------------------------------------------------
    // The strongest possible proof of the takeover fix: attempting to construct the torn-identity data
    // shape (a grace-deleted row and an active row sharing one email) is REJECTED at the real DB
    // constraint — the vulnerability class is structurally unreachable through any write path, not merely
    // guarded by an application-level check that a future refactor could drop.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task AnAttemptToSeedATornIdentity_SharedEmailAcrossAGraceDeletedAndAnActiveRow_IsRejectedByTheIndexItself()
    {
        using var scope = fixture.NewScope();
        var sharedEmail = $"torn-{Guid.NewGuid():N}@example.test";
        await SeedAccount(scope, $"torn_old_{Guid.NewGuid():N}"[..20], sharedEmail, "deleted", DateTimeOffset.UtcNow.AddDays(10));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => SeedAccount(scope, $"torn_new_{Guid.NewGuid():N}"[..20], sharedEmail, "active"));
    }
}
