using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.Identity.Auth;
using Svac.Identity.Config;
using Svac.Identity.Contracts;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// SLICE_PLAYBOOK.md's deferred-finding discipline: a Skip-annotated proof test per DEFER row, each
/// documenting the exact finding + the slice it's carried to, in a shape that would fail (catching a
/// regression or proving the gap) the moment someone un-Skips it. None of these are fixed in this pass —
/// see SECURITY_REVIEW_S3.md's DEFER table for the disposition rule and rationale.
/// </summary>
[Collection(IdentityHttpCollectionDefinition.Name)]
public sealed class DeferredFindingsProofTests(IdentityHttpFixture fixture)
{
    private static readonly System.Text.Json.JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------------------------------------
    // CONC-5 / AUTH-5 (MED, carried to S14 with the quota-tx work): SessionIssuance.IssueAsync's
    // eviction is check-then-act (CountAsync -> evict oldest -> Add -> one SaveChangesAsync) with no
    // advisory lock over the account's sessions. Two concurrent logins can both observe count == cap and
    // both mint, transiently exceeding identity.session.max_active_per_account. No privilege gain — just
    // >cap live sessions until the next issue re-evicts.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S3.md CONC-5 (session-cap eviction check-then-act) -> carried to S14 with the quota-tx work")]
    public async Task ConcurrentLogins_AtTheSessionCap_CanTransientlyExceedMaxActivePerAccount()
    {
        var (accountId, _) = await fixture.SeedActiveAccountWithSession();
        const int cap = 1; // identity.session.max_active_per_account's manifest v0 is 10; forced to 1 here so two concurrent issues collide deterministically instead of needing 10+ parallel calls.

        using var seedScope = fixture.NewScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await seedDb.Accounts.SingleAsync(a => a.AccountId == accountId);
        var region = account.Region;
        var lawfulBasis = account.LawfulBasis;

        // Two SEPARATE scopes/DbContexts (own connections) — exactly what two concurrent real HTTP
        // requests would each get; EF Core's DbContext is not safe for concurrent use from one instance.
        using var firstScope = fixture.NewScope();
        using var secondScope = fixture.NewScope();
        Task<IssuedSession> IssueOnce(Microsoft.Extensions.DependencyInjection.IServiceScope scope) => SessionIssuance.IssueAsync(
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(), accountId, deviceId: null, region, lawfulBasis,
            accessTtlMinutes: 60, refreshTtlDays: 90, maxActivePerAccount: cap, CancellationToken.None);

        var first = IssueOnce(firstScope);
        var second = IssueOnce(secondScope);
        await Task.WhenAll(first, second);

        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var liveSessionCount = await assertDb.Sessions.CountAsync(s => s.AccountId == accountId && s.RevokedAt == null);

        // Documents the gap: today this can observe 2 (> cap) transiently. Fixed shape (S14): an atomic
        // advisory-lock or eviction-after-insert makes this assertion hold under real concurrency.
        Assert.True(liveSessionCount <= cap, $"expected at most {cap} live session(s), found {liveSessionCount} — CONC-5's check-then-act eviction let both concurrent issues through.");
    }

    // ------------------------------------------------------------------------------------------------
    // AUTH-4 (LOW, carried to S4 with notification delivery): POST /v1/auth/logout only sets
    // RevokedAt/RevokeReason on the session — it never touches devices, and every session is minted with
    // device_id=null (SessionIssuance.IssueAsync/AuthEndpoints), so the "logout clears its device's push
    // token" cascade named in §1c has nothing to bind to even if AuthEndpoints.PostAuthLogout tried.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S3.md AUTH-4 (logout doesn't clear device push token; session<->device binding never set) -> carried to S4 (notification delivery)")]
    public async Task Logout_DoesNotClearTheAccountsDevicePushToken()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();

        var registerDevice = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(
            HttpMethod.Post, "/v1/me/devices", token, new { platform = "ios", pushToken = "auth-4-push-token" }));
        Assert.Equal(System.Net.HttpStatusCode.Created, registerDevice.StatusCode);
        var deviceId = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            await registerDevice.Content.ReadAsStringAsync(),
            CaseInsensitive).GetProperty("deviceId").GetString()!;

        var logout = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/auth/logout", token));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, logout.StatusCode);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var device = await db.Devices.SingleAsync(d => d.DeviceId == deviceId);

        // §1c's own contract clause: logout "revokes the presenting session + clears its device's push
        // token." Today this fails — the push token survives logout untouched.
        Assert.Null(device.PushToken);
    }

    // ------------------------------------------------------------------------------------------------
    // CONC-6 (LOW, carried to S12 with moderation surfaces): DeletionEndpoints.PostDeletion does
    // `effectiveAt!.Value` on the re-read after IAccountLifecycle.RequestDeletion — a non-null-forgiving
    // unwrap. RequestDeletion's own guarded FOR-UPDATE fetch (`WHERE account_state IN
    // ('active','suspended','banned')`) early-returns doing NOTHING if a concurrent staff action (Ban/
    // Suspend, or the CONC-1 physical-purge CAS) flips the account's state between the policy chokepoint's
    // authorization and this method's own read — leaving deletion_effective_at exactly as it always was:
    // NULL, for an account that has never actually had deletion requested. The endpoint's blind `.Value`
    // then throws InvalidOperationException -> unhandled -> 500, instead of a graceful "state changed,
    // try again" response.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S3.md CONC-6 (PostDeletion's effectiveAt!.Value unwrap 500s on a RequestDeletion no-op race) -> carried to S12 (moderation surfaces)")]
    public async Task RequestDeletion_ThatNoOpsBecauseTheAccountRaceOutOfEligibleStates_LeavesEffectiveAtNull_AndTheEndpointsUnwrapWouldThrow()
    {
        var (accountId, _) = await fixture.SeedActiveAccountWithSession(accountState: "deleted"); // simulates the race outcome: some OTHER path already moved the account out of active/suspended/banned before RequestDeletion's own FOR UPDATE fetch runs.

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
        var ctx = IdentityHttpFixtureAnonymousContext();

        await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), ctx, CancellationToken.None);

        // Exactly DeletionEndpoints.PostDeletion's own re-read (Deletion/DeletionEndpoints.cs).
        var effectiveAt = await db.Accounts.Where(a => a.AccountId == accountId).Select(a => a.DeletionEffectiveAt).SingleAsync();
        Assert.Null(effectiveAt); // RequestDeletion no-op'd — nothing was ever scheduled.

        // The endpoint's own unwrap, reproduced directly: `effectiveAt!.Value` on a genuinely-null value.
        Assert.Throws<InvalidOperationException>(() => _ = effectiveAt!.Value);
    }

    // ------------------------------------------------------------------------------------------------
    // CONC-7 (LOW, carried to S12 with moderation surfaces): ExportEndpoints.PostMeExport's 23505-loser
    // path re-reads the winning job with `.FirstAsync(...)` over `state IN ('pending','ready')` — if the
    // winner's row has ALREADY left that state set by the time the loser re-reads (e.g. ExportWorker.
    // RunAsync completed synchronously and flipped straight to 'failed', or a lazy-expiry sweep raced it
    // to 'expired' in the same instant), FirstAsync throws "Sequence contains no matching element" ->
    // unhandled -> 500, instead of falling back to SOME resolvable export id.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S3.md CONC-7 (export 23505-loser re-read FirstAsync 500s if the winner's row already left pending/ready) -> carried to S12 (moderation surfaces)")]
    public async Task ExportJobsQuery_MirroringThe23505LoserReRead_ThrowsWhenTheWinningRowAlreadyLeftPendingOrReady()
    {
        var (accountId, _) = await fixture.SeedActiveAccountWithSession();

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        // Simulates the winner's row having ALREADY resolved to "failed" (e.g. ExportWorker.RunAsync
        // completed synchronously) by the instant a concurrent loser's 23505-catch handler re-reads.
        db.ExportJobs.Add(new Svac.Identity.Persistence.ExportJobEntity
        {
            ExportId = "exp_" + Guid.NewGuid().ToString("N")[..26],
            AccountId = accountId,
            State = "failed",
            RequestedAt = now,
            Region = "US",
            LawfulBasis = "legitimate_interest",
        });
        await db.SaveChangesAsync();

        // Exactly ExportEndpoints.PostMeExport's own loser re-read query (Endpoints/ExportEndpoints.cs).
        var query = db.ExportJobs
            .Where(e => e.AccountId == accountId && (e.State == "pending" || e.State == "ready"))
            .Select(e => e.ExportId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => query.FirstAsync());
    }

    private static RequestContext IdentityHttpFixtureAnonymousContext() => RequestContext.System(
        new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System),
        correlationId: "deferred-proof-conc-6");
}

/// <summary>
/// OPS-5 (LOW, carried to S5 with the ops desk): the manifest seeds TWO rows for the same tunable — the
/// desk-facing `identity.export.daily_cap` and the enforced `quota.identity.export.request.daily.cap` —
/// and nothing keeps them in sync. An ops edit to the intuitive desk-facing key changes nothing about
/// enforcement.
/// </summary>
public sealed class ConfigDualKeyDivergenceProofTests
{
    [Fact(Skip = "deferred: SECURITY_REVIEW_S3.md OPS-5 (identity.export.daily_cap / quota.identity.export.request.daily.cap can silently drift — no sync mechanism) -> carried to S5 (collapse to one key with the desk)")]
    public async Task EditingTheDeskFacingCap_LeavesTheEnforcedQuotaCapUnchanged()
    {
        var httpFixture = new IdentityHttpFixture();
        await httpFixture.InitializeAsync();
        try
        {
            using var scope = httpFixture.NewScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfigRegistry>();
            var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
            var ctx = RequestContext.System(systemActor, "ops-5-proof");

            await config.SetValue(IdentityConfigKeys.ExportDailyCap, 7, "ops-5-proof-edit", systemActor, ctx);

            var deskFacing = await config.GetValue<int>(IdentityConfigKeys.ExportDailyCap);
            // The REAL enforced key, per QuotaService.Consume's fixed `quota.<key>.cap` convention
            // (Svac.DomainCore/Quota/QuotaService.cs:24) — a DIFFERENT row from the desk-facing one above.
            var reallyEnforced = await config.GetValue<int>("quota.identity.export.request.daily.cap");

            Assert.Equal(7, deskFacing);
            // Documents the gap: the desk-facing edit above changed NOTHING about what QuotaService.Consume
            // actually reads. Fixed shape (S5): collapse to one key, or have the desk write both atomically.
            Assert.Equal(deskFacing, reallyEnforced);
        }
        finally
        {
            await httpFixture.DisposeAsync();
        }
    }
}
