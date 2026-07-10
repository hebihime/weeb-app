namespace Svac.DomainCore.Persistence;

/// <summary>
/// The one generated shape behind all six event tables (SLICE_S1_CONTRACT.md §2). Mapped six times as
/// an EF "shared-type entity" — one CLR shape, six physical tables (core.events_ledger,
/// events_reputation, events_consent, events_behavioral, events_audit, events_heatmap_provenance) — so
/// the shape costs one design, not six, while each stream keeps its own table for independent retention/
/// purge/volume/residency posture.
/// </summary>
public sealed class EventRow
{
    public required string EventId { get; set; } // evt_ ULID, PK
    public required string StreamId { get; set; } // subject scope (opaque ref)
    public long Seq { get; set; } // per-stream_id; optimistic append (ExpectedVersion)
    // Concurrency-F1 (SECURITY_REVIEW_S1.md): a TABLE-WIDE monotonic identity, distinct from Seq
    // (per-stream_id, resets to 1 for every new stream). Replay's watermark must advance over THIS
    // column, never Seq — Seq restarting per stream means "seq > watermark" silently and permanently
    // drops a second subject's events once an earlier subject's higher per-stream seq became the
    // watermark. GlobalSeq never resets, so a watermark expressed in it is comparable across every
    // stream_id sharing the table, both for filtering (`WHERE global_seq > watermark`) and for ordering
    // (`ORDER BY global_seq`, the table's true insertion order across streams).
    public long GlobalSeq { get; set; }
    public required string EventType { get; set; }
    public string? PayloadJson { get; set; } // NULL only when tombstoned
    public string? ReversalOf { get; set; } // FK -> this table's event_id
    public bool Tombstone { get; set; }
    public required string ActorRef { get; set; }
    public required string Region { get; set; } // L21, stamped from RequestContext
    public required string LawfulBasis { get; set; } // L21, resolved per §1b
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
