using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SECURITY_REVIEW_S5.md S5-03 (fixNow, CRITICAL->lockout): before this fix, NOTHING stopped a
/// self-deactivate or a self-revoke of <c>super_admin</c> from dropping the active-SuperAdmin count to
/// zero — with no in-app recovery path (every other admin.staff.* action requires SuperAdmin, §3's own
/// StaffRoles allowlists). This file exercises the new executor step directly against
/// <see cref="AdminActionExecutor"/> (never a stand-in), including the race-safety claim under REAL
/// concurrent Postgres transactions — mirrors AdminActionExecutorTests.cs's own
/// "TwoConcurrentRoleGrants...NeitherThrowsUnhandled" convention for exercising two executors against the
/// SAME database at once.
/// </summary>
public sealed class LastSuperAdminLockoutGuardTests : IAsyncLifetime
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

    private static PolicyTable RealPolicyTable() =>
        new(new IPolicyTableSource[]
        {
            new CorePolicyTableSource(),
            new Svac.AdminHost.Domain.Policy.AdminPolicyTableSource(),
        });

    private static PolicyEngine RealPolicyEngine(IPolicyTable table, string connectionString) =>
        new(table, staffRoleResolver: new Svac.AdminHost.Domain.Policy.GrantTableStaffRoleResolver(AdminTestSupport.NewAdminDbFactory(connectionString)));

    private static AdminActionExecutor NewExecutor(AdminDbContext adminDb, CoreDbContext coreDb, string connectionString)
    {
        var table = RealPolicyTable();
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var policyEngine = RealPolicyEngine(table, connectionString);
        return new AdminActionExecutor(adminDb, coreDb, eventStore, policyEngine, table, configRegistry);
    }

    private static RequestContext CallerCtx(ActorRef staff, string correlationId) => new(
        staff, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    private static async Task<int> ActiveSuperAdminCount(AdminDbContext adminDb) =>
        await adminDb.StaffRoleGrants
            .Where(g => g.Role == "super_admin" && g.RevokedAt == null)
            .Join(adminDb.StaffAccounts.Where(a => a.Status == "active"), g => g.StaffId, a => a.Id, (g, a) => a.Id)
            .CountAsync();

    // -------------------- deactivate --------------------

    [Fact]
    public async Task Deactivate_TheLastActiveSuperAdmin_SelfDeactivate_IsDenied_RowStaysActive()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (staffId, actor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "lone-superadmin");
        var executor = NewExecutor(adminDb, coreDb, ConnectionString);
        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(
            CallerCtx(actor, "s5-03-deactivate-lone"),
            "admin.staff.deactivate",
            target,
            "attempted self-deactivate of the only SuperAdmin",
            async _ =>
            {
                var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
                row.Status = "deactivated";
                await adminDb.SaveChangesAsync();
            });

        var denied = Assert.IsType<AdminActionResult.Denied>(result);
        Assert.Equal("policy.denied.last_superadmin", denied.ReasonKey);

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var reread = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        Assert.Equal("active", reread.Status); // never mutated

        var events = await AdminTestSupport.CountAuditEvents(coreDb, staffId, "admin.action.refused");
        Assert.Equal(1, events); // the refusal IS audited, exactly like every other admin.action.refused
    }

    [Fact]
    public async Task Deactivate_ASuperAdmin_WithASecondActiveSuperAdmin_Succeeds()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "superadmin-target");
        var (_, actingSuperAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "superadmin-actor");
        var executor = NewExecutor(adminDb, coreDb, ConnectionString);
        var target = new TargetRef("staff_account", targetId);

        var result = await executor.Execute(
            CallerCtx(actingSuperAdmin, "s5-03-deactivate-second"),
            "admin.staff.deactivate",
            target,
            "deactivating one of two SuperAdmins is fine",
            async _ =>
            {
                var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == targetId);
                row.Status = "deactivated";
                await adminDb.SaveChangesAsync();
            });

        Assert.IsType<AdminActionResult.Success>(result);
        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var reread = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == targetId);
        Assert.Equal("deactivated", reread.Status);
    }

    [Fact]
    public async Task Deactivate_AStaffRowThatHoldsNoSuperAdminGrant_IsNeverBlockedByThisGuard()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        // The TARGET holds no super_admin grant at all (only the acting caller does, so Authorize on
        // admin.staff.deactivate itself passes) -- the guard must be a complete no-op regardless of how
        // many real SuperAdmins exist elsewhere, because deactivating THIS row can never move that count.
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "no-superadmin-target");
        var (_, actingSuperAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, Array.Empty<string>(), "no-role-actor");
        // Bootstrap this actor's OWN grant separately so Authorize itself passes without touching the
        // scenario under test (a SuperAdmin acting on a SafetyAgent target).
        adminDb.StaffRoleGrants.Add(new Svac.AdminHost.Domain.Persistence.StaffRoleGrantEntity
        {
            Id = AdminTestSupport.FreshGrantId(),
            StaffId = actingSuperAdmin.Id.ToString(),
            Role = "super_admin",
            GrantedBy = "sys_test-setup",
            GrantReason = "test setup",
            GrantedAt = DateTimeOffset.UtcNow,
            Region = "US",
            LawfulBasis = "contract",
        });
        await adminDb.SaveChangesAsync();

        var executor = NewExecutor(adminDb, coreDb, ConnectionString);
        var target = new TargetRef("staff_account", targetId);

        var result = await executor.Execute(
            CallerCtx(actingSuperAdmin, "s5-03-deactivate-non-superadmin"),
            "admin.staff.deactivate",
            target,
            "deactivating a SafetyAgent never touches the SuperAdmin headcount",
            async _ =>
            {
                var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == targetId);
                row.Status = "deactivated";
                await adminDb.SaveChangesAsync();
            });

        Assert.IsType<AdminActionResult.Success>(result);
    }

    // -------------------- role_revoke --------------------

    [Fact]
    public async Task RoleRevoke_SuperAdminRole_FromTheLastActiveSuperAdmin_SelfRevoke_IsDenied_GrantStaysActive()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (staffId, actor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "lone-superadmin-revoke");
        var executor = NewExecutor(adminDb, coreDb, ConnectionString);
        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(
            CallerCtx(actor, "s5-03-revoke-lone"),
            "admin.staff.role_revoke",
            target,
            "attempted self-revoke of the only SuperAdmin's own super_admin grant",
            async ctx =>
            {
                var grant = await adminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId && g.Role == "super_admin" && g.RevokedAt == null);
                grant.RevokedAt = DateTimeOffset.UtcNow;
                grant.RevokedBy = ctx.Actor.Id.ToString();
                await adminDb.SaveChangesAsync();
            },
            affectedRoleCode: "super_admin");

        var denied = Assert.IsType<AdminActionResult.Denied>(result);
        Assert.Equal("policy.denied.last_superadmin", denied.ReasonKey);

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var stillActive = await freshAdminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId && g.Role == "super_admin");
        Assert.Null(stillActive.RevokedAt); // never revoked
    }

    [Fact]
    public async Task RoleRevoke_SuperAdminRole_WithASecondActiveSuperAdmin_Succeeds()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "revoke-target-of-two");
        var (_, actingSuperAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "revoke-actor-of-two");
        var executor = NewExecutor(adminDb, coreDb, ConnectionString);
        var target = new TargetRef("staff_account", targetId);

        var result = await executor.Execute(
            CallerCtx(actingSuperAdmin, "s5-03-revoke-second"),
            "admin.staff.role_revoke",
            target,
            "revoking one of two SuperAdmins' grant is fine",
            async ctx =>
            {
                var grant = await adminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == targetId && g.Role == "super_admin" && g.RevokedAt == null);
                grant.RevokedAt = DateTimeOffset.UtcNow;
                grant.RevokedBy = ctx.Actor.Id.ToString();
                await adminDb.SaveChangesAsync();
            },
            affectedRoleCode: "super_admin");

        Assert.IsType<AdminActionResult.Success>(result);
        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var revoked = await freshAdminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == targetId && g.Role == "super_admin");
        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task RoleRevoke_ANonSuperAdminRole_OnTheLastActiveSuperAdmin_IsNeverBlockedByThisGuard()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        // The LONE SuperAdmin also holds economy_ops -- revoking economy_ops must NEVER be blocked by the
        // last-superadmin guard, only a revoke of super_admin itself. Proves affectedRoleCode's own
        // precision: without it, a naive "is this staffer the last SuperAdmin" check would incorrectly
        // deny this entirely unrelated revoke.
        var (staffId, actor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin", "economy_ops" }, "dual-role-lone-superadmin");
        var executor = NewExecutor(adminDb, coreDb, ConnectionString);
        var target = new TargetRef("staff_account", staffId);

        var result = await executor.Execute(
            CallerCtx(actor, "s5-03-revoke-non-superadmin-role"),
            "admin.staff.role_revoke",
            target,
            "revoking economy_ops from the lone SuperAdmin never touches the SuperAdmin headcount",
            async ctx =>
            {
                var grant = await adminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId && g.Role == "economy_ops" && g.RevokedAt == null);
                grant.RevokedAt = DateTimeOffset.UtcNow;
                grant.RevokedBy = ctx.Actor.Id.ToString();
                await adminDb.SaveChangesAsync();
            },
            affectedRoleCode: "economy_ops");

        Assert.IsType<AdminActionResult.Success>(result);
        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var stillActiveSuperAdmin = await freshAdminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId && g.Role == "super_admin");
        Assert.Null(stillActiveSuperAdmin.RevokedAt); // untouched
        var revokedEconomyOps = await freshAdminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId && g.Role == "economy_ops");
        Assert.NotNull(revokedEconomyOps.RevokedAt);
    }

    // -------------------- race safety --------------------

    [Fact]
    public async Task RoleRevoke_TwoConcurrentSelfRevokesOfTheLastTwoSuperAdmins_ExactlyOneSucceeds_NeitherThrowsUnhandled_NeverZero()
    {
        using var setupDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffAId, actorA, _) = await AdminTestSupport.SeedActiveStaff(setupDb, new[] { "super_admin" }, "race-superadmin-a");
        var (staffBId, actorB, _) = await AdminTestSupport.SeedActiveStaff(setupDb, new[] { "super_admin" }, "race-superadmin-b");

        using var adminDbA = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbA = AdminTestSupport.NewCoreDb(ConnectionString);
        using var adminDbB = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbB = AdminTestSupport.NewCoreDb(ConnectionString);
        var executorA = NewExecutor(adminDbA, coreDbA, ConnectionString);
        var executorB = NewExecutor(adminDbB, coreDbB, ConnectionString);

        // A self-revokes A's OWN super_admin grant; B (concurrently) self-revokes B's OWN -- deliberately
        // SELF, never mutual: a mutual "A revokes B / B revokes A" design confounds the proof, because
        // whichever commits FIRST strips the LOSER's OWN acting authority, so the loser would be denied
        // by ORDINARY Authorize (no longer holding SuperAdmin) regardless of whether this guard's lock
        // does anything at all. Self-revoke isolates the guard: neither actor's OWN grant is touched by
        // the OTHER's transaction, so ONLY the shared active-SuperAdmin-count invariant (guarded by the
        // FOR UPDATE lock below) can determine which of the two commits.
        Func<AdminDbContext, ActorRef, Task> SelfRevoke(string targetStaffId) => async (db, actor) =>
        {
            var grant = await db.StaffRoleGrants.SingleAsync(g => g.StaffId == targetStaffId && g.Role == "super_admin" && g.RevokedAt == null);
            grant.RevokedAt = DateTimeOffset.UtcNow;
            grant.RevokedBy = actor.Id.ToString();
            await db.SaveChangesAsync();
        };

        var taskA = executorA.Execute(
            CallerCtx(actorA, "s5-03-race-a"), "admin.staff.role_revoke", new TargetRef("staff_account", staffAId),
            "A self-revokes", ctx => SelfRevoke(staffAId)(adminDbA, ctx.Actor), affectedRoleCode: "super_admin");
        var taskB = executorB.Execute(
            CallerCtx(actorB, "s5-03-race-b"), "admin.staff.role_revoke", new TargetRef("staff_account", staffBId),
            "B self-revokes", ctx => SelfRevoke(staffBId)(adminDbB, ctx.Actor), affectedRoleCode: "super_admin");

        var results = await Task.WhenAll(taskA, taskB); // neither call may throw unhandled

        var successCount = results.Count(r => r is AdminActionResult.Success);
        var deniedCount = results.Count(r => r is AdminActionResult.Denied denied && denied.ReasonKey == "policy.denied.last_superadmin");
        Assert.Equal(1, successCount); // exactly one self-revoke wins
        Assert.Equal(1, deniedCount); // the other is denied BY THIS GUARD specifically -- never both succeeding

        using var finalAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var remainingActive = await ActiveSuperAdminCount(finalAdminDb);
        Assert.Equal(1, remainingActive); // never zero, never two -- exactly one SuperAdmin survives
    }

    [Fact]
    public async Task Deactivate_TwoConcurrentSelfDeactivationsOfTheLastTwoSuperAdmins_ExactlyOneSucceeds_NeverZero()
    {
        using var setupDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffAId, actorA, _) = await AdminTestSupport.SeedActiveStaff(setupDb, new[] { "super_admin" }, "race-deactivate-a");
        var (staffBId, actorB, _) = await AdminTestSupport.SeedActiveStaff(setupDb, new[] { "super_admin" }, "race-deactivate-b");

        using var adminDbA = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbA = AdminTestSupport.NewCoreDb(ConnectionString);
        using var adminDbB = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbB = AdminTestSupport.NewCoreDb(ConnectionString);
        var executorA = NewExecutor(adminDbA, coreDbA, ConnectionString);
        var executorB = NewExecutor(adminDbB, coreDbB, ConnectionString);

        // Deliberately SELF-deactivate, never mutual (the SAME confound the revoke race test's own
        // comment explains): a mutual "A deactivates B / B deactivates A" design would let the LOSER be
        // denied by the executor's own step-1 "is the CALLING actor's row still active" re-read (the
        // winner's commit already flipped the loser's OWN status) -- a real, but DIFFERENT, denial path
        // that would mask whether this guard's FOR UPDATE lock did anything at all. Self-deactivate
        // isolates it: neither actor's own status is touched by the other's transaction.
        var taskA = executorA.Execute(
            CallerCtx(actorA, "s5-03-deactivate-race-a"), "admin.staff.deactivate", new TargetRef("staff_account", staffAId),
            "A self-deactivates", async _ =>
            {
                var row = await adminDbA.StaffAccounts.SingleAsync(s => s.Id == staffAId);
                row.Status = "deactivated";
                await adminDbA.SaveChangesAsync();
            });
        var taskB = executorB.Execute(
            CallerCtx(actorB, "s5-03-deactivate-race-b"), "admin.staff.deactivate", new TargetRef("staff_account", staffBId),
            "B self-deactivates", async _ =>
            {
                var row = await adminDbB.StaffAccounts.SingleAsync(s => s.Id == staffBId);
                row.Status = "deactivated";
                await adminDbB.SaveChangesAsync();
            });

        var results = await Task.WhenAll(taskA, taskB);

        var successCount = results.Count(r => r is AdminActionResult.Success);
        var deniedCount = results.Count(r => r is AdminActionResult.Denied denied && denied.ReasonKey == "policy.denied.last_superadmin");
        Assert.Equal(1, successCount);
        Assert.Equal(1, deniedCount); // the other is denied BY THIS GUARD specifically

        using var finalAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var remainingActive = await ActiveSuperAdminCount(finalAdminDb);
        Assert.Equal(1, remainingActive);
    }
}
