using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// B3's proof (SLICE_S1_CONTRACT.md §10.3): "con_day_cutoff and the S1 keys read through registry
/// machinery by real consumers in a host path." Exercises the EXACT sequence Svac.PublicApi's Program.cs
/// runs at boot — ConfigSeedLoader.SeedFromFile against the real committed manifest file — then reads the
/// seeded value back through IConfigRegistry.GetValue and feeds it into the real Deterministic consumer
/// (ConDayWindow), never a hand-typed fixture value standing in for the manifest.
/// </summary>
public sealed class ConfigRegistryRealConsumerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);

    private static RequestContext SystemCtx(string correlationId) =>
        RequestContext.System(new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System), correlationId);

    private static string RealManifestPath()
    {
        // Same manifest Svac.PublicApi's Program.cs.SeedConfigOnStartup loads (Config/manifests/*.json is
        // Content-copied to the publish output at that relative path) — walked from the test assembly's
        // own output directory back to the repo-committed source file, so this test always exercises the
        // ACTUAL shipped manifest, never a copy that could drift from it.
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir is null)
        {
            throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir, "backend", "domain-core", "Svac.DomainCore", "Config", "manifests", "domain-core.config.json");
    }

    [Fact]
    public async Task SeedFromFile_ThenGetValue_ReadsBackTheRealManifestsConDayCutoff()
    {
        using var db = NewDb();
        var eventStore = new PostgresEventStore(db);
        var loader = new ConfigSeedLoader(db, eventStore);
        var registry = new ConfigRegistry(db, eventStore);

        var seeded = await loader.SeedFromFile(RealManifestPath(), SystemCtx("boot-seed"));
        Assert.True(seeded > 0);

        var cutoff = await registry.GetValue<string>("core.con_day_cutoff");
        Assert.Equal("04:00", cutoff); // the real manifest's v0 value (SLICE_S1_CONTRACT.md §4).
    }

    [Fact]
    public async Task SeededConDayCutoff_FeedsTheRealDeterministicConsumer_ConDayWindow()
    {
        // The full B3 chain: 9A manifest -> ConfigSeedLoader -> IConfigRegistry.GetValue -> a REAL
        // consumer in Svac.DomainCore.Deterministic (never a hand-typed TimeOnly standing in for what
        // config actually resolved to).
        using var db = NewDb();
        var eventStore = new PostgresEventStore(db);
        await new ConfigSeedLoader(db, eventStore).SeedFromFile(RealManifestPath(), SystemCtx("boot-seed"));
        var registry = new ConfigRegistry(db, eventStore);

        var cutoffText = await registry.GetValue<string>("core.con_day_cutoff");
        var cutoff = TimeOnly.Parse(cutoffText, System.Globalization.CultureInfo.InvariantCulture);

        var spec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var beforeCutoff = new DateTimeOffset(2026, 7, 10, 2, 0, 0, TimeSpan.Zero); // 02:00 UTC, before the 04:00 cutoff
        var afterCutoff = new DateTimeOffset(2026, 7, 10, 6, 0, 0, TimeSpan.Zero); // 06:00 UTC, after the 04:00 cutoff

        var windowBefore = ConDayWindow.WindowStart(spec, TimeZoneInfo.Utc, cutoff, beforeCutoff);
        var windowAfter = ConDayWindow.WindowStart(spec, TimeZoneInfo.Utc, cutoff, afterCutoff);

        // 02:00 is still "yesterday's" con-day under a 04:00 cutoff; 06:00 has rolled into "today's" con-day.
        Assert.NotEqual(windowBefore, windowAfter);
        Assert.Equal(new TimeOnly(4, 0), TimeOnly.FromDateTime(windowAfter.DateTime));
    }

    [Fact]
    public async Task SeedFromFile_IsIdempotent_RerunningNeverClobbersAnOpsDeskEdit()
    {
        using var db = NewDb();
        var eventStore = new PostgresEventStore(db);
        var loader = new ConfigSeedLoader(db, eventStore);
        var registry = new ConfigRegistry(db, eventStore);

        await loader.SeedFromFile(RealManifestPath(), SystemCtx("boot-seed-1"));
        // Simulate an ops-desk edit after the first boot.
        await registry.SetValue(
            "core.con_day_cutoff", "05:00", "ops correction",
            new ActorRef(OpaqueId.New(IdPrefixes.Staff, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Staff),
            SystemCtx("ops-edit"));

        // A second boot's seed pass (e.g. a container restart) must never clobber the edit back to "04:00".
        var reseeded = await loader.SeedFromFile(RealManifestPath(), SystemCtx("boot-seed-2"));
        Assert.Equal(0, reseeded); // idempotent: nothing re-seeded, the key already existed.

        var current = await registry.GetValue<string>("core.con_day_cutoff");
        Assert.Equal("05:00", current);
    }

    [Fact]
    public async Task SetValue_AppendsAnAuditStreamEvent_InTheSameTransactionAsTheSeed()
    {
        using var db = NewDb();
        var eventStore = new PostgresEventStore(db);
        var loader = new ConfigSeedLoader(db, eventStore);
        await loader.SeedFromFile(RealManifestPath(), SystemCtx("boot-seed"));

        var auditEvents = new List<RecordedEvent>();
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, "core.con_day_cutoff"))
        {
            auditEvents.Add(e);
        }

        var seedEvent = Assert.Single(auditEvents);
        Assert.Equal("config.seeded", seedEvent.EventType);
    }

    [Fact]
    public async Task RealManifest_EveryEntry_DeclaresANonEmptyConsumer()
    {
        // §4: "A config key with no registered consumer fails a lint." Proven against the real file, not
        // a fixture manifest, so a future entry landing without a consumer string fails THIS test.
        var json = await File.ReadAllTextAsync(RealManifestPath());
        var manifest = System.Text.Json.JsonSerializer.Deserialize<ConfigManifestFile>(json)!;

        Assert.NotEmpty(manifest.Entries);
        Assert.All(manifest.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Consumer)));
    }
}
