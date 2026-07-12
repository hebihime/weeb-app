using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.AdminHost.Domain.Search;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Quota;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §0/§8 seam 6/§9's "search-audit+quota+honest-dark" gate leg: "EVERY query (even
/// empty) is audited admin.user_search.executed {query_class,query_term,hat} stream_id=staff ref AND
/// quota-consumed via 10A admin.user_search.daily ... The execute path runs THROUGH the audited flow
/// (auth→4A→quota→audit→render)." Exercises the REAL <see cref="AdminActionExecutor"/> +
/// <see cref="QuotaService"/> + <see cref="EmptyUserSearchSource"/> (L30) — never a hand-rolled stand-in
/// for any of the three.
/// </summary>
public sealed class UserSearchExecutionServiceTests : IAsyncLifetime
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

    private static (UserSearchExecutionService Service, AdminActionExecutor Executor) NewService(AdminDbContext adminDb, CoreDbContext coreDb)
    {
        var table = RealPolicyTable();
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var policyEngine = new PolicyEngine(table, staffRoleResolver: new GrantTableStaffRoleResolver(adminDb));
        var executor = new AdminActionExecutor(adminDb, coreDb, eventStore, policyEngine, table, configRegistry);
        var quotaService = new QuotaService(coreDb, configRegistry, Array.Empty<ICapModifier>());
        var searchSource = new EmptyUserSearchSource();
        return (new UserSearchExecutionService(executor, searchSource, quotaService, eventStore), executor);
    }

    private static RequestContext CallerCtx(ActorRef staff, string correlationId) => new(
        staff, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    private static async Task SeedQuotaCap(CoreDbContext coreDb, int cap) =>
        await AdminTestSupport.SeedConfigEntry(coreDb, $"quota.{AdminQuotaKeys.UserSearchDaily}.cap", "ops", "int", cap.ToString(System.Globalization.CultureInfo.InvariantCulture), requiresReason: false);

    // -------------------- audited + honest-dark --------------------

    [Fact]
    public async Task Execute_QualifyingRole_RendersEmptyUserSearchSourcesHonestDarkState_AndAuditsWithHatAndQueryClass()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await SeedQuotaCap(coreDb, 500);
        var (_, safetyAgent, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "search-actor");
        var (service, _) = NewService(adminDb, coreDb);

        var outcome = await service.Execute(CallerCtx(safetyAgent, "search-1"), UserSearchQueryClass.HandlePrefix, "nobody-should-match", cursor: null);

        var rendered = Assert.IsType<UserSearchExecutionOutcome.Rendered>(outcome);
        Assert.False(rendered.Page.SourceLive); // EmptyUserSearchSource's honest-dark state
        Assert.Empty(rendered.Page.Items); // zero fabricated rows

        var staffId = safetyAgent.Id.ToString();
        var events = await CollectAuditEvents(coreDb, staffId);
        var searchEvent = Assert.Single(events, e => e.EventType == "admin.user_search.executed");
        Assert.Contains("\"query_class\":\"HandlePrefix\"", searchEvent.PayloadJson);
        Assert.Contains("\"query_term\":\"nobody-should-match\"", searchEvent.PayloadJson);
        Assert.Contains("\"hat\":\"SafetyAgent\"", searchEvent.PayloadJson);
        Assert.DoesNotContain(events, e => e.EventType == "admin.action.executed"); // self-logging, never double-logged
    }

    [Fact]
    public async Task Execute_EvenAZeroResultQuery_StillAuditsAndConsumesQuota()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await SeedQuotaCap(coreDb, 500);
        var (_, superAdmin, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "super_admin" }, "search-empty-actor");
        var (service, _) = NewService(adminDb, coreDb);

        var outcome1 = await service.Execute(CallerCtx(superAdmin, "search-empty-1"), UserSearchQueryClass.EmailExact, "nobody@example.com", cursor: null);
        var outcome2 = await service.Execute(CallerCtx(superAdmin, "search-empty-2"), UserSearchQueryClass.EmailExact, "nobody@example.com", cursor: null);

        Assert.IsType<UserSearchExecutionOutcome.Rendered>(outcome1);
        Assert.IsType<UserSearchExecutionOutcome.Rendered>(outcome2);

        var events = await CollectAuditEvents(coreDb, superAdmin.Id.ToString());
        Assert.Equal(2, events.Count(e => e.EventType == "admin.user_search.executed")); // every call audited, none skipped for being empty
    }

    // -------------------- role exclusion (Analyst) --------------------

    [Fact]
    public async Task Execute_Analyst_IsDenied_NoUserPiiBeyondAggregate()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await SeedQuotaCap(coreDb, 500);
        var (_, analyst, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "analyst" }, "search-analyst");
        var (service, _) = NewService(adminDb, coreDb);

        var outcome = await service.Execute(CallerCtx(analyst, "search-analyst-1"), UserSearchQueryClass.HandlePrefix, "someone", cursor: null);

        Assert.IsType<UserSearchExecutionOutcome.AccessDenied>(outcome);

        var events = await CollectAuditEvents(coreDb, analyst.Id.ToString());
        Assert.DoesNotContain(events, e => e.EventType == "admin.user_search.executed"); // work() never ran
        Assert.Contains(events, e => e.EventType == "admin.action.refused"); // the standard staff deny, itself audited
    }

    // -------------------- quota cap --------------------

    [Fact]
    public async Task Execute_QuotaCapped_RefusesFurtherEnumeration_ButStillAudits()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await SeedQuotaCap(coreDb, 1);
        var (_, contentMod, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "content_moderator" }, "search-capped");
        var (service, _) = NewService(adminDb, coreDb);

        var first = await service.Execute(CallerCtx(contentMod, "search-cap-1"), UserSearchQueryClass.DeviceExact, "device-1", cursor: null);
        var second = await service.Execute(CallerCtx(contentMod, "search-cap-2"), UserSearchQueryClass.DeviceExact, "device-2", cursor: null);

        Assert.IsType<UserSearchExecutionOutcome.Rendered>(first);
        var limited = Assert.IsType<UserSearchExecutionOutcome.QuotaLimited>(second);
        Assert.Equal(AdminQuotaKeys.UserSearchDaily, limited.Limit.QuotaKey);

        var events = await CollectAuditEvents(coreDb, contentMod.Id.ToString());
        Assert.Equal(2, events.Count(e => e.EventType == "admin.user_search.executed")); // the capped attempt is STILL audited (detection value, §5)
        Assert.Contains(events, e => e.EventType == "admin.user_search.executed" && e.PayloadJson!.Contains("device-2", StringComparison.Ordinal));
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
