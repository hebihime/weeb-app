using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Purge;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>
/// One of S1's four named dashboard tiles (SLICE_S5_CONTRACT.md §8 seam 2: "purge runs via
/// IPurgeRunReader"). Zero purge runs is a REAL, honest zero (no purge class has fired yet at this early
/// slice) — never withheld or rendered as a placeholder (§8 seam 16).
/// </summary>
public sealed class PurgeRunsTileSource(IPurgeRunReader purgeRunReader) : IMetricsTileSource
{
    private const int Window = 200;

    public string TileId => "purge-runs";
    public string TitleKey => "admin.dashboard.tile.purge_runs.title";
    public IReadOnlySet<StaffRole> VisibleTo => TileRoles.AllSix;

    public async Task<MetricsTileResult> Query(CancellationToken ct = default)
    {
        var page = await purgeRunReader.Recent(new CursorPageRequest(Limit: Window), ct);
        var count = page.Items.Count;
        var suffix = page.HasMore ? "+" : "";

        var details = new List<MetricsTileDetail>();
        var mostRecent = page.Items.OrderByDescending(r => r.StartedAt).FirstOrDefault();
        if (mostRecent is not null)
        {
            details.Add(new MetricsTileDetail("admin.dashboard.tile.purge_runs.most_recent_class", mostRecent.PurgeClass));
            details.Add(new MetricsTileDetail("admin.dashboard.tile.purge_runs.most_recent_at", mostRecent.StartedAt.ToString("u")));
        }

        return new MetricsTileResult($"{count}{suffix}", details);
    }
}
