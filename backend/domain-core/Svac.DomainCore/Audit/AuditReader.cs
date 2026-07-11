using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Audit;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Audit;

/// <summary>
/// The read-only pillar contract over <c>core.events_audit</c> (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_
/// CONTRACT.md §1d). No S1/S2 caller exists — this ships the implementation so S5's audit-trail desk has
/// something real to register against, not a stub. Ordered newest-first (RecordedAt DESC via GlobalSeq —
/// the table's true insertion order); the two additive indexes SLICE_S5_CONTRACT.md §1d names are
/// DEFERRED to the S5 build (Phase-2a makes zero schema changes).
/// </summary>
public sealed class AuditReader(CoreDbContext db) : IAuditReader
{
    public async Task<AuditPage> Query(AuditFilter filter, CursorPageRequest page, CancellationToken ct = default)
    {
        var offset = string.IsNullOrEmpty(page.Cursor) ? 0L : CursorMath.Decode(page.Cursor);
        var limit = Math.Max(1, page.Limit);

        var query = db.EventsFor(StreamType.Audit).AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.EventTypePrefix))
        {
            query = query.Where(e => e.EventType.StartsWith(filter.EventTypePrefix));
        }
        if (!string.IsNullOrEmpty(filter.ActorRef))
        {
            query = query.Where(e => e.ActorRef == filter.ActorRef);
        }
        if (!string.IsNullOrEmpty(filter.StreamId))
        {
            query = query.Where(e => e.StreamId == filter.StreamId);
        }
        if (filter.From is not null)
        {
            query = query.Where(e => e.RecordedAt >= filter.From.Value);
        }
        if (filter.To is not null)
        {
            query = query.Where(e => e.RecordedAt <= filter.To.Value);
        }

        var ordered = query.OrderByDescending(e => e.GlobalSeq);
        var rows = await ordered.Skip((int)offset).Take(limit + 1).ToListAsync(ct);

        var hasMore = rows.Count > limit;
        var items = rows.Take(limit)
            .Select(e => new AuditEntryView(e.EventId, e.StreamId, e.EventType, e.ActorRef, e.RecordedAt, e.Tombstone ? null : e.PayloadJson))
            .ToList();

        var nextCursor = hasMore ? CursorMath.Encode(offset + limit) : null;
        return new AuditPage(items, nextCursor, hasMore);
    }
}
