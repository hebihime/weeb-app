using Svac.DomainCore.Contracts.Api;

namespace Svac.DomainCore.Contracts.Audit;

/// <summary>A staff-desk query over the immutable audit stream (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.md §1d). All fields optional/ANDed.</summary>
public sealed record AuditFilter(string? EventTypePrefix = null, string? ActorRef = null, string? StreamId = null, DateTimeOffset? From = null, DateTimeOffset? To = null);

/// <summary>One rendered audit row — never the raw payload by default (least privilege; SLICE_S5_CONTRACT.md §3: "raw events carry user refs/PII").</summary>
public sealed record AuditEntryView(string EventId, string StreamId, string EventType, string ActorRef, DateTimeOffset RecordedAt, string? PayloadJson);

public sealed record AuditPage(IReadOnlyList<AuditEntryView> Items, string? NextCursor, bool HasMore);

/// <summary>
/// Read-only pillar contract over <c>core.events_audit</c> (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.
/// md §1d). No PublicApi endpoint may ever map this (staff-only reachability, arch-gated at S5). Implemented
/// in Svac.DomainCore this surgery; zero callers exist at S1/S2.
/// </summary>
public interface IAuditReader
{
    public Task<AuditPage> Query(AuditFilter filter, CursorPageRequest page, CancellationToken ct = default);
}
