using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Audit;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>
/// A small, bounded full-scan helper over <see cref="IAuditReader"/> (SLICE_S5_CONTRACT.md §8 seam 7:
/// "audit-trail desk + tiles" are the pillar contract's named consumers) — several tiles (config-change
/// count, staff sign-in breakdown, aiml.route_decided aggregates) need a TOTAL over a filtered event-type
/// prefix, which <see cref="IAuditReader.Query"/>'s own cursor shape does not hand back directly. Bounded
/// at <see cref="MaxPages"/> so a runaway event volume degrades to an honest under-count (still real data,
/// never fabricated) rather than an unbounded dashboard-request loop.
/// </summary>
internal static class AuditReaderPaging
{
    private const int PageSize = 500;
    private const int MaxPages = 40; // 20,000 events per tile query — generous for this slice's real volumes.

    public static async Task<int> ScanAll(
        IAuditReader reader, AuditFilter filter, Action<AuditEntryView> onEach, CancellationToken ct = default)
    {
        var total = 0;
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var result = await reader.Query(filter, new CursorPageRequest(cursor, PageSize), ct);
            foreach (var item in result.Items)
            {
                onEach(item);
                total++;
            }
            if (!result.HasMore || result.NextCursor is null)
            {
                break;
            }
            cursor = result.NextCursor;
        }
        return total;
    }
}
