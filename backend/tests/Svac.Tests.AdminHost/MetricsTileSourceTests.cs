using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Tiles;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Purge;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §8 seam 2 ("dashboard-live-tiles" gate leg) — every registered
/// <see cref="IMetricsTileSource"/> reads REAL data it did not fabricate: seed real events through the
/// real domain-core write paths (never a hand-rolled fixture object standing in for
/// <see cref="IAuditReader"/>/<see cref="Svac.DomainCore.Contracts.Purge.IPurgeRunReader"/>'s own
/// implementations, L30), then assert the tile's own computed value tracks what was actually seeded —
/// the deterministic-gate-lane proof behind the live E2E's own "real counts" assertion.
/// </summary>
public sealed class MetricsTileSourceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgis/postgis:16-3.4").Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await coreDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // -------------------- config-change tile (tile #1, the slice metric) --------------------

    [Fact]
    public async Task ConfigChangeTileSource_ZeroConfigSetEventsYet_RendersAnHonestZero_NeverFabricated()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var source = new ConfigChangeTileSource(new Svac.DomainCore.Audit.AuditReader(coreDb));

        var result = await source.Query();

        Assert.Equal("0", result.PrimaryValue);
        Assert.Empty(result.Details); // no "most recent" line when nothing has ever changed
    }

    [Fact]
    public async Task ConfigChangeTileSource_TracksRealConfigSetEvents_ThroughTheRealSetValuePath()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        const string key = "test.tiles.config_change_key";
        await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "1", requiresReason: false);
        var eventStore = new PostgresEventStore(coreDb);
        var configRegistry = new ConfigRegistry(coreDb, eventStore);
        var ctx = AdminTestSupport.SystemCtx("tile-config-change-1");

        var source = new ConfigChangeTileSource(new Svac.DomainCore.Audit.AuditReader(coreDb));
        var before = await source.Query();

        await configRegistry.SetValue(key, 2, "tile drill", ctx.Actor, ctx);

        var after = await source.Query();
        Assert.Equal(int.Parse(before.PrimaryValue, CultureInfo.InvariantCulture) + 1, int.Parse(after.PrimaryValue, CultureInfo.InvariantCulture));
        Assert.Contains(after.Details, d => d.LabelKey == "admin.dashboard.tile.config_changes.most_recent");
    }

    // -------------------- purge-runs tile --------------------

    [Fact]
    public async Task PurgeRunsTileSource_ZeroRunsYet_RendersAnHonestZero()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var source = new PurgeRunsTileSource(new PurgeRunReader(coreDb));

        var result = await source.Query();

        Assert.Equal("0", result.PrimaryValue);
    }

    [Fact]
    public async Task PurgeRunsTileSource_TracksARealPurgeRunRow()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        coreDb.PurgeRuns.Add(new PurgeRunEntity
        {
            Id = OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared).ToString(),
            PurgeClass = "account_deletion",
            SubjectRef = "usr_test-subject",
            StoreKey = "core.test_store",
            RowsAffected = 3,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        await coreDb.SaveChangesAsync();

        var source = new PurgeRunsTileSource(new PurgeRunReader(coreDb));
        var result = await source.Query();

        Assert.Equal("1", result.PrimaryValue);
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.purge_runs.most_recent_class");
    }

    // -------------------- stream-volumes tile --------------------

    [Fact]
    public async Task StreamVolumeTileSource_CountsRealRowsAcrossAllSixStreams()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var eventStore = new PostgresEventStore(coreDb);
        var ctx = AdminTestSupport.SystemCtx("tile-stream-volumes-1");

        await eventStore.Append(StreamType.Behavioral, "subj_1", "test.behavioral.event", "{}", ctx, ExpectedVersion.AnyVersion);
        await eventStore.Append(StreamType.Audit, "subj_1", "test.audit.event", "{}", ctx, ExpectedVersion.AnyVersion);
        await eventStore.Append(StreamType.Audit, "subj_2", "test.audit.event", "{}", ctx, ExpectedVersion.AnyVersion);

        var source = new StreamVolumeTileSource(coreDb);
        var result = await source.Query();

        Assert.Equal("3", result.PrimaryValue);
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.stream_volumes.stream.behavioral" && d.Value == "1");
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.stream_volumes.stream.audit" && d.Value == "2");
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.stream_volumes.stream.ledger" && d.Value == "0"); // real zero, not absent
    }

    // -------------------- staff-signins tile --------------------

    [Fact]
    public async Task StaffSignInsTileSource_BreaksDownSucceededVsRefused_FromRealAuditEvents()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var eventStore = new PostgresEventStore(coreDb);
        var ctx = AdminTestSupport.SystemCtx("tile-signins-1");

        await eventStore.Append(StreamType.Audit, "stf_a", "admin.signin.succeeded", "{\"subject\":\"a\"}", ctx, ExpectedVersion.AnyVersion);
        await eventStore.Append(StreamType.Audit, "stf_a", "admin.signin.succeeded", "{\"subject\":\"a\"}", ctx, ExpectedVersion.AnyVersion);
        await eventStore.Append(StreamType.Audit, "signin:x", "admin.signin.refused", "{\"subject\":\"x\",\"reason_key\":\"no_mfa_claim\"}", ctx, ExpectedVersion.AnyVersion);

        var source = new StaffSignInsTileSource(new Svac.DomainCore.Audit.AuditReader(coreDb));
        var result = await source.Query();

        Assert.Equal("3", result.PrimaryValue);
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.staff_signins.succeeded" && d.Value == "2");
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.staff_signins.refused" && d.Value == "1");
    }

    // -------------------- aiml-routing tile (the S2 §10.6 promise kept) --------------------

    [Fact]
    public async Task AimlRouteTileSource_AggregatesVolumeFailoverLatencyMixAndPolicyVersion_FromRealPayloads()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var eventStore = new PostgresEventStore(coreDb);
        var ctx = AdminTestSupport.SystemCtx("tile-aiml-1");

        await eventStore.Append(
            StreamType.Audit, "inv_1", "aiml.route_decided",
            """{"invocation_id":"inv_1","caller":"T9","task":"moderation","payload_class":"text","provider":"Anthropic","model":"claude","policy_version":1,"outcome":"succeeded","latency_ms":100,"input_tokens":1,"output_tokens":1,"payload_sha256":"x"}""",
            ctx, ExpectedVersion.AnyVersion);
        await eventStore.Append(
            StreamType.Audit, "inv_2", "aiml.route_decided",
            """{"invocation_id":"inv_2","caller":"T9","task":"moderation","payload_class":"text","provider":"Anthropic","model":"claude","policy_version":1,"failover_from":"OpenAI","outcome":"succeeded","latency_ms":300,"input_tokens":1,"output_tokens":1,"payload_sha256":"y"}""",
            ctx, ExpectedVersion.AnyVersion);

        var source = new AimlRouteTileSource(new Svac.DomainCore.Audit.AuditReader(coreDb));
        var result = await source.Query();

        Assert.Equal("2", result.PrimaryValue);
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.aiml_routing.failovers" && d.Value == "1");
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.aiml_routing.avg_latency_ms" && d.Value == "200");
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.aiml_routing.policy_versions" && d.Value == "1");
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.aiml_routing.mix_entry" && d.Value.Contains("Anthropic/claude", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AimlRouteTileSource_ZeroEventsYet_RendersAnHonestZero_NeverFabricated()
    {
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var source = new AimlRouteTileSource(new Svac.DomainCore.Audit.AuditReader(coreDb));

        var result = await source.Query();

        Assert.Equal("0", result.PrimaryValue);
        Assert.Contains(result.Details, d => d.LabelKey == "admin.dashboard.tile.aiml_routing.avg_latency_ms" && d.Value == "n/a");
    }
}
