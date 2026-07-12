using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §4's ledger headline ("config change = audited 3A event, rendered from
/// registry") through the SAME sequence Svac.AdminHost's own Program.cs.SeedConfigOnStartup runs at boot
/// (ConfigSeedLoader.SeedFromFile against the two REAL committed manifest files) — mirrors
/// Svac.Tests.Architecture.ConfigRegistryRealConsumerTests exactly, for the admin-host.config.json/
/// v0-batch.config.json pair instead of domain-core.config.json. V0BatchManifestTests.cs proves the
/// files' own JSON shape matches SLICE_S5_CONTRACT.md §4's two tables key-for-key; THIS file proves that
/// shape actually loads through the real loader without throwing and lands real, readable 9A rows —
/// the difference between "the JSON looks right" and "the boot sequence actually seeds it."
/// </summary>
public sealed class V0BatchManifestSeedingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgis/postgis:16-3.4").Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() => new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);

    private static RequestContext SystemCtx(string correlationId) =>
        RequestContext.System(new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System), correlationId);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }

    private static string AdminHostManifestPath() => Path.Combine(RepoRoot(), "backend", "admin-host", "Svac.AdminHost", "config", "admin-host.config.json");
    private static string V0BatchManifestPath() => Path.Combine(RepoRoot(), "backend", "admin-host", "Svac.AdminHost", "config", "v0-batch.config.json");

    [Fact]
    public async Task SeedFromFile_BothRealManifests_Seeds41RowsTotal_ZeroExceptions()
    {
        using var db = NewDb();
        var loader = new ConfigSeedLoader(db, new PostgresEventStore(db));

        var seededHostTunables = await loader.SeedFromFile(AdminHostManifestPath(), SystemCtx("boot-seed-host"));
        var seededV0Batch = await loader.SeedFromFile(V0BatchManifestPath(), SystemCtx("boot-seed-v0"));

        Assert.Equal(5, seededHostTunables);
        Assert.Equal(36, seededV0Batch);
    }

    [Fact]
    public async Task SeedFromFile_IsIdempotent_ASecondBootSeedsNothingMore()
    {
        using var db = NewDb();
        var loader = new ConfigSeedLoader(db, new PostgresEventStore(db));

        await loader.SeedFromFile(AdminHostManifestPath(), SystemCtx("boot-1"));
        await loader.SeedFromFile(V0BatchManifestPath(), SystemCtx("boot-1"));

        var reseededHost = await loader.SeedFromFile(AdminHostManifestPath(), SystemCtx("boot-2"));
        var reseededV0 = await loader.SeedFromFile(V0BatchManifestPath(), SystemCtx("boot-2"));

        Assert.Equal(0, reseededHost);
        Assert.Equal(0, reseededV0);
    }

    [Fact]
    public async Task SeededSessionLifetimeHours_ReadsBackTheV0Value_WithItsBoundsSeededVerbatim()
    {
        using var db = NewDb();
        var loader = new ConfigSeedLoader(db, new PostgresEventStore(db));
        await loader.SeedFromFile(AdminHostManifestPath(), SystemCtx("boot-seed"));

        var registry = new ConfigRegistry(db, new PostgresEventStore(db));
        var hours = await registry.GetValue<int>("admin.session_lifetime_hours");
        Assert.Equal(8, hours);

        var row = await db.ConfigEntries.SingleAsync(e => e.Key == "admin.session_lifetime_hours");
        Assert.NotNull(row.BoundsJson);
        // Semantic, not raw-text, comparison: BoundsJson preserves the manifest file's own JSON
        // formatting (pretty-printed) verbatim (OPS-3's promise is real DATA for ConfigBounds.
        // ValidateAsync, not a particular whitespace layout).
        var bounds = System.Text.Json.JsonSerializer.Deserialize<int[]>(row.BoundsJson!);
        Assert.Equal(new[] { 1, 24 }, bounds);
        Assert.False(row.RequiresReason);
        Assert.Equal("ops", row.Scope);
    }

    [Fact]
    public async Task SeededFourEyesRequired_DefaultsFalse_FounderScope_RequiresReason()
    {
        using var db = NewDb();
        var loader = new ConfigSeedLoader(db, new PostgresEventStore(db));
        await loader.SeedFromFile(AdminHostManifestPath(), SystemCtx("boot-seed"));

        var registry = new ConfigRegistry(db, new PostgresEventStore(db));
        Assert.False(await registry.GetValue<bool>("admin.four_eyes_required"));

        var row = await db.ConfigEntries.SingleAsync(e => e.Key == "admin.four_eyes_required");
        Assert.Equal("founder", row.Scope);
        Assert.True(row.RequiresReason);
    }

    [Fact]
    public async Task SeededV0BatchKey_ReadsBackARealJsonValue()
    {
        using var db = NewDb();
        var loader = new ConfigSeedLoader(db, new PostgresEventStore(db));
        await loader.SeedFromFile(V0BatchManifestPath(), SystemCtx("boot-seed"));

        var registry = new ConfigRegistry(db, new PostgresEventStore(db));
        var threshold = await registry.GetValue<int>("verification.age_gate_challenge_threshold");
        Assert.Equal(21, threshold);

        var budget = await registry.GetValue<System.Text.Json.JsonElement>("romantic.superlike_budget");
        Assert.Equal(1, budget.GetProperty("free").GetInt32());
        Assert.Equal(3, budget.GetProperty("premium").GetInt32());
    }

    [Fact]
    public async Task BothRealManifests_EveryEntry_DeclaresANonEmptyConsumer()
    {
        // §4's dead-tunable lint (ConfigSeedLoader.SeedFromFile's own enforcement, proven live above by
        // the zero-exceptions seeding test): every entry, pending or not, carries a real consumer string
        // TODAY — the v0-batch keys' consumer text honestly says "pending" and points at
        // pending_consumer_slice; Pass C's own dead-tunable-lint extension later reads that JSON field
        // structurally, but the non-empty invariant this loader already enforces holds from day one.
        foreach (var path in new[] { AdminHostManifestPath(), V0BatchManifestPath() })
        {
            var json = await File.ReadAllTextAsync(path);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<ConfigManifestFile>(json)!;
            Assert.NotEmpty(manifest.Entries);
            Assert.All(manifest.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Consumer)));
        }
    }
}
