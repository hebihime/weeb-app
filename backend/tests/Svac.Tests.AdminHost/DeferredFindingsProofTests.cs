using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_PLAYBOOK's deferred-finding discipline (mirrors Svac.Tests.Identity.DeferredFindingsProofTests):
/// one Skip-annotated proof test per S5 DEFER row that is provable at the DI/direct-call level (no live
/// HTTP host needed) — each documents the exact finding + the shape that would fail (catching a
/// regression, or proving the gap) the moment someone un-Skips it. None of these are fixed in this pass —
/// see SECURITY_REVIEW_S5.md's DEFER table for the disposition rule and rationale.
/// </summary>
public sealed class DeferredFindingsProofTests : IAsyncLifetime
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

    private static PolicyTable RealPolicyTable(params IPolicyTableSource[] extraSources) =>
        new(new IPolicyTableSource[] { new CorePolicyTableSource(), new AdminPolicyTableSource() }.Concat(extraSources).ToArray());

    private static PolicyEngine RealPolicyEngine(IPolicyTable table, string connectionString) =>
        new(table, staffRoleResolver: new GrantTableStaffRoleResolver(AdminTestSupport.NewAdminDbFactory(connectionString)));

    private static AdminActionExecutor NewExecutor(AdminDbContext adminDb, CoreDbContext coreDb, string connectionString, IPolicyTable table)
    {
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var policyEngine = RealPolicyEngine(table, connectionString);
        return new AdminActionExecutor(adminDb, coreDb, eventStore, policyEngine, table, configRegistry);
    }

    private static RequestContext CallerCtx(ActorRef staff, string correlationId) => new(
        staff, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    // ------------------------------------------------------------------------------------------------
    // S5-08 (MEDIUM, Lens2): the four-eyes exemption at AdminActionExecutor step 5 keys off the COMPUTED
    // least-privileged hat (HatFor.SelectLeastPrivileged), never off "does this actor HOLD SuperAdmin at
    // all." A dual-role actor holding BOTH SuperAdmin and EconomyOps, acting on core.config.set.ops
    // (allowlist {SuperAdmin, EconomyOps}), computes hat=EconomyOps (the LEAST-privileged of the two ranks
    // that satisfy the row) — so hat != SuperAdmin, and an armed four-eyes switch incorrectly refuses a
    // genuinely-SuperAdmin actor. Fail-closed direction (over-refusal, never a privilege escalation), but
    // still a real usability/correctness gap the finding names explicitly.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-08 (four-eyes exemption keyed on the COMPUTED least-privileged hat, not \"holds SuperAdmin\" -- a dual-role SuperAdmin+EconomyOps actor is fail-closed over-refused) -> test on rolesHeld.Contains(SuperAdmin), not the computed hat")]
    public async Task FourEyesArmed_DualRoleSuperAdminAndEconomyOps_StillAllowed()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.admin.s5_08.dual_role_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "30", requiresReason: true);
        await AdminTestSupport.SeedConfigEntry(coreDb, "admin.four_eyes_required", "founder", "bool", "true", requiresReason: true);
        var (_, dualRoleActor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin", "economy_ops" }, "s5-08-dual-role");
        var table = RealPolicyTable();
        var executor = NewExecutor(adminDb, coreDb, ConnectionString, table);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));

        var result = await executor.Execute(
            CallerCtx(dualRoleActor, "s5-08-drill"),
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            "s5-08 drill",
            ctx => configRegistry.SetValue(key, 99, "s5-08 drill", ctx.Actor, ctx));

        // Desired: an actor who genuinely HOLDS SuperAdmin is never four-eyes-refused, regardless of
        // which allowlisted role the computed hat happens to land on. Today this returns FourEyesRequired
        // instead (the computed hat is EconomyOps, the least-privileged of the two ranks satisfying the row).
        Assert.IsType<AdminActionResult.Success>(result);
    }

    // ------------------------------------------------------------------------------------------------
    // S5-09 (LOW-latent, Lens2): two independently-reasonable "null means no-op" behaviors compose into a
    // total bypass. PolicyEngine.Authorize skips the Role axis ENTIRELY when a row's StaffRoles is null
    // (treats it as "no role restriction"). AdminActionExecutor's own hat computation, for a null-
    // StaffRoles row, uses AllRoleRanks as the allowlist — so a ZERO-grant staff actor's heldRanks
    // (empty) never intersects anything, hat comes back null. The four-eyes condition
    // (`hat is { } hatValue && ...`) then SHORT-CIRCUITS FALSE on a null hat -- four-eyes never fires
    // either. Composed: a hypothetical FUTURE PolicyTableEntry with StaffRoles=null AND
    // RequiresReason=true would let a staff actor holding ZERO grants reach work() completely ungated,
    // even with four-eyes armed. No SHIPPED row has StaffRoles=null AND RequiresReason=true today (every
    // admin.* row explicitly types StaffRoles; only admin.host.transport leaves it null, and that row's
    // RequiresReason is false) -- this test constructs the hypothetical row directly to prove the
    // composition, not a live regression.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-09 (null-StaffRoles Authorize skip composes with a null-hat four-eyes skip -- a future RequiresReason row with StaffRoles=null would let a zero-grant Staff commit with four-eyes armed; no shipped row hits it today) -> guard the composition")]
    public async Task NullStaffRolesRow_ZeroGrantStaff_DoesNotBypassFourEyes()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string hypotheticalAction = "test.admin.s5_09.null_staff_roles_action";
        const string key = "test.admin.s5_09.key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "1", requiresReason: true);
        await AdminTestSupport.SeedConfigEntry(coreDb, "admin.four_eyes_required", "founder", "bool", "true", requiresReason: true);

        // A staff row with ZERO active grants -- Authorize's own Role axis is a no-op for a
        // StaffRoles=null row, so this actor is NOT denied at step 4 despite holding nothing.
        var (_, zeroGrantActor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, Array.Empty<string>(), "s5-09-zero-grant");

        var hypotheticalRow = new PolicyTableEntry(
            Action: hypotheticalAction,
            ActorKinds: new HashSet<ActorKind> { ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true, // the exact composition the finding names
            ReasonKey: "policy.denied.s5_09_hypothetical",
            StaffRoles: null); // "no role restriction" by convention -- the exact seam this test exercises

        var hypotheticalSource = new InMemoryPolicyTableSource(hypotheticalRow);
        var table = RealPolicyTable(hypotheticalSource);
        var executor = NewExecutor(adminDb, coreDb, ConnectionString, table);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));
        var workInvoked = false;

        var result = await executor.Execute(
            CallerCtx(zeroGrantActor, "s5-09-drill"),
            hypotheticalAction,
            new TargetRef("config_entry", key),
            "s5-09 drill",
            ctx => { workInvoked = true; return configRegistry.SetValue(key, 2, "s5-09 drill", ctx.Actor, ctx); });

        // Desired: a zero-grant staff actor must never reach work() on a RequiresReason row with
        // four-eyes armed, regardless of the row's StaffRoles typing. Today this succeeds (work() runs)
        // because both "null means no-op" behaviors compose into a silent bypass.
        Assert.False(workInvoked);
        Assert.IsNotType<AdminActionResult.Success>(result);
    }

    private sealed class InMemoryPolicyTableSource(params PolicyTableEntry[] entries) : IPolicyTableSource
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = entries;
    }

    // ------------------------------------------------------------------------------------------------
    // S5-10 (LOW, Lens2): AdminActionExecutor.IsFourEyesArmed catches KeyNotFoundException and returns
    // false -- documented as safe because the unit tests below never seed the key and the real host always
    // seeds it at startup (v0=false). But that reasoning only covers "unseeded," never "dropped in prod"
    // (a manifest edit, a botched migration, a manual DELETE) -- either way the key going missing
    // SILENTLY DISARMS the control instead of failing closed, the opposite of every other fail-closed
    // guard in this file (ProdFieldKeyVaultGuard, ProdStaffAuthGuard, the S5-04 DataProtection guard above).
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-10 (IsFourEyesArmed swallows KeyNotFoundException -> false; a missing admin.four_eyes_required key silently DISARMS four-eyes instead of failing closed) -> fail closed on a missing key")]
    public async Task FourEyesArmed_MissingKey_FailsClosed()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.admin.s5_10.missing_four_eyes_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "30", requiresReason: true);
        // Deliberately NEVER seed "admin.four_eyes_required" -- simulates it being dropped/never seeded
        // in a real deploy, never merely "this unit test didn't bother."
        var (_, economyOps, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "economy_ops" }, "s5-10-actor");
        var table = RealPolicyTable();
        var executor = NewExecutor(adminDb, coreDb, ConnectionString, table);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));

        var result = await executor.Execute(
            CallerCtx(economyOps, "s5-10-drill"),
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            "s5-10 drill",
            ctx => configRegistry.SetValue(key, 99, "s5-10 drill", ctx.Actor, ctx));

        // Desired: a MISSING four-eyes key fails CLOSED (treated as armed) -- a non-SuperAdmin
        // RequiresReason action is refused exactly as if the key were explicitly true. Today it silently
        // succeeds instead (KeyNotFoundException -> false -> four-eyes never fires).
        Assert.IsType<AdminActionResult.FourEyesRequired>(result);
    }

    // ------------------------------------------------------------------------------------------------
    // S5-12 (LOW, Lens5 F2): ConfigRegistry.SetValue (domain-core, the ONE place either write path ever
    // touches the config table) has NO scope check at all -- the set-scope write-refusal rests entirely
    // on ConfigRegistryEndpointExtensions.HandlePropose's own single `entry.Scope == "set"` line.
    // HandleConfirm doesn't recheck scope either (it only re-verifies the sealed confirmToken's key/value/
    // reason triple). A hand-crafted call reaching SetValue directly for a set-scope key succeeds today.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-12 (ConfigRegistry.SetValue has no scope check; HandleConfirm never rechecks scope either -- set-scope write-refusal rests on a single HandlePropose line with no domain-layer depth) -> assert entry.Scope != \"set\" inside SetValue")]
    public async Task SetValue_SetScopeKey_Refused()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.admin.s5_12.set_scope_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "set", "int", "8", requiresReason: false);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));
        var systemActor = AdminTestSupport.SystemActor();
        var ctx = AdminTestSupport.SystemCtx("s5-12-drill");

        // Desired: SetValue itself refuses a set-scope key (defense in depth, independent of whatever the
        // editor's own HandlePropose check does elsewhere). Today it silently succeeds -- no exception,
        // the value actually changes.
        await Assert.ThrowsAsync<ArgumentException>(() => configRegistry.SetValue(key, 999, "s5-12 drill", systemActor, ctx));

        var unchanged = await coreDb.ConfigEntries.SingleAsync(e => e.Key == key);
        Assert.Equal("8", unchanged.ValueJson); // byte-identical -- never touched
    }
}
