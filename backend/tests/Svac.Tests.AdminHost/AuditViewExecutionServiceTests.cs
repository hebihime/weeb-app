using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Audit;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Audit;
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
/// SLICE_S5_CONTRACT.md §0's Audit Trail law: "each VIEW query itself audited (filter metadata, not
/// results)." Exercises the REAL <see cref="AdminActionExecutor"/> + <see
/// cref="Svac.DomainCore.Audit.AuditReader"/> (L30).
/// </summary>
public sealed class AuditViewExecutionServiceTests : IAsyncLifetime
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
        new(new IPolicyTableSource[] { new CorePolicyTableSource(), new AdminPolicyTableSource() });

    private static AuditViewExecutionService NewService(AdminDbContext adminDb, CoreDbContext coreDb)
    {
        var table = RealPolicyTable();
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var policyEngine = new PolicyEngine(table, staffRoleResolver: new GrantTableStaffRoleResolver(adminDb));
        var executor = new AdminActionExecutor(adminDb, coreDb, eventStore, policyEngine, table, configRegistry);
        var auditReader = new Svac.DomainCore.Audit.AuditReader(coreDb);
        return new AuditViewExecutionService(executor, auditReader, eventStore);
    }

    private static RequestContext CallerCtx(ActorRef staff, string correlationId) => new(
        staff, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    [Fact]
    public async Task View_SuperAdmin_TheDefaultFilterlessLandingView_IsItselfAuditedAndRendersRealRows()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "audit-view-actor");

        // Seed one unrelated real event so the view has something real to render.
        var eventStore = new PostgresEventStore(coreDb);
        await eventStore.Append(StreamType.Audit, "subj_unrelated", "test.some.event", "{}", CallerCtx(superAdmin, "seed"), ExpectedVersion.AnyVersion);

        var service = NewService(adminDb, coreDb);
        var outcome = await service.View(CallerCtx(superAdmin, "audit-view-1"), new AuditFilter(), cursor: null);

        var rendered = Assert.IsType<AuditViewOutcome.Rendered>(outcome);
        Assert.NotEmpty(rendered.Page.Items);

        var staffId = superAdmin.Id.ToString();
        var events = await CollectAuditEvents(coreDb, staffId);
        var viewEvent = Assert.Single(events, e => e.EventType == "admin.audit.viewed");
        Assert.Contains("\"hat\":\"SuperAdmin\"", viewEvent.PayloadJson);
        Assert.DoesNotContain(events, e => e.EventType == "admin.action.executed"); // self-logging, never double-logged
    }

    [Fact]
    public async Task View_CarriesTheAppliedFilterMetadata_NeverTheResultRows()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "audit-view-filter-actor");
        var service = NewService(adminDb, coreDb);

        var filter = new AuditFilter(EventTypePrefix: "core.config.set.", ActorRef: "stf_someone");
        await service.View(CallerCtx(superAdmin, "audit-view-2"), filter, cursor: null);

        var events = await CollectAuditEvents(coreDb, superAdmin.Id.ToString());
        var viewEvent = Assert.Single(events, e => e.EventType == "admin.audit.viewed");
        Assert.Contains("\"event_type_prefix\":\"core.config.set.\"", viewEvent.PayloadJson);
        Assert.Contains("\"actor_ref\":\"stf_someone\"", viewEvent.PayloadJson);
    }

    [Fact]
    public async Task View_NonSuperAdminStaff_IsDenied_LeastPrivilegeOnRawAuditEvents()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (_, safetyAgent, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "audit-view-denied-actor");
        var service = NewService(adminDb, coreDb);

        var outcome = await service.View(CallerCtx(safetyAgent, "audit-view-3"), new AuditFilter(), cursor: null);

        Assert.IsType<AuditViewOutcome.AccessDenied>(outcome);

        var events = await CollectAuditEvents(coreDb, safetyAgent.Id.ToString());
        Assert.DoesNotContain(events, e => e.EventType == "admin.audit.viewed"); // work() never ran
        Assert.Contains(events, e => e.EventType == "admin.action.refused");
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
