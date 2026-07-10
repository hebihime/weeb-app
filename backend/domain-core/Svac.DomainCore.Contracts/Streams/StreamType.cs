namespace Svac.DomainCore.Contracts.Streams;

/// <summary>The six typed streams of the one event substrate (SLICE_S1_CONTRACT.md §1b, §2).</summary>
public enum StreamType
{
    Ledger,
    Reputation,
    Consent,
    Behavioral,
    Audit,
    HeatmapProvenance,
}

/// <summary>Optimistic-concurrency expectation for IEventStore.Append (SLICE_S1_CONTRACT.md §1b, §8 "concurrency at check-then-act").</summary>
public abstract record ExpectedVersion
{
    /// <summary>No expectation — append unconditionally (still gets the next seq atomically).</summary>
    public sealed record Any : ExpectedVersion;

    /// <summary>The stream must be at exactly this seq, or the append fails with a concurrency exception.</summary>
    public sealed record Exact(long Seq) : ExpectedVersion;

    public static readonly ExpectedVersion AnyVersion = new Any();
    public static ExpectedVersion At(long seq) => new Exact(seq);
}

/// <summary>Thrown by IEventStore.Append when ExpectedVersion.Exact does not match the stream's current seq.</summary>
public sealed class ConcurrencyConflictException(string streamId, long expectedSeq, long actualSeq)
    : Exception($"stream \"{streamId}\": expected seq {expectedSeq}, actual seq {actualSeq}")
{
    public string StreamId { get; } = streamId;
    public long ExpectedSeq { get; } = expectedSeq;
    public long ActualSeq { get; } = actualSeq;
}

/// <summary>A durable, previously-appended event, as read back from a stream.</summary>
public sealed record RecordedEvent(
    string EventId,
    string StreamId,
    long Seq,
    string EventType,
    string? PayloadJson,
    string? ReversalOf,
    bool Tombstone,
    string ActorRef,
    string Region,
    string LawfulBasis,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt);
