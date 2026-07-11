using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Svac.Identity.Config;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// OPS-3 (SECURITY_REVIEW_S3.md, MED, statutory): <c>ConfigRegistry.SetValue</c>'s bounds check used to be
/// a hardcoded switch over exactly 3 AimlRouter keys — every identity key (including the statutory export
/// floor and the GDPR grace-window ceiling) hit <c>default: break</c> and passed through unenforced. Now
/// generalized to read each key's own <c>BoundsJson</c> (seeded from identity.config.json's own "bounds"
/// field) on the REAL <c>ConfigRegistry.SetValue</c> write path — not just the DevSeams grace-days
/// endpoint's own local <c>if</c> check (Program.cs), which never protected any OTHER caller of SetValue.
///
/// Deliberately its OWN Postgres container (mirrors Svac.Tests.Architecture's
/// ConfigRegistryRealConsumerTests, never IdentityDbFixture's shared collection): these tests mutate
/// GLOBAL 9A config rows (identity.deletion.grace_days, identity.export.daily_cap) that every other test
/// in the shared IdentityDb collection reads — sharing that container would make this suite's own
/// red/green writes a source of cross-test flakiness for everyone else.
/// </summary>
public sealed class ConfigBoundsRealWritePathTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private static readonly ActorRef SystemActor = new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();

        var eventStore = new PostgresEventStore(db);
        var loader = new ConfigSeedLoader(db, eventStore);
        var seeded = await loader.SeedFromFile(RealManifestPath(), SystemCtx("boot-seed"));
        Assert.True(seeded > 0);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);

    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);

    private static string RealManifestPath()
    {
        // The ACTUAL shipped identity manifest, never a copy that could drift from it (mirrors
        // ConfigRegistryRealConsumerTests.RealManifestPath's own walk-to-repo-root discipline).
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir is null)
        {
            throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir, "backend", "modules", "identity", "config", "identity.config.json");
    }

    [Fact]
    public async Task SetValue_ExportDailyCapToZero_IsRejected_TheStatutoryFloorIsOne()
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => registry.SetValue(
            IdentityConfigKeys.ExportDailyCap, 0, reason: "red-fixture", SystemActor, SystemCtx("ops-3-red-fixture-export-zero")));
        Assert.Contains("bounds", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The rejected value never lands — a re-read still sees the seeded v0 (2), never 0.
        using var readDb = NewDb();
        var readRegistry = new ConfigRegistry(readDb, new PostgresEventStore(readDb));
        var current = await readRegistry.GetValue<int>(IdentityConfigKeys.ExportDailyCap);
        Assert.NotEqual(0, current);
    }

    [Fact]
    public async Task SetValue_ExportDailyCapWithinBounds_Succeeds()
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        await registry.SetValue(IdentityConfigKeys.ExportDailyCap, 5, reason: "green-fixture", SystemActor, SystemCtx("ops-3-green-fixture-export"));

        using var readDb = NewDb();
        var readRegistry = new ConfigRegistry(readDb, new PostgresEventStore(readDb));
        var current = await readRegistry.GetValue<int>(IdentityConfigKeys.ExportDailyCap);
        Assert.Equal(5, current);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(9999)]
    public async Task SetValue_DeletionGraceDaysOutsideZeroToThirty_IsRejected(int outOfBoundsDays)
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => registry.SetValue(
            IdentityConfigKeys.DeletionGraceDays, outOfBoundsDays, reason: "red-fixture", SystemActor, SystemCtx($"ops-3-red-fixture-grace-{outOfBoundsDays}")));
        Assert.Contains("bounds", ex.Message, StringComparison.OrdinalIgnoreCase);

        using var readDb = NewDb();
        var readRegistry = new ConfigRegistry(readDb, new PostgresEventStore(readDb));
        var current = await readRegistry.GetValue<int>(IdentityConfigKeys.DeletionGraceDays);
        Assert.NotEqual(outOfBoundsDays, current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    [InlineData(30)]
    public async Task SetValue_DeletionGraceDaysWithinZeroToThirty_Succeeds(int withinBoundsDays)
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        await registry.SetValue(IdentityConfigKeys.DeletionGraceDays, withinBoundsDays, reason: "green-fixture", SystemActor, SystemCtx($"ops-3-green-fixture-grace-{withinBoundsDays}"));

        using var readDb = NewDb();
        var readRegistry = new ConfigRegistry(readDb, new PostgresEventStore(readDb));
        var current = await readRegistry.GetValue<int>(IdentityConfigKeys.DeletionGraceDays);
        Assert.Equal(withinBoundsDays, current);
    }
}
