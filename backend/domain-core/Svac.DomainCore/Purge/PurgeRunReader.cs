using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Persistence;

namespace Svac.DomainCore.Purge;

/// <summary>
/// The read-only pillar contract over <c>core.purge_runs</c> (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_
/// CONTRACT.md §1d) — the desk-tile read side of 13A's receipts. No S1/S2 caller exists at this surgery.
/// Ordered newest-first by <c>started_at</c>.
/// </summary>
public sealed class PurgeRunReader(CoreDbContext db) : IPurgeRunReader
{
    public async Task<PurgeRunPage> Recent(CursorPageRequest page, CancellationToken ct = default)
    {
        var offset = string.IsNullOrEmpty(page.Cursor) ? 0L : CursorMath.Decode(page.Cursor);
        var limit = Math.Max(1, page.Limit);

        var rows = await db.PurgeRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Skip((int)offset)
            .Take(limit + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        var items = rows.Take(limit)
            .Select(r => new PurgeRunView(r.Id, r.PurgeClass, r.SubjectRef, r.StoreKey, r.RowsAffected, r.StartedAt, r.CompletedAt))
            .ToList();

        var nextCursor = hasMore ? CursorMath.Encode(offset + limit) : null;
        return new PurgeRunPage(items, nextCursor, hasMore);
    }
}
