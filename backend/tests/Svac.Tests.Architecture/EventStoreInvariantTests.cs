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
/// The 3A substrate's append/reverse/tombstone/replay invariants over a REAL Postgres (SLICE_S1_CONTRACT.md
/// §1b, §8 "concurrency at check-then-act", §11: "property-based event-substrate tests (append/reverse/
/// tombstone/replay invariants, seq uniqueness under race — eng review §6)"). Every assertion drives the
/// real IEventStore/PostgresEventStore through its public contract — no raw SQL is ever used to fake a
/// row into existence; seq uniqueness under race is proven against the actual unique index the migration
/// creates, not simulated.
/// </summary>
public sealed class EventStoreInvariantTests : IAsyncLifetime
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
    public async Task Append_AssignsMonotonicSeqPerStreamId_StartingAt1()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamId = FreshStreamId();

        var first = await store.Append(StreamType.Audit, streamId, "fixture.one", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);
        var second = await store.Append(StreamType.Audit, streamId, "fixture.two", "{}", SystemCtx("c2"), ExpectedVersion.AnyVersion);

        Assert.Equal(1, first.Seq);
        Assert.Equal(2, second.Seq);
    }

    [Fact]
    public async Task Append_SeqIsPerStreamId_NotGlobalAcrossStreams()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamA = FreshStreamId();
        var streamB = FreshStreamId();

        await store.Append(StreamType.Audit, streamA, "fixture.a1", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);
        await store.Append(StreamType.Audit, streamA, "fixture.a2", "{}", SystemCtx("c2"), ExpectedVersion.AnyVersion);
        var bFirst = await store.Append(StreamType.Audit, streamB, "fixture.b1", "{}", SystemCtx("c3"), ExpectedVersion.AnyVersion);

        Assert.Equal(1, bFirst.Seq); // stream B's own first append, unaffected by stream A already being at seq 2.
    }

    [Fact]
    public async Task Append_ExpectedVersionExact_MismatchThrowsWithActualSeq()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamId = FreshStreamId();
        await store.Append(StreamType.Audit, streamId, "fixture.one", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion); // stream now at seq 1

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.Append(StreamType.Audit, streamId, "fixture.two", "{}", SystemCtx("c2"), ExpectedVersion.At(0)));

        Assert.Equal(streamId, ex.StreamId);
        Assert.Equal(0, ex.ExpectedSeq);
        Assert.Equal(1, ex.ActualSeq);
    }

    [Fact]
    public async Task Append_ExpectedVersionExact_MatchSucceeds()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamId = FreshStreamId();
        var first = await store.Append(StreamType.Audit, streamId, "fixture.one", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);

        var second = await store.Append(StreamType.Audit, streamId, "fixture.two", "{}", SystemCtx("c2"), ExpectedVersion.At(first.Seq));

        Assert.Equal(2, second.Seq);
    }

    [Fact]
    public async Task Reverse_AppendsAReversalEntry_NeverMutatesTheOriginal()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamId = FreshStreamId();
        var original = await store.Append(StreamType.Audit, streamId, "fixture.chargeable", """{"amount":10}""", SystemCtx("c1"), ExpectedVersion.AnyVersion);

        var reversal = await store.Reverse(StreamType.Audit, original.EventId, "fixture reversal reason", SystemCtx("c2"));

        Assert.Equal(original.EventId, reversal.ReversalOf);
        Assert.Equal("fixture.chargeable.reversed", reversal.EventType);
        Assert.Equal(2, reversal.Seq);

        var events = await ReadAll(store, streamId);
        var originalReRead = events.Single(e => e.EventId == original.EventId);
        // "Never mutates the original" — payload, event type and seq are byte-identical to what Append returned.
        Assert.Equal(original.PayloadJson, originalReRead.PayloadJson);
        Assert.Equal(original.EventType, originalReRead.EventType);
        Assert.Equal(original.Seq, originalReRead.Seq);
        Assert.False(originalReRead.Tombstone);
    }

    [Fact]
    public async Task Tombstone_NullsPayloadAndSetsFlag_RowSurvives_NeverADelete()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamId = FreshStreamId();
        var original = await store.Append(StreamType.Audit, streamId, "fixture.pii-bearing", """{"secret":"value"}""", SystemCtx("c1"), ExpectedVersion.AnyVersion);

        await store.Tombstone(StreamType.Audit, original.EventId, "statutory_erasure", SystemCtx("c2"));

        var events = await ReadAll(store, streamId);
        var row = Assert.Single(events);
        Assert.True(row.Tombstone);
        Assert.Null(row.PayloadJson);
        Assert.Equal(original.EventId, row.EventId); // the row itself survives — this is an update, never a delete.
    }

    [Fact]
    public async Task ReadStream_FromSeq_ExcludesEarlierEvents()
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var streamId = FreshStreamId();
        await store.Append(StreamType.Audit, streamId, "fixture.one", "{}", SystemCtx("c1"), ExpectedVersion.AnyVersion);
        var second = await store.Append(StreamType.Audit, streamId, "fixture.two", "{}", SystemCtx("c2"), ExpectedVersion.AnyVersion);

        var fromSecond = new List<RecordedEvent>();
        await foreach (var e in store.ReadStream(StreamType.Audit, streamId, fromSeq: second.Seq))
        {
            fromSecond.Add(e);
        }

        var onlyEvent = Assert.Single(fromSecond);
        Assert.Equal("fixture.two", onlyEvent.EventType);
    }

    /// <summary>
    /// Seq uniqueness under race (SLICE_S1_CONTRACT.md §11): N callers race to append onto the SAME
    /// streamId with ExpectedVersion.AnyVersion from independent connections/DbContexts (a true race, not
    /// a serialized simulation). PostgresEventStore.Append does not internally retry — a caller either
    /// wins the (stream_id, seq) unique index or gets a typed ConcurrencyConflictException — so this test
    /// also proves the documented "catch the unique violation, re-read the winner" pattern (BUILD.md §9)
    /// by retrying failed callers until every one of the N logical appends has landed with a UNIQUE seq
    /// and there are no gaps and no duplicates.
    /// </summary>
    [Fact]
    public async Task ConcurrentAppends_AnyVersion_NeverProduceDuplicateSeq_RetryClosesEveryGap()
    {
        const int callers = 8;
        var streamId = FreshStreamId();

        var results = new System.Collections.Concurrent.ConcurrentBag<long>();
        var tasks = Enumerable.Range(0, callers).Select(async i =>
        {
            var seq = await AppendWithRetryOnConflict(streamId, $"fixture.racer.{i}");
            results.Add(seq);
        });
        await Task.WhenAll(tasks);

        var seqs = results.OrderBy(s => s).ToList();
        Assert.Equal(callers, seqs.Count);
        Assert.Equal(seqs.Distinct().Count(), seqs.Count); // no duplicate seq ever committed
        Assert.Equal(Enumerable.Range(1, callers).Select(i => (long)i), seqs); // no gaps: exactly 1..N

        using var verifyDb = NewDb();
        var verifyStore = new PostgresEventStore(verifyDb);
        var persisted = await ReadAll(verifyStore, streamId);
        Assert.Equal(callers, persisted.Count);
        Assert.Equal(persisted.Select(e => e.Seq).Distinct().Count(), persisted.Count);
    }

    [Fact]
    public async Task ConcurrentAppends_ExpectedVersionExact_ExactlyOneWinnerPerAttemptedSeq()
    {
        // Two callers BOTH believe the stream is at seq 0 (a genuine stale-read race on the SAME expected
        // version) and both attempt to append at seq 1 with ExpectedVersion.At(0). Exactly one must win;
        // the other must observe a ConcurrencyConflictException carrying the winner's actual seq.
        var streamId = FreshStreamId();

        var attempt1 = AppendExactAt(streamId, "fixture.racer.exact.1", expectedSeq: 0);
        var attempt2 = AppendExactAt(streamId, "fixture.racer.exact.2", expectedSeq: 0);
        var outcomes = await Task.WhenAll(attempt1, attempt2);

        var succeeded = outcomes.Where(o => o.Succeeded).ToList();
        var failed = outcomes.Where(o => !o.Succeeded).ToList();
        Assert.Single(succeeded);
        Assert.Single(failed);
        Assert.Equal(succeeded[0].Seq, failed[0].ConflictActualSeq);
    }

    private async Task<long> AppendWithRetryOnConflict(string streamId, string eventType)
    {
        while (true)
        {
            using var db = NewDb();
            var store = new PostgresEventStore(db);
            try
            {
                var recorded = await store.Append(StreamType.Audit, streamId, eventType, "{}", SystemCtx(Guid.NewGuid().ToString()), ExpectedVersion.AnyVersion);
                return recorded.Seq;
            }
            catch (ConcurrencyConflictException)
            {
                // Reference pattern (BUILD.md §9): catch the unique violation, re-read, retry. A fresh
                // DbContext/connection on the next loop iteration re-reads the actual current max seq.
            }
        }
    }

    private async Task<(bool Succeeded, long Seq, long ConflictActualSeq)> AppendExactAt(string streamId, string eventType, long expectedSeq)
    {
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        try
        {
            var recorded = await store.Append(StreamType.Audit, streamId, eventType, "{}", SystemCtx(Guid.NewGuid().ToString()), ExpectedVersion.At(expectedSeq));
            return (true, recorded.Seq, 0);
        }
        catch (ConcurrencyConflictException ex)
        {
            return (false, 0, ex.ActualSeq);
        }
    }

    private static async Task<List<RecordedEvent>> ReadAll(PostgresEventStore store, string streamId)
    {
        var events = new List<RecordedEvent>();
        await foreach (var e in store.ReadStream(StreamType.Audit, streamId))
        {
            events.Add(e);
        }
        return events;
    }

    private static string FreshStreamId() => $"usr_fixture_{Guid.NewGuid():N}";
}
