namespace Svac.DomainCore.Contracts.Streams;

/// <summary>
/// The 3A substrate: one append/reverse/tombstone/replay store over six typed streams
/// (SLICE_S1_CONTRACT.md §1b). Append joins the ambient EF transaction: the substrate IS the outbox
/// (§9) — a domain write and its event are one tx by API shape, not by author discipline.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends a new event. Joins the caller's ambient EF transaction (same-tx or it does not exist).
    /// </summary>
    public Task<RecordedEvent> Append(
        StreamType stream,
        string streamId,
        string eventType,
        string? payloadJson,
        RequestContext ctx,
        ExpectedVersion expectedVersion,
        CancellationToken ct = default);

    /// <summary>Appends a reversal_of entry. Never mutates the original — reversal entries, never data surgery.</summary>
    public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default);

    /// <summary>The ONLY sanctioned update path: payload -> NULL, tombstone flag set. Never a real DELETE.</summary>
    public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default);

    /// <summary>Reads a stream's events from a given sequence number (inclusive), in ascending seq order.</summary>
    public IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, CancellationToken ct = default);

    /// <summary>
    /// Replays a stream's events from a consumer's watermark through a projection. The watermark
    /// advances even when CanHandle()==false for an event — the foreign-event skip is STRUCTURAL
    /// (SLICE_S1_CONTRACT.md §8 clause 7), never left to the projection author to remember.
    /// </summary>
    public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default);
}
