using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Quota;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// 10A idempotent-under-race proof (SLICE_S1_CONTRACT.md §2/§8: "Consume = single atomic UPSERT ... WHERE
/// consumed &lt; cap. ... catch the unique violation, re-read the winner; idempotent-under-race tests
/// standard"). Fires real concurrent Consume calls against the SAME (actor, quotaKey, window) from
/// independent DbContexts/connections and asserts the cap is never overshot — the actual atomic
/// UPSERT...WHERE statement under real Postgres, not a serialized simulation of it.
/// </summary>
public sealed class QuotaConcurrencyTests : IAsyncLifetime
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

    private const int Cap = 5;
    private static readonly QuotaContext FixtureContext = new(
        new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
        TimeZoneInfo.Utc,
        new TimeOnly(4, 0),
        new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Seeds the 9A config row QuotaService reads its base cap from (`quota.&lt;key&gt;.cap`) via a real
    /// EF write through CoreDbContext — never raw SQL, and not the manifest loader (which requires a
    /// "consumer" field this fixture key deliberately has none of, since it is not a real product key).
    /// </summary>
    private async Task SeedCapConfig(string quotaKey, int cap)
    {
        using var db = NewDb();
        db.ConfigEntries.Add(new ConfigEntryEntity
        {
            Key = $"quota.{quotaKey}.cap",
            Type = "int",
            ValueJson = cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Scope = "ops",
            RequiresReason = false,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "sys_fixture",
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Consume_UnderCap_Succeeds_AndReportsCorrectRemaining()
    {
        var quotaKey = FreshQuotaKey();
        await SeedCapConfig(quotaKey, Cap);
        var actor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);

        using var db = NewDb();
        var service = new QuotaService(db, new ConfigRegistryFixtureAdapter(db), IdentityOnlyModifiers());
        var result = await service.Consume(actor, quotaKey, FixtureContext);

        var ok = Assert.IsType<QuotaResult.Ok>(result);
        Assert.Equal(Cap - 1, ok.Consumed.Remaining);
    }

    [Fact]
    public async Task Consume_PastCap_ReturnsLimited_NeverASecondDenyShape()
    {
        var quotaKey = FreshQuotaKey();
        await SeedCapConfig(quotaKey, cap: 1);
        var actor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);

        using var db = NewDb();
        var service = new QuotaService(db, new ConfigRegistryFixtureAdapter(db), IdentityOnlyModifiers());
        var first = await service.Consume(actor, quotaKey, FixtureContext);
        var second = await service.Consume(actor, quotaKey, FixtureContext);

        Assert.IsType<QuotaResult.Ok>(first);
        var limited = Assert.IsType<QuotaResult.Limited>(second);
        Assert.Equal(quotaKey, limited.LimitReached.QuotaKey);
    }

    /// <summary>
    /// The idempotent-under-race proof: <see cref="Cap"/> * 3 concurrent callers race the SAME window;
    /// exactly <see cref="Cap"/> may ever succeed regardless of how the race actually interleaves under
    /// real Postgres — the atomic UPSERT...WHERE consumed &lt; cap clause is what makes this true, not
    /// test-level locking.
    /// </summary>
    [Fact]
    public async Task ConcurrentConsume_AcrossManyCallers_NeverExceedsTheCap_ExactlyCapSucceed()
    {
        var quotaKey = FreshQuotaKey();
        await SeedCapConfig(quotaKey, Cap);
        var actor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);
        const int callers = Cap * 3;

        var tasks = Enumerable.Range(0, callers).Select(async _ =>
        {
            using var db = NewDb();
            var service = new QuotaService(db, new ConfigRegistryFixtureAdapter(db), IdentityOnlyModifiers());
            return await service.Consume(actor, quotaKey, FixtureContext);
        });
        var results = await Task.WhenAll(tasks);

        var succeeded = results.OfType<QuotaResult.Ok>().ToList();
        var limited = results.OfType<QuotaResult.Limited>().ToList();
        Assert.Equal(Cap, succeeded.Count); // never overshoots the cap, even under real concurrent races.
        Assert.Equal(callers - Cap, limited.Count);

        using var verifyDb = NewDb();
        var consumed = await verifyDb.QuotaCounters.Where(q => q.QuotaKey == quotaKey).SumAsync(q => q.Consumed);
        Assert.Equal(Cap, consumed); // the persisted counter itself never exceeds the cap either.
    }

    private static ICapModifier[] IdentityOnlyModifiers() => new ICapModifier[]
    {
        new Svac.DomainCore.Quota.PremiumCapModifier(),
        new Svac.DomainCore.Quota.ReputationCapModifier(),
        new Svac.DomainCore.Quota.ModeCapModifier(),
    };

    private static string FreshQuotaKey() => $"fixture.quota.{Guid.NewGuid():N}";

    /// <summary>Thin IConfigRegistry adapter over a live CoreDbContext — QuotaService only ever calls GetValue.</summary>
    private sealed class ConfigRegistryFixtureAdapter(CoreDbContext db) : Svac.DomainCore.Contracts.Config.IConfigRegistry
    {
        public async Task<T> GetValue<T>(string key, CancellationToken ct = default)
        {
            var row = await db.ConfigEntries.SingleAsync(e => e.Key == key, ct);
            return System.Text.Json.JsonSerializer.Deserialize<T>(row.ValueJson)!;
        }

        public Task SetValue<T>(string key, T value, string reason, Svac.DomainCore.Contracts.Ids.ActorRef actor, Svac.DomainCore.Contracts.RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("fixture adapter: QuotaService never calls SetValue.");
    }
}
