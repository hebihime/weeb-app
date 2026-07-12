using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Persistence;

namespace Svac.Tests.AdminHost;

/// <summary>
/// Shared test-setup helpers for the Phase-2 deterministic gate suite (SLICE_S5_CONTRACT.md §10.2).
/// Every test file below spins its OWN Testcontainer Postgres (mirrors Svac.Tests.Architecture.
/// PurgeCompletenessIdentityTests / ConfigRegistryRealConsumerTests's per-class-container convention,
/// never the shared AdminHostFixture collection — that fixture boots the REAL Program.cs wiring
/// end-to-end for HTTP-shape proofs; these tests instead construct the real domain-core/admin-host
/// service TYPES directly, exactly like ConfigRegistryRealConsumerTests does for ConfigRegistry, so a
/// change to Pass A's Program.cs/DI wiring can never make these tests collide with BootHttpTests/
/// BootRefusalTests/DependencyInjectionTests over one shared fixture instance).
///
/// Directly inserting a staff/grant row here (SeedActiveStaff) is legitimate test SETUP, never a stand-in
/// for the flow under test (L30) — every test below exercises AdminActionExecutor/the sign-in pipeline/
/// the purge pipeline for real; only the STARTING fixture state is hand-placed, exactly like
/// PurgeCompletenessIdentityTests hand-places account rows before running the real purge pipeline.
/// </summary>
internal static class AdminTestSupport
{
    public static CoreDbContext NewCoreDb(string connectionString) =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(connectionString).Options);

    public static AdminDbContext NewAdminDb(string connectionString) =>
        new(new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(connectionString).Options);

    public static ActorRef SystemActor() =>
        new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    public static RequestContext SystemCtx(string correlationId) =>
        RequestContext.System(SystemActor(), correlationId);

    public static string FreshStaffId() => OpaqueId.New(IdPrefixes.Staff, DateTimeOffset.UtcNow, Random.Shared).ToString();

    public static string FreshGrantId() => OpaqueId.New(IdPrefixes.StaffRoleGrant, DateTimeOffset.UtcNow, Random.Shared).ToString();

    /// <summary>Inserts an ACTIVE staff row + the given active role grants, shaped exactly like a real
    /// AdminActionExecutor provision+grant would leave behind (SLICE_S5_CONTRACT.md §2).</summary>
    public static async Task<(string StaffId, ActorRef Actor, string SecurityStamp)> SeedActiveStaff(
        AdminDbContext adminDb, IReadOnlyList<string> roles, string tag, CancellationToken ct = default)
    {
        var staffId = FreshStaffId();
        var stamp = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        adminDb.StaffAccounts.Add(new StaffAccountEntity
        {
            Id = staffId,
            ExternalSubject = $"test:{tag}:{staffId}",
            Email = $"{tag}@devseams.svac.internal",
            DisplayName = tag,
            Status = "active",
            SecurityStamp = stamp,
            Region = "US",
            LawfulBasis = "contract",
            CreatedAt = now,
            UpdatedAt = now,
        });
        foreach (var role in roles)
        {
            adminDb.StaffRoleGrants.Add(new StaffRoleGrantEntity
            {
                Id = FreshGrantId(),
                StaffId = staffId,
                Role = role,
                GrantedBy = "sys_test-setup",
                GrantReason = "test setup",
                GrantedAt = now,
                Region = "US",
                LawfulBasis = "contract",
            });
        }
        await adminDb.SaveChangesAsync(ct);
        return (staffId, new ActorRef(OpaqueId.Parse(staffId), ActorKind.Staff), stamp);
    }

    /// <summary>Directly seeds a 9A config row, bypassing the manifest loader (legitimate test setup —
    /// the loader itself is proven elsewhere, e.g. Svac.Tests.Architecture.ConfigRegistryRealConsumerTests).</summary>
    public static async Task SeedConfigEntry(
        CoreDbContext coreDb, string key, string scope, string type, string valueJson,
        bool requiresReason, string? boundsJson = null, CancellationToken ct = default)
    {
        coreDb.ConfigEntries.Add(new ConfigEntryEntity
        {
            Key = key,
            Type = type,
            ValueJson = valueJson,
            Scope = scope,
            BoundsJson = boundsJson,
            RequiresReason = requiresReason,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "sys:test-setup",
        });
        await coreDb.SaveChangesAsync(ct);
    }

    public static async Task<int> CountAuditEvents(CoreDbContext coreDb, string streamId, string? eventType = null, CancellationToken ct = default) =>
        await coreDb.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit)
            .Where(e => e.StreamId == streamId && (eventType == null || e.EventType == eventType))
            .CountAsync(ct);
}
