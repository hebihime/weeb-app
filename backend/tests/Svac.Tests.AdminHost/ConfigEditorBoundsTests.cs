using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Policy;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §10.2: "bounds rejection leaves value+stream untouched through the editor path."
/// Exercises the SAME chokepoint the real Config Registry editor's form-post handler calls (Pass C) --
/// AdminActionExecutor.Execute with `work` = the identical <c>IConfigRegistry.SetValue</c> call the
/// editor makes -- never a direct/bypassed call to ConfigBounds or ConfigRegistry alone. See
/// AdminActionExecutorTests.cs for the AdminActionExecutor/GrantTableStaffRoleResolver INTERFACE-SKETCH
/// and the "constructed directly, no DI container" test convention this file follows.
/// </summary>
public sealed class ConfigEditorBoundsTests : IAsyncLifetime
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

    private static Svac.AdminHost.Domain.Execution.AdminActionExecutor NewExecutor(
        Svac.AdminHost.Domain.Persistence.AdminDbContext adminDb, Svac.DomainCore.Persistence.CoreDbContext coreDb)
    {
        var table = new PolicyTable(new IPolicyTableSource[] { new CorePolicyTableSource(), new Svac.AdminHost.Domain.Policy.AdminPolicyTableSource() });
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var policyEngine = new PolicyEngine(table, staffRoleResolver: new Svac.AdminHost.Domain.Policy.GrantTableStaffRoleResolver(adminDb));
        return new Svac.AdminHost.Domain.Execution.AdminActionExecutor(adminDb, coreDb, eventStore, policyEngine, table, configRegistry);
    }

    [Fact]
    public async Task Execute_OpsScopeEdit_OutOfBounds_ThrowsTheRegistrysOwnBoundsMessage_ValueAndStreamByteIdentical()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "admin.session_lifetime_hours"; // real v0-batch key: bounds [1,24] (SLICE_S5_CONTRACT.md §4)
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "8", requiresReason: false, boundsJson: "[1,24]");
        var (_, economyOps, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "economy_ops" }, "bounds-actor");
        var executor = NewExecutor(adminDb, coreDb);
        var configRegistry = new ConfigRegistry(coreDb, new PostgresEventStore(coreDb));
        var callerCtx = new RequestContext(economyOps, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", "bounds-1");

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => executor.Execute(
            callerCtx,
            "core.config.set.ops",
            new TargetRef("config_entry", key),
            reason: null, // this row does not require a reason -- the bounds rejection must fire regardless
            work: ctx => configRegistry.SetValue(key, 999, "bounds drill", ctx.Actor, ctx)));

        Assert.Contains("outside the declared bounds", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("[1,24]", thrown.Message, StringComparison.Ordinal);

        using var freshCoreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var unchanged = await freshCoreDb.ConfigEntries.SingleAsync(e => e.Key == key);
        Assert.Equal("8", unchanged.ValueJson); // byte-identical -- never touched

        var streamEvents = await AdminTestSupport.CountAuditEvents(freshCoreDb, key);
        Assert.Equal(0, streamEvents); // no config.set landed, no partial/failed-attempt event either
    }
}
