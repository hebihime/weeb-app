using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

// ============================================================================================
// INTERFACE-SKETCH addendum (SLICE_S5_CONTRACT.md §1b: "Bootstrap (first SuperAdmin)"). See
// AdminActionExecutorTests.cs for the primary sketch + test conventions this file follows.
//
// namespace Svac.AdminHost.Domain.Bootstrap
// {
//     public sealed class StaffBootstrapper(AdminDbContext adminDb, Svac.DomainCore.Contracts.Streams.IEventStore eventStore)
//     {
//         // Only ever provisions when admin.staff_accounts is EMPTY (§1b: "one-shot: no bootstrap path
//         // exists once any account exists"). Returns true iff it provisioned this call, false = no-op.
//         // Provisions the staff row THEN the SuperAdmin grant, both as system-actor
//         // admin.action.executed envelope events (admin.staff.provision, admin.staff.role_grant) in the
//         // SAME transaction as their row inserts -- audited exactly like a founder-driven grant would
//         // be, per the SuperAdminOnly/StaffAndSystem policy rows (PHASE_2A_SUBSTRATE.md core-row typing;
//         // AdminPolicyTableSource §3: "system: bootstrap"). A concurrent double-call (two instances
//         // racing an empty table on cold boot) is idempotent-under-race: the loser's own INSERT hits the
//         // ux_staff_accounts_external_subject unique violation, is caught, and the call returns false
//         // rather than throwing or double-provisioning.
//         public Task<bool> BootstrapIfEmpty(string externalSubject, string email, string displayName, string region, CancellationToken ct = default);
//     }
// }
// ============================================================================================

public sealed class BootstrapTests : IAsyncLifetime
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

    [Fact]
    public async Task BootstrapIfEmpty_OnAnEmptyTable_ProvisionsOneSuperAdmin_BothActionsAudited()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var bootstrapper = new Svac.AdminHost.Domain.Bootstrap.StaffBootstrapper(adminDb, new PostgresEventStore(coreDb));

        var provisioned = await bootstrapper.BootstrapIfEmpty("devseams:superadmin", "founder@devseams.svac.internal", "Founder fixture", "US");
        Assert.True(provisioned);

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var staff = await freshAdminDb.StaffAccounts.SingleAsync(s => s.ExternalSubject == "devseams:superadmin");
        Assert.Equal("active", staff.Status);
        var grant = await freshAdminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staff.Id && g.RevokedAt == null);
        Assert.Equal("super_admin", grant.Role);
        Assert.StartsWith("sys", grant.GrantedBy, StringComparison.Ordinal); // a system actor, never a phantom staff ref

        var events = await CollectAuditEvents(coreDb, staff.Id);
        Assert.Contains(events, e => e.EventType == "admin.action.executed" && (e.PayloadJson ?? "").Contains("admin.staff.provision"));
        Assert.Contains(events, e => e.EventType == "admin.action.executed" && (e.PayloadJson ?? "").Contains("admin.staff.role_grant"));
    }

    [Fact]
    public async Task BootstrapIfEmpty_CalledASecondTime_IsANoOp_NoDuplicateRowOrEvent()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var bootstrapper = new Svac.AdminHost.Domain.Bootstrap.StaffBootstrapper(adminDb, new PostgresEventStore(coreDb));

        Assert.True(await bootstrapper.BootstrapIfEmpty("devseams:superadmin", "founder@devseams.svac.internal", "Founder fixture", "US"));
        Assert.False(await bootstrapper.BootstrapIfEmpty("devseams:superadmin", "founder@devseams.svac.internal", "Founder fixture", "US"));

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        Assert.Equal(1, await freshAdminDb.StaffAccounts.CountAsync());
        Assert.Equal(1, await freshAdminDb.StaffRoleGrants.CountAsync());
    }

    [Fact]
    public async Task BootstrapIfEmpty_CalledConcurrentlyByTwoColdBootInstances_ExactlyOneRowSurvives_NeitherThrowsUnhandled()
    {
        using var adminDbA = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbA = AdminTestSupport.NewCoreDb(ConnectionString);
        using var adminDbB = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbB = AdminTestSupport.NewCoreDb(ConnectionString);
        var bootstrapperA = new Svac.AdminHost.Domain.Bootstrap.StaffBootstrapper(adminDbA, new PostgresEventStore(coreDbA));
        var bootstrapperB = new Svac.AdminHost.Domain.Bootstrap.StaffBootstrapper(adminDbB, new PostgresEventStore(coreDbB));

        var taskA = bootstrapperA.BootstrapIfEmpty("devseams:superadmin", "founder@devseams.svac.internal", "Founder fixture", "US");
        var taskB = bootstrapperB.BootstrapIfEmpty("devseams:superadmin", "founder@devseams.svac.internal", "Founder fixture", "US");
        var results = await Task.WhenAll(taskA, taskB); // must not throw

        Assert.Contains(true, results);
        Assert.Single(results, r => r); // exactly one instance actually provisioned

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        Assert.Equal(1, await freshAdminDb.StaffAccounts.CountAsync());
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
