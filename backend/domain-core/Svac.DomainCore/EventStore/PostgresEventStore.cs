using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Deterministic;

namespace Svac.DomainCore.EventStore;

/// <summary>
/// The 3A substrate over Postgres (SLICE_S1_CONTRACT.md §1b, §2). Append joins the ambient EF
/// transaction — the substrate IS the outbox: a caller wraps its own domain write and this Append call
/// in one <c>DbContext.Database.BeginTransaction()</c> (or simply the same SaveChanges, since Append
/// stages rather than immediately saves) so both commit or neither does.
/// </summary>
public sealed class PostgresEventStore(CoreDbContext db) : IEventStore
{
    public async Task<RecordedEvent> Append(
        StreamType stream,
        string streamId,
        string eventType,
        string? payloadJson,
        RequestContext ctx,
        ExpectedVersion expectedVersion,
        CancellationToken ct = default)
    {
        var table = db.EventsFor(stream);
        var currentMaxSeq = await table
            .Where(e => e.StreamId == streamId)
            .Select(e => (long?)e.Seq)
            .MaxAsync(ct) ?? 0;

        if (expectedVersion is ExpectedVersion.Exact exact && exact.Seq != currentMaxSeq)
        {
            throw new ConcurrencyConflictException(streamId, exact.Seq, currentMaxSeq);
        }

        var nextSeq = currentMaxSeq + 1;
        var now = DateTimeOffset.UtcNow;
        var eventId = MintEventId(now);

        var region = ctx.Region.ToString();
        var row = new EventRow
        {
            EventId = eventId,
            StreamId = streamId,
            Seq = nextSeq,
            EventType = eventType,
            PayloadJson = payloadJson,
            ReversalOf = null,
            Tombstone = false,
            ActorRef = ctx.Actor.ToString(),
            Region = region,
            // PII-F2 (SECURITY_REVIEW_S1.md): the config VARIANT KEY selects which code table resolves the
            // basis (§1b) — it is never itself a lawful basis. LawfulBasisResolver is the resolver.
            LawfulBasis = LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, stream.ToString(), eventType, region),
            OccurredAt = now,
            RecordedAt = now,
        };
        table.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // C# cannot await inside a catch filter, so the unique-violation check happens in the body:
            // re-throw untouched if this was some other failure, only translate the (stream_id, seq)
            // race into the typed ConcurrencyConflictException.
            db.Entry(row).State = EntityState.Detached;
            if (!await IsUniqueViolationOnSeq(table, streamId, nextSeq, ct))
            {
                throw;
            }
            var actualSeq = await table.Where(e => e.StreamId == streamId).Select(e => (long?)e.Seq).MaxAsync(ct) ?? 0;
            throw new ConcurrencyConflictException(streamId, nextSeq - 1, actualSeq);
        }

        return ToRecorded(row);
    }

    public async Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default)
    {
        var table = db.EventsFor(stream);
        var original = await table.SingleOrDefaultAsync(e => e.EventId == eventId, ct)
            ?? throw new InvalidOperationException($"event \"{eventId}\" not found on stream {stream}.");

        var now = DateTimeOffset.UtcNow;
        var reversalRegion = ctx.Region.ToString();
        var reversalRow = new EventRow
        {
            EventId = MintEventId(now),
            StreamId = original.StreamId,
            Seq = await table.Where(e => e.StreamId == original.StreamId).Select(e => (long?)e.Seq).MaxAsync(ct) is { } maxSeq ? maxSeq + 1 : 1,
            EventType = $"{original.EventType}.reversed",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { reason }),
            ReversalOf = original.EventId,
            Tombstone = false,
            ActorRef = ctx.Actor.ToString(),
            Region = reversalRegion,
            LawfulBasis = LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, stream.ToString(), $"{original.EventType}.reversed", reversalRegion),
            OccurredAt = now,
            RecordedAt = now,
        };
        table.Add(reversalRow);
        await db.SaveChangesAsync(ct);
        return ToRecorded(reversalRow);
    }

    public async Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default)
    {
        var table = db.EventsFor(stream);
        var row = await table.SingleOrDefaultAsync(e => e.EventId == eventId, ct)
            ?? throw new InvalidOperationException($"event \"{eventId}\" not found on stream {stream}.");

        // The ONLY sanctioned update path: payload -> NULL, tombstone flag set. The append-only trigger
        // (migration-level raw SQL) permits exactly this transition and rejects any other UPDATE.
        row.PayloadJson = null;
        row.Tombstone = true;
        await db.SaveChangesAsync(ct);
    }

    public async IAsyncEnumerable<RecordedEvent> ReadStream(
        StreamType stream, string streamId, long fromSeq = 0, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var table = db.EventsFor(stream);
        var query = table
            .Where(e => e.StreamId == streamId && e.Seq >= fromSeq)
            .OrderBy(e => e.Seq)
            .AsAsyncEnumerable();

        await foreach (var row in query.WithCancellation(ct))
        {
            yield return ToRecorded(row);
        }
    }

    public async Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default)
    {
        var streamTypeKey = stream.ToString();

        // Concurrency-F4 (SECURITY_REVIEW_S1.md): the checkpoint read/apply/write used to be three
        // separate, unlocked steps — two concurrent runners for the SAME consumer (a host-restart
        // overlapping a background runner, or two workers sharing a consumer id) could both read the same
        // watermark, both hand the same event to the projection, and both commit — a non-idempotent
        // projection executes twice. An explicit transaction + SELECT ... FOR UPDATE NOWAIT locks the
        // checkpoint row for this runner's ENTIRE read-apply-write, so a second runner for the same
        // consumer can never observe the same pre-advance watermark this one already claimed.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        ProjectionCheckpointEntity? checkpoint;
        try
        {
            checkpoint = await db.ProjectionCheckpoints
                .FromSqlInterpolated($"""
                    SELECT * FROM core.projection_checkpoints
                    WHERE consumer_id = {consumerId} AND stream_type = {streamTypeKey}
                    FOR UPDATE NOWAIT
                    """)
                .SingleOrDefaultAsync(ct);
        }
        catch (Exception ex) when (IsLockNotAvailable(ex))
        {
            // NOWAIT (never blocking indefinitely, unlike a plain FOR UPDATE) means a caller awaiting
            // this Replay call inside the same async chain as the OTHER in-flight runner's own release
            // can never deadlock against it. This runner backs off entirely: zero events applied,
            // watermark unchanged — the in-flight runner's own commit is the only resolution, and its
            // watermark advance already accounts for whatever this runner would otherwise have done.
            // Safe to retry later; never surfaces as a caller-visible failure for a benign overlap.
            await tx.RollbackAsync(ct);
            return;
        }

        var watermark = checkpoint?.WatermarkSeq ?? 0;

        // Concurrency-F1 (SECURITY_REVIEW_S1.md): filter AND order by GlobalSeq, never Seq. Seq is
        // per-stream_id and restarts at 1 for every new stream, so a watermark expressed in Seq (as
        // shipped) is only ever comparable to events on the ONE stream_id whose Seq values happened to
        // produce it — a second subject's events on the same table, at a lower per-stream Seq than an
        // already-processed subject's, are silently and permanently unreachable once the watermark
        // exceeds their Seq. GlobalSeq is table-wide and monotonic, so both the filter and the cross-
        // stream chronological order it now drives are correct regardless of how many distinct stream_ids
        // share the table.
        var table = db.EventsFor(stream);
        var pending = await table.Where(e => e.GlobalSeq > watermark).OrderBy(e => e.GlobalSeq).ToListAsync(ct);

        foreach (var row in pending)
        {
            var recorded = ToRecorded(row);
            // Foreign-event skip is STRUCTURAL (§8 clause 7): the watermark advances regardless of
            // CanHandle, so a shared-stream consumer never gets silently stuck on an event type it
            // does not own.
            if (projection.CanHandle(recorded.EventType))
            {
                await projection.Apply(recorded, ct);
            }
            watermark = row.GlobalSeq;
        }

        if (checkpoint is null)
        {
            db.ProjectionCheckpoints.Add(new ProjectionCheckpointEntity
            {
                ConsumerId = consumerId,
                StreamType = streamTypeKey,
                WatermarkSeq = watermark,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            checkpoint.WatermarkSeq = watermark;
            checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static string MintEventId(DateTimeOffset now)
    {
        var randomness = new byte[10];
        RandomNumberGenerator.Fill(randomness);
        var body = Ulid.Encode(now.ToUnixTimeMilliseconds(), randomness);
        return Ulid.WithPrefix(IdPrefixes.Event, body);
    }

    /// <summary>
    /// True if <paramref name="ex"/> is (or wraps) Postgres' 55P03 "lock_not_available" — the error
    /// FOR UPDATE NOWAIT raises. EF Core's default execution strategy re-wraps a provider exception it
    /// classifies as transient in an InvalidOperationException ("likely due to a transient failure") when
    /// no retrying strategy is configured, so the real PostgresException may be the outer exception OR
    /// its InnerException — this checks both without requiring EnableRetryOnFailure to be configured.
    /// </summary>
    private static bool IsLockNotAvailable(Exception ex) =>
        (ex as Npgsql.PostgresException ?? ex.InnerException as Npgsql.PostgresException)?.SqlState == Npgsql.PostgresErrorCodes.LockNotAvailable;

    private static async Task<bool> IsUniqueViolationOnSeq(DbSet<EventRow> table, string streamId, long seq, CancellationToken ct) =>
        await table.AnyAsync(e => e.StreamId == streamId && e.Seq == seq, ct);

    private static RecordedEvent ToRecorded(EventRow row) => new(
        row.EventId, row.StreamId, row.Seq, row.EventType, row.PayloadJson, row.ReversalOf,
        row.Tombstone, row.ActorRef, row.Region, row.LawfulBasis, row.OccurredAt, row.RecordedAt);
}
