using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The foreign-event skip is STRUCTURAL (SLICE_S1_CONTRACT.md §8 clause 7 / SLICE_PLAYBOOK.md §8 clause
/// 7): "Replay advances the per-consumer watermark even when Handles()==false." This is the hermetic
/// template the contract calls for — feed a real IEventStore a foreign event a fixture projection must
/// skip, then a real one, and assert BOTH the watermark advance and the Apply call, against a real
/// Postgres (never an in-memory fake of the replay loop).
///
/// SLICE_S1_CONTRACT.md §8's own text names "the ledger-balance projection" as "instance #1" of this
/// hermetic test. As shipped, Svac.DomainCore.Ledger.LedgerService updates core.ledger_balances INLINE
/// in the same transaction as the ledger append (SLICE_S1_CONTRACT.md §1b: "a domain write and its event
/// are one tx by API shape") rather than via a separate IProjection consuming Replay — core.ledger_balances
/// never advances its own watermark today (LedgerService always writes Watermark=0). That is a real,
/// worth-flagging gap between the contract's "balances rebuild via Replay" framing (§2) and the shipped
/// mechanism; tracked here rather than silently claiming it's covered. This suite instead proves the REPLAY
/// MECHANISM ITSELF is correct and hermetic with a fixture projection — the exact template §8 says every
/// future stream consumer (S4's notifications, S16's reputation recompute, etc.) must pass, ledger-balance
/// included whenever it is wired onto Replay for real.
/// </summary>
public sealed class ProjectionReplayForeignEventSkipTests : IAsyncLifetime
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

    [Fact]
    public async Task Replay_SkipsAForeignEvent_ButStillAdvancesTheWatermarkPastIt()
    {
        var streamId = FreshStreamId();
        using (var seedDb = NewDb())
        {
            var seedStore = new PostgresEventStore(seedDb);
            await seedStore.Append(StreamType.Ledger, streamId, "foreign.event.type", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var projection = new HandledOnlyProjection("fixture.consumer.skip-only", "fixture.event.handled");

        await store.Replay(StreamType.Ledger, projection.ConsumerId, projection);

        Assert.Empty(projection.Applied); // the foreign event was never handed to Apply.
        var checkpoint = await db.ProjectionCheckpoints.SingleAsync(c => c.ConsumerId == projection.ConsumerId && c.StreamType == StreamType.Ledger.ToString());
        Assert.Equal(1, checkpoint.WatermarkSeq); // watermark still advances past the skipped event — the structural guarantee.
    }

    [Fact]
    public async Task Replay_AppliesAHandledEvent_AndSkipsAForeignOneOnTheSameStream()
    {
        var streamId = FreshStreamId();
        using (var seedDb = NewDb())
        {
            var seedStore = new PostgresEventStore(seedDb);
            await seedStore.Append(StreamType.Ledger, streamId, "foreign.event.type", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion); // seq 1: foreign
            await seedStore.Append(StreamType.Ledger, streamId, "fixture.event.handled", "{}", SystemCtx("c2"), ExpectedVersion.AnyVersion); // seq 2: handled
            await seedStore.Append(StreamType.Ledger, streamId, "another.foreign.type", "{}", SystemCtx("c3"), ExpectedVersion.AnyVersion); // seq 3: foreign
        }

        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var projection = new HandledOnlyProjection("fixture.consumer.mixed", "fixture.event.handled");

        await store.Replay(StreamType.Ledger, projection.ConsumerId, projection);

        var applied = Assert.Single(projection.Applied);
        Assert.Equal("fixture.event.handled", applied.EventType);
        Assert.Equal(2, applied.Seq);

        var checkpoint = await db.ProjectionCheckpoints.SingleAsync(c => c.ConsumerId == projection.ConsumerId && c.StreamType == StreamType.Ledger.ToString());
        Assert.Equal(3, checkpoint.WatermarkSeq); // advanced past ALL three events, including the two skipped ones.
    }

    [Fact]
    public async Task Replay_IsResumable_ASecondCallOnlyProcessesEventsAfterTheWatermark()
    {
        var streamId = FreshStreamId();
        var consumerId = "fixture.consumer.resumable";
        using (var seedDb = NewDb())
        {
            var seedStore = new PostgresEventStore(seedDb);
            await seedStore.Append(StreamType.Ledger, streamId, "fixture.event.handled", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);
        }

        var firstProjection = new HandledOnlyProjection(consumerId, "fixture.event.handled");
        using (var db1 = NewDb())
        {
            await new PostgresEventStore(db1).Replay(StreamType.Ledger, consumerId, firstProjection);
        }
        Assert.Single(firstProjection.Applied);

        using (var seedDb2 = NewDb())
        {
            await new PostgresEventStore(seedDb2).Append(StreamType.Ledger, streamId, "fixture.event.handled", "{}", SystemCtx("c2"), ExpectedVersion.AnyVersion);
        }

        var secondProjection = new HandledOnlyProjection(consumerId, "fixture.event.handled");
        using var db2 = NewDb();
        await new PostgresEventStore(db2).Replay(StreamType.Ledger, consumerId, secondProjection);

        // Resuming from the persisted watermark: only the NEW event since the first Replay call is applied.
        var onlyNew = Assert.Single(secondProjection.Applied);
        Assert.Equal(2, onlyNew.Seq);
    }

    [Fact]
    public async Task Replay_TwoConsumersOnTheSameStream_TrackIndependentWatermarks()
    {
        var streamId = FreshStreamId();
        using (var seedDb = NewDb())
        {
            await new PostgresEventStore(seedDb).Append(StreamType.Ledger, streamId, "fixture.event.handled", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);
        }

        var consumerA = new HandledOnlyProjection("fixture.consumer.a", "fixture.event.handled");
        var consumerB = new HandledOnlyProjection("fixture.consumer.b", "fixture.event.handled");

        using (var dbA = NewDb())
        {
            await new PostgresEventStore(dbA).Replay(StreamType.Ledger, consumerA.ConsumerId, consumerA);
        }

        // Consumer B has never replayed this stream — it must start from watermark 0, not inherit A's progress.
        using var dbB = NewDb();
        await new PostgresEventStore(dbB).Replay(StreamType.Ledger, consumerB.ConsumerId, consumerB);

        Assert.Single(consumerA.Applied);
        Assert.Single(consumerB.Applied);
    }

    private static string FreshStreamId() => $"usr_fixture_{Guid.NewGuid():N}";

    /// <summary>
    /// Fixture IProjection (never referenced outside this test file): CanHandle matches exactly one
    /// configured event type, everything else is a foreign event it must skip. Apply records what it
    /// was actually handed so the test can assert the exact set (and only that set) was applied.
    /// </summary>
    private sealed class HandledOnlyProjection(string consumerId, string handledEventType) : IProjection
    {
        public string ConsumerId { get; } = consumerId;
        public StreamType Stream => StreamType.Ledger;
        public List<RecordedEvent> Applied { get; } = new();

        public bool CanHandle(string eventType) => eventType == handledEventType;

        public Task Apply(RecordedEvent recordedEvent, CancellationToken ct = default)
        {
            Applied.Add(recordedEvent);
            return Task.CompletedTask;
        }
    }
}
