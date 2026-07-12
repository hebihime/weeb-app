using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

// ============================================================================================
// INTERFACE-SKETCH addendum (SLICE_S5_CONTRACT.md §1b, §8 seams 4/12) — the staff sign-in pipeline Pass
// A builds. See AdminActionExecutorTests.cs for the primary sketch + its own conventions (this file
// follows the same "constructed directly, no DI container" test convention).
//
// namespace Svac.AdminHost.Domain.Auth
// {
//     // A minimal, transport-agnostic projection of what BOTH Entra OIDC and DevSeamsStaffTransport hand
//     // the pipeline (SLICE_S5_CONTRACT.md §1b: "the pipeline after [the transport] is IDENTICAL in dev
//     // and prod") — never a ClaimsPrincipal, so this stays unit-testable with zero ASP.NET dependency.
//     public sealed record StaffExternalClaims(string ExternalSubject, bool HasMfaClaim, string Email, string DisplayName);
//
//     public abstract record StaffSignInResult
//     {
//         public sealed record Allowed(ActorRef Staff, IReadOnlySet<Svac.DomainCore.Contracts.Policy.StaffRole> RolesHeld) : StaffSignInResult;
//         // MFA is checked FIRST, before any directory lookup (§1b: "MFA is enforced by US, fail-closed
//         // ... absence => sign-in refused"), so this refusal fires identically whether or not a staff
//         // row exists for the subject -- the two facts are never conflated in the audit trail.
//         public sealed record RefusedNoMfa : StaffSignInResult;
//         // No admin.staff_accounts row for this external_subject. JIT provisioning is REFUSED (§1b) --
//         // this pipeline NEVER inserts a row on this path, under any circumstance.
//         public sealed record RefusedUnknownSubject : StaffSignInResult;
//         // A row exists but status != 'active' (deactivated) -- security_stamp mismatch is checked by
//         // the SEPARATE IStaffSessionRevalidator seam below (sign-in always re-reads the CURRENT stamp,
//         // so a stamp mismatch can only ever be observed mid-session, never at sign-in itself).
//         public sealed record RefusedInactiveAccount : StaffSignInResult;
//     }
//
//     public sealed class StaffSignInPipeline(AdminDbContext adminDb, Svac.DomainCore.Contracts.Streams.IEventStore eventStore)
//     {
//         // Every refusal is audited admin.signin.refused (metadata only: subject/reasonKey, NEVER the
//         // raw claim bag) in the SAME tx as nothing else (a read-mostly pipeline) -- stream_id is
//         // "signin:{externalSubject}" pre-mapping (no ActorRef resolvable yet) or the resolved stf_ ref
//         // once a row is found, so the audit trail is queryable either way.
//         public Task<StaffSignInResult> SignIn(StaffExternalClaims claims, RequestContext systemCtx, CancellationToken ct = default);
//     }
//
//     // The SECOND revocation leg (§1b: "security_stamp ... cookie validation + revalidation ... re-
//     // checks stamp + status -- a deactivated operator loses a LIVE session within the interval").
//     // Called by the cookie auth handler's OnValidatePrincipal on every request past the 9A
//     // admin.session_revalidate_seconds interval, NEVER on every request (that would defeat the point
//     // of a cookie).
//     public interface IStaffSessionRevalidator
//     {
//         public Task<bool> IsStillValid(OpaqueId staffId, string cookieSecurityStamp, CancellationToken ct = default);
//     }
//
//     public sealed class GrantTableStaffSessionRevalidator(AdminDbContext adminDb) : IStaffSessionRevalidator { /* ... */ }
// }
// ============================================================================================

public sealed class StaffSignInTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgis/postgis:16-3.4").Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await coreDb.Database.MigrateAsync();
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        await adminDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static Svac.AdminHost.Domain.Auth.StaffSignInPipeline NewPipeline(AdminDbContext adminDb, CoreDbContext coreDb) =>
        new(adminDb, new PostgresEventStore(coreDb));

    // -------------------- MFA-claim refusal --------------------

    [Fact]
    public async Task SignIn_NoMfaClaim_RefusesEvenWithAnActiveProvisionedStaffRow_AndAudits()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var pipeline = NewPipeline(adminDb, coreDb);
        var subject = "test:no-mfa-subject";
        adminDb.StaffAccounts.Add(new StaffAccountEntity
        {
            Id = AdminTestSupport.FreshStaffId(),
            ExternalSubject = subject,
            Email = "no-mfa@devseams.svac.internal",
            DisplayName = "No MFA fixture",
            Status = "active",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Region = "US",
            LawfulBasis = "contract",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await adminDb.SaveChangesAsync();

        var result = await pipeline.SignIn(
            new Svac.AdminHost.Domain.Auth.StaffExternalClaims(subject, HasMfaClaim: false, "no-mfa@devseams.svac.internal", "No MFA fixture"),
            AdminTestSupport.SystemCtx("no-mfa-signin"));

        Assert.IsType<Svac.AdminHost.Domain.Auth.StaffSignInResult.RefusedNoMfa>(result);

        var events = await CollectAuditEvents(coreDb, $"signin:{subject}");
        var refusal = Assert.Single(events, e => e.EventType == "admin.signin.refused");
        Assert.DoesNotContain("password", refusal.PayloadJson ?? "", StringComparison.OrdinalIgnoreCase); // metadata only
    }

    // -------------------- unknown-subject refusal --------------------

    [Fact]
    public async Task SignIn_MfaPresentButNoStaffRow_RefusesAsUnknownSubject_JitProvisioningNeverHappens()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var pipeline = NewPipeline(adminDb, coreDb);
        var subject = "test:never-provisioned-subject";

        var before = await adminDb.StaffAccounts.CountAsync();

        var result = await pipeline.SignIn(
            new Svac.AdminHost.Domain.Auth.StaffExternalClaims(subject, HasMfaClaim: true, "ghost@devseams.svac.internal", "Ghost fixture"),
            AdminTestSupport.SystemCtx("unknown-subject-signin"));

        Assert.IsType<Svac.AdminHost.Domain.Auth.StaffSignInResult.RefusedUnknownSubject>(result);

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var after = await freshAdminDb.StaffAccounts.CountAsync();
        Assert.Equal(before, after); // JIT NEVER happens -- zero rows created by this call

        var events = await CollectAuditEvents(coreDb, $"signin:{subject}");
        Assert.Single(events, e => e.EventType == "admin.signin.refused");
    }

    // -------------------- allowed leg is audited too (Pass D fix) --------------------

    [Fact]
    public async Task SignIn_MfaPresentAndActiveStaffRow_Allowed_AndAudited_TheLiveSourceForTheStaffSignInsTile()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var pipeline = NewPipeline(adminDb, coreDb);
        var subject = "test:allowed-subject";
        var staffId = AdminTestSupport.FreshStaffId();
        adminDb.StaffAccounts.Add(new StaffAccountEntity
        {
            Id = staffId,
            ExternalSubject = subject,
            Email = "allowed@devseams.svac.internal",
            DisplayName = "Allowed fixture",
            Status = "active",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Region = "US",
            LawfulBasis = "contract",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await adminDb.SaveChangesAsync();

        var result = await pipeline.SignIn(
            new Svac.AdminHost.Domain.Auth.StaffExternalClaims(subject, HasMfaClaim: true, "allowed@devseams.svac.internal", "Allowed fixture"),
            AdminTestSupport.SystemCtx("allowed-signin"));

        Assert.IsType<Svac.AdminHost.Domain.Auth.StaffSignInResult.Allowed>(result);

        // Pass D fix: BEFORE this pass, the allowed leg appended NOTHING — no live source existed for
        // the dashboard's "staff sign-ins" tile (SLICE_S5_CONTRACT.md §8 seam 2), which would have had to
        // either fabricate a count from refusals alone or not exist at all. Keyed by the resolved stf_
        // ref (a real row exists on this leg), never the pre-mapping "signin:{subject}" shape the
        // refusal legs use before a row is found.
        var events = await CollectAuditEvents(coreDb, staffId);
        var succeeded = Assert.Single(events, e => e.EventType == "admin.signin.succeeded");
        Assert.DoesNotContain("password", succeeded.PayloadJson ?? "", StringComparison.OrdinalIgnoreCase); // metadata only
    }

    // -------------------- security-stamp live revalidation --------------------

    [Fact]
    public async Task Revalidator_ReadsTheCurrentStamp_AStaleCookieStampFromBeforeAGrantOrRevoke_IsNoLongerValid()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffId, actor, originalStamp) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "revalidate-target");
        var revalidator = new Svac.AdminHost.Domain.Auth.GrantTableStaffSessionRevalidator(adminDb);

        Assert.True(await revalidator.IsStillValid(actor.Id, originalStamp));

        // Bump the stamp exactly like a grant/revoke/deactivate would (SLICE_S5_CONTRACT.md §2: "bumped
        // on deactivate/grant/revoke; kills live sessions").
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        row.SecurityStamp = Guid.NewGuid().ToString("N");
        await adminDb.SaveChangesAsync();

        Assert.False(await revalidator.IsStillValid(actor.Id, originalStamp)); // the OLD cookie stamp is now stale
        Assert.True(await revalidator.IsStillValid(actor.Id, row.SecurityStamp)); // a freshly-read stamp is still valid
    }

    [Fact]
    public async Task Revalidator_ADeactivatedRow_IsNeverValid_EvenWithTheCurrentStamp()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffId, actor, stamp) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "revalidate-deactivated");
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        row.Status = "deactivated";
        await adminDb.SaveChangesAsync();

        var revalidator = new Svac.AdminHost.Domain.Auth.GrantTableStaffSessionRevalidator(adminDb);
        Assert.False(await revalidator.IsStillValid(actor.Id, stamp));
    }

    private static async Task<List<RecordedEvent>> CollectAuditEvents(CoreDbContext coreDb, string streamId)
    {
        var events = new List<RecordedEvent>();
        var eventStore = new PostgresEventStore(coreDb);
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, streamId))
        {
            events.Add(e);
        }
        return events;
    }
}
