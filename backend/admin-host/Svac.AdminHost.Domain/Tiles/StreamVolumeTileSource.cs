using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Persistence;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>
/// One of S1's four named dashboard tiles (SLICE_S5_CONTRACT.md §8 seam 2: "stream volumes"). No pillar
/// read contract exposes a per-stream row count (<see cref="IAuditReader"/>/<see cref="IPurgeRunReader"/>
/// are scoped to <c>events_audit</c> / purge runs only) — this tile reads <see cref="CoreDbContext"/>
/// directly via <see cref="CoreDbContext.EventsFor"/>, a public, already-shipped domain-core symbol
/// (verified present before use, never re-added), exactly like <c>AdminActionExecutor</c>'s own direct
/// <c>CoreDbContext</c> use. A plain row-count query is a READ, not one of the three named mutating-call
/// shapes <c>AdminActionChokepointArchTests</c> guards (configRegistry.SetValue / eventStore.Append|
/// Reverse|Tombstone / adminDb.*.Add) — reading every stream's own table is exactly the substrate's own
/// six-stream shape (SLICE_S1_CONTRACT.md §1b), never a second store.
/// </summary>
public sealed class StreamVolumeTileSource(CoreDbContext coreDb) : IMetricsTileSource
{
    public string TileId => "stream-volumes";
    public string TitleKey => "admin.dashboard.tile.stream_volumes.title";
    public IReadOnlySet<StaffRole> VisibleTo => TileRoles.AllSix;

    public async Task<MetricsTileResult> Query(CancellationToken ct = default)
    {
        var details = new List<MetricsTileDetail>();
        long total = 0;
        foreach (var stream in CoreDbContext.StreamTables.Keys.OrderBy(s => s.ToString(), StringComparer.Ordinal))
        {
            var count = await coreDb.EventsFor(stream).LongCountAsync(ct);
            total += count;
            details.Add(new MetricsTileDetail($"admin.dashboard.tile.stream_volumes.stream.{stream.ToString().ToLowerInvariant()}", count.ToString(CultureInfo.InvariantCulture)));
        }

        return new MetricsTileResult(total.ToString(CultureInfo.InvariantCulture), details);
    }
}
