using Microsoft.EntityFrameworkCore;
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

// ============================================================================================
// INTERFACE-SKETCH (SLICE_S5_CONTRACT.md §1c/§1d, §8 seam 3) — the intended public surface Pass B
// (the AdminActionExecutor + Staff & Roles builder pass) implements. This is the ONE authoritative
// sketch for every new admin-host type this test tree references; other files in this project point
// back here rather than repeating it. None of these types exist yet (RED, by design, per BUILD.md §8/
// SLICE_PLAYBOOK L30) — every test below is written to compile against EXACTLY this shape.
//
// namespace Svac.AdminHost.Domain.Execution
// {
//     // The closed outcome union — mirrors PolicyDecision's own closed-union shape (Svac.DomainCore.
//     // Contracts.Policy.PolicyDecision) so callers pattern-match, never string-sniff.
//     public abstract record AdminActionResult
//     {
//         // ctx.Staff is populated (hat + full roles_held snapshot) — the SAME enriched context `work`
//         // itself received, handed back so the caller (a Razor page) can render "acting as <hat>".
//         public sealed record Success(RequestContext Context) : AdminActionResult;
//         // Refused BEFORE Authorize (§1c executor doc: "whitespace = refused before Authorize") — a
//         // requires_reason row given a null/whitespace reason. NEVER audited (nothing chokepoint-worthy
//         // was even attempted) and `work` is NEVER invoked.
//         public sealed record ReasonRequired : AdminActionResult;
//         // policyEngine.Authorize denied. `work` NEVER invoked. Audited: admin.action.refused (metadata
//         // only — action/target_ref/hat/roles_held/reason, never a second copy of `work`'s own effects,
//         // because work never ran).
//         public sealed record Denied(string ReasonKey) : AdminActionResult;
//         // 9A admin.four_eyes_required is true AND the computed hat != StaffRole.SuperAdmin AND the
//         // row RequiresReason — fail-closed placeholder (§4). `work` NEVER invoked. Audited exactly like
//         // Denied (same admin.action.refused shape) — a four-eyes refusal IS a refusal.
//         public sealed record FourEyesRequired : AdminActionResult;
//     }
//
//     public interface IAdminActionExecutor
//     {
//         // callerCtx.Actor MUST be ActorKind.Staff (or ActorKind.System for the two bootstrap-capable
//         // actions, §3) — enforced by throwing ArgumentException on any other kind, never a silent deny
//         // (a non-staff caller reaching this door at all is a caller bug, not a policy question).
//         // callerCtx.Staff is IGNORED on input (overwritten internally after the fresh re-read, step 2)
//         // — callers never get to assert their own hat.
//         //
//         // Sequence (§1c, ONE EF transaction spanning AdminDbContext + CoreDbContext end to end, shared
//         // via DbTransaction.UseTransaction on both contexts so `work`'s own SaveChangesAsync and this
//         // executor's own audit-event SaveChangesAsync commit or roll back together):
//         //   1. re-read staff row (active? stamp?) + grants from AdminDbContext — revocation bites now.
//         //      Inactive/missing row -> Denied("policy.denied.admin_actor_inactive"), audited, `work`
//         //      never invoked (SLICE_S5_CONTRACT.md §1b law 2: "AdminActionExecutor re-reads status +
//         //      grants from the DB on EVERY action -- revocation bites at the next mutation regardless").
//         //   2. hat = HatFor.SelectLeastPrivileged(heldRanks, policyTable.Find(action).StaffRoles ranks);
//         //      ctx = callerCtx with { Staff = new StaffContext(staffId, rolesHeld, hat) }.
//         //   3. reason precheck: row.RequiresReason && string.IsNullOrWhiteSpace(reason) ->
//         //      ReasonRequired, returned immediately (NOT audited, `work` never invoked) — resolves the
//         //      contract's own two orderings (the executor doc comment says "before Authorize"; the
//         //      numbered list says step 5 — this sketch is the tie-break Pass B implements to).
//         //   4. await policyEngine.Authorize(ctx.Actor, action, target) — deny -> Denied(reasonKey),
//         //      audited admin.action.refused, `work` never invoked.
//         //   5. four-eyes: if configRegistry.GetValue<bool>("admin.four_eyes_required") && hat !=
//         //      SuperAdmin && row.RequiresReason -> FourEyesRequired, audited admin.action.refused,
//         //      `work` never invoked.
//         //   6. await work(ctx) — the domain mutation. `work` is authored by the SAME DI scope that
//         //      resolved this executor, so a DbContext `work` captures (AdminDbContext, or
//         //      CoreDbContext via IConfigRegistry for config.set actions) IS the same tracked instance
//         //      this executor shares a transaction with -- `work` calling its own SaveChangesAsync
//         //      flushes onto the SAME open transaction, never a separate commit.
//         //   7. exactly ONE audit event: actions matching "core.config.set.*" are SELF-LOGGING (the
//         //      work callback's own IConfigRegistry.SetValue call already appended+enriched a
//         //      config.set event via ctx.Staff, PHASE_2A_SUBSTRATE.md §1 core-row typing) -- the
//         //      executor appends NOTHING further for those. Every OTHER action key gets ONE
//         //      admin.action.executed envelope {action, target_ref, hat, roles_held, reason} appended
//         //      via IEventStore.Append, stream_id = target.ResourceId for user/staff-impacting actions
//         //      (grant/revoke/deactivate/reactivate target the AFFECTED staff_account; provision targets
//         //      the newly-minted staff_account id).
//         //   8. commit the shared transaction; return Success(ctx).
//         public Task<AdminActionResult> Execute(
//             RequestContext callerCtx,
//             string action,
//             TargetRef target,
//             string? reason,
//             Func<RequestContext, Task> work,
//             CancellationToken ct = default);
//     }
//
//     // Concrete constructor shape this test tree resolves directly (no DI container — mirrors
//     // Svac.Tests.Architecture.ConfigRegistryRealConsumerTests's "new ConfigRegistry(db, eventStore)"
//     // convention). adminDb + coreDb are the TWO contexts the executor shares one transaction across;
//     // eventStore/configRegistry/policyEngine/policyTable are constructed over that SAME coreDb
//     // instance by the caller (mirrors every other domain-core service's own constructor convention).
//     public sealed class AdminActionExecutor(
//         AdminDbContext adminDb,
//         CoreDbContext coreDb,
//         IEventStore eventStore,
//         IPolicyEngine policyEngine,
//         IPolicyTable policyTable,
//         Svac.DomainCore.Contracts.Config.IConfigRegistry configRegistry) : IAdminActionExecutor { /* ... */ }
// }
//
// namespace Svac.AdminHost.Domain.Policy
// {
//     // The grant-table-backed IStaffRoleResolver override AddAdminHostModule registers (Phase 2),
//     // superseding the domain-core DenyAllStaffRoleResolver default on THIS host only.
//     public sealed class GrantTableStaffRoleResolver(AdminDbContext adminDb) : IStaffRoleResolver { /* ... */ }
// }
// ============================================================================================

public sealed class AdminActionExecutorTests : IAsyncLifetime
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

    // Builds the REAL union (CorePolicyTableSource + AdminPolicyTableSource) + the REAL grant-table
    // resolver over the SAME adminDb the executor itself uses — never a hand-rolled test double
    // standing in for the Role axis (L30: exercise the real chokepoint).
    private static PolicyTable RealPolicyTable() =>
        new PolicyTable(new IPolicyTableSource[]
        {
            new CorePolicyTableSource(),
            new Svac.AdminHost.Domain.Policy.AdminPolicyTableSource(),
        });

    private static PolicyEngine RealPolicyEngine(IPolicyTable table, AdminDbContext adminDb) =>
        new PolicyEngine(table, staffRoleResolver: new Svac.AdminHost.Domain.Policy.GrantTableStaffRoleResolver(adminDb));

    private static Svac.AdminHost.Domain.Execution.AdminActionExecutor NewExecutor(AdminDbContext adminDb, CoreDbContext coreDb)
    {
        var table = RealPolicyTable();
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var policyEngine = RealPolicyEngine(table, adminDb);
        return new Svac.AdminHost.Domain.Execution.AdminActionExecutor(adminDb, coreDb, eventStore, policyEngine, table, configRegistry);
    }

    private static RequestContext CallerCtx(ActorRef staff, string correlationId) => new(
        staff, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    // -------------------- tx-atomicity --------------------

    [Fact]
    public async Task Execute_WhenWorkThrowsAfterFlushingItsOwnMutation_RollsBackBothTheMutationAndAnyEvent()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "atomicity-target");
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "atomicity-actor");

        var executor = NewExecutor(adminDb, coreDb);
        var target = new TargetRef("staff_account", targetId);

        // "kill the tx after work()" (§10.2): work() mutates + FLUSHES (SaveChangesAsync, joining the
        // executor's own shared transaction) and THEN throws — simulating a failure discovered only
        // after the domain mutation already hit the wire, the exact case a bare "SaveChanges, then
        // append the event separately" implementation would get wrong.
        var thrown = await Record.ExceptionAsync(() => executor.Execute(
            CallerCtx(superAdmin, "atomicity-1"),
            "admin.staff.deactivate",
            target,
            "atomicity drill",
            async ctx =>
            {
                var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == targetId);
                row.Status = "deactivated";
                row.DeactivatedAt = DateTimeOffset.UtcNow;
                await adminDb.SaveChangesAsync(); // flushed onto the shared tx, NOT yet committed
                throw new InvalidOperationException("simulated post-flush failure — the tx must still roll back");
            }));

        Assert.NotNull(thrown);

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var reread = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == targetId);
        Assert.Equal("active", reread.Status); // the mutation never committed
        Assert.Null(reread.DeactivatedAt);

        using var freshCoreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var eventCount = await AdminTestSupport.CountAuditEvents(freshCoreDb, targetId);
        Assert.Equal(0, eventCount); // no admin.action.executed, no admin.action.refused — nothing committed
    }

    // -------------------- one-event-per-action --------------------

    [Fact]
    public async Task Execute_OnAVerbLessAction_AppendsExactlyOneAdminActionExecutedEnvelope()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "one-event-target");
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "one-event-actor");
        var executor = NewExecutor(adminDb, coreDb);
        var target = new TargetRef("staff_account", targetId);

        var result = await executor.Execute(
            CallerCtx(superAdmin, "one-event-1"),
            "admin.staff.deactivate",
            target,
            "one-event drill",
            async ctx =>
            {
                var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == targetId);
                row.Status = "deactivated";
                await adminDb.SaveChangesAsync();
            });

        Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.Success>(result);

        var events = await CollectAuditEvents(coreDb, targetId);
        var envelope = Assert.Single(events, e => e.EventType == "admin.action.executed");
        Assert.Contains("\"admin.staff.deactivate\"", envelope.PayloadJson);
        Assert.Contains("\"hat\":\"SuperAdmin\"", envelope.PayloadJson);
    }

    [Fact]
    public async Task Execute_OnConfigSetOps_NeverDoubleLogs_TheEnrichedConfigSetEventIsTheOnlyRow()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.admin.one_event.ops_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "30", requiresReason: true);
        var (_, economyOps, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "economy_ops" }, "config-actor");
        var executor = NewExecutor(adminDb, coreDb);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));

        var result = await executor.Execute(
            CallerCtx(economyOps, "config-set-1"),
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            "config drill",
            ctx => configRegistry.SetValue(key, 45, "config drill", ctx.Actor, ctx));

        Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.Success>(result);

        var events = await CollectAuditEvents(coreDb, key);
        var single = Assert.Single(events); // NEVER two rows for one config.set action
        Assert.Equal("config.set", single.EventType);
        Assert.Contains("\"hat\":\"EconomyOps\"", single.PayloadJson); // Phase-2a enrichment, real, not fabricated
        Assert.Contains("\"roles_held\":[\"EconomyOps\"]", single.PayloadJson);
        Assert.DoesNotContain(events, e => e.EventType == "admin.action.executed"); // no parallel envelope
    }

    // -------------------- reason-refusal --------------------

    [Fact]
    public async Task Execute_RequiresReasonRowWithWhitespaceReason_RefusesBeforeAuthorize_NoAuditNoWork()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "reason-target");
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "reason-actor");
        var executor = NewExecutor(adminDb, coreDb);
        var workInvoked = false;

        var result = await executor.Execute(
            CallerCtx(superAdmin, "reason-1"),
            "admin.staff.deactivate",
            new TargetRef("staff_account", targetId),
            reason: "   ",
            work: _ => { workInvoked = true; return Task.CompletedTask; });

        Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.ReasonRequired>(result);
        Assert.False(workInvoked, "a whitespace reason on a requires_reason row must refuse before work() ever runs");

        var events = await CollectAuditEvents(coreDb, targetId);
        Assert.Empty(events); // §1c: refused before Authorize -- not even admin.action.refused fires here
    }

    // -------------------- four-eyes --------------------

    [Fact]
    public async Task Execute_FourEyesArmed_NonSuperAdminHatOnARequiresReasonAction_RefusesFailClosed()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.admin.four_eyes.ops_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "30", requiresReason: true);
        await AdminTestSupport.SeedConfigEntry(coreDb, "admin.four_eyes_required", "founder", "bool", "true", requiresReason: true);
        var (_, economyOps, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "economy_ops" }, "four-eyes-actor");
        var executor = NewExecutor(adminDb, coreDb);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));
        var workInvoked = false;

        var result = await executor.Execute(
            CallerCtx(economyOps, "four-eyes-1"),
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            "four-eyes drill",
            ctx => { workInvoked = true; return configRegistry.SetValue(key, 99, "four-eyes drill", ctx.Actor, ctx); });

        // EconomyOps IS in core.config.set.ops's StaffRoles allowlist -- Authorize alone would ALLOW
        // this. Four-eyes, armed, refuses it anyway because the acting hat != SuperAdmin.
        Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.FourEyesRequired>(result);
        Assert.False(workInvoked);

        using var freshCoreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var unchanged = await freshCoreDb.ConfigEntries.SingleAsync(e => e.Key == key);
        Assert.Equal("30", unchanged.ValueJson); // byte-identical -- refused, never mutated
    }

    [Fact]
    public async Task Execute_FourEyesArmed_SuperAdminHat_StillAllowed_NotBlockedByItsOwnSwitch()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.admin.four_eyes.superadmin_ok_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "30", requiresReason: true);
        await AdminTestSupport.SeedConfigEntry(coreDb, "admin.four_eyes_required", "founder", "bool", "true", requiresReason: true);
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "four-eyes-super");
        var executor = NewExecutor(adminDb, coreDb);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));

        var result = await executor.Execute(
            CallerCtx(superAdmin, "four-eyes-2"),
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            "four-eyes drill (superadmin)",
            ctx => configRegistry.SetValue(key, 99, "four-eyes drill (superadmin)", ctx.Actor, ctx));

        Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.Success>(result);
    }

    // -------------------- deactivation bites the very next action --------------------

    [Fact]
    public async Task Execute_ReReadsStaffRowEveryCall_ADeactivationThatHappenedMidSession_DeniesTheVeryNextAction()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (staffId, staffActor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "economy_ops" }, "revoke-victim");
        const string key = "test.admin.revoke.ops_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "1", requiresReason: true);
        var executor = NewExecutor(adminDb, coreDb);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));

        // The cookie-session's cached ActorRef is UNCHANGED (mirrors "the live session, mid-session") --
        // only the DB row changes, exactly like a SuperAdmin deactivating them from another browser tab.
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        row.Status = "deactivated";
        row.SecurityStamp = Guid.NewGuid().ToString("N");
        await adminDb.SaveChangesAsync();

        var result = await executor.Execute(
            CallerCtx(staffActor, "revoke-1"),
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            "post-deactivation attempt",
            ctx => configRegistry.SetValue(key, 2, "post-deactivation attempt", ctx.Actor, ctx));

        Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.Denied>(result);
        var unchanged = await coreDb.ConfigEntries.SingleAsync(e => e.Key == key);
        Assert.Equal("1", unchanged.ValueJson);
    }

    // -------------------- grant-race idempotency --------------------

    [Fact]
    public async Task Execute_TwoConcurrentRoleGrantsForTheSameStaffAndRole_ExactlyOneActiveRowSurvives_NeitherThrowsUnhandled()
    {
        using var setupDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbSetup = AdminTestSupport.NewCoreDb(ConnectionString);
        var (targetId, _, _) = await AdminTestSupport.SeedActiveStaff(setupDb, Array.Empty<string>(), "grant-race-target");
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(setupDb, new[] { "super_admin" }, "grant-race-actor");

        using var adminDbA = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbA = AdminTestSupport.NewCoreDb(ConnectionString);
        using var adminDbB = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDbB = AdminTestSupport.NewCoreDb(ConnectionString);
        var executorA = NewExecutor(adminDbA, coreDbA);
        var executorB = NewExecutor(adminDbB, coreDbB);
        var target = new TargetRef("staff_account", targetId);

        Func<RequestContext, Task> Grant(AdminDbContext db) => async _ =>
        {
            db.StaffRoleGrants.Add(new Svac.AdminHost.Domain.Persistence.StaffRoleGrantEntity
            {
                Id = AdminTestSupport.FreshGrantId(),
                StaffId = targetId,
                Role = "economy_ops",
                GrantedBy = superAdmin.Id.ToString(),
                GrantReason = "grant-race drill",
                GrantedAt = DateTimeOffset.UtcNow,
                Region = "US",
                LawfulBasis = "contract",
            });
            await db.SaveChangesAsync();
        };

        var taskA = executorA.Execute(CallerCtx(superAdmin, "grant-race-a"), "admin.staff.role_grant", target, "grant-race drill", Grant(adminDbA));
        var taskB = executorB.Execute(CallerCtx(superAdmin, "grant-race-b"), "admin.staff.role_grant", target, "grant-race drill", Grant(adminDbB));

        var results = await Task.WhenAll(taskA, taskB); // neither call may throw unhandled — the race is idempotent, not a crash
        Assert.All(results, r => Assert.IsType<Svac.AdminHost.Domain.Execution.AdminActionResult.Success>(r));

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var activeGrants = await freshAdminDb.StaffRoleGrants
            .Where(g => g.StaffId == targetId && g.Role == "economy_ops" && g.RevokedAt == null)
            .ToListAsync();
        Assert.Single(activeGrants); // ux_active_grant held: exactly one active grant survives the race
    }

    private static async Task<List<Svac.DomainCore.Contracts.Streams.RecordedEvent>> CollectAuditEvents(CoreDbContext coreDb, string streamId)
    {
        var events = new List<Svac.DomainCore.Contracts.Streams.RecordedEvent>();
        var eventStore = new PostgresEventStore(coreDb);
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, streamId))
        {
            events.Add(e);
        }
        return events;
    }
}
