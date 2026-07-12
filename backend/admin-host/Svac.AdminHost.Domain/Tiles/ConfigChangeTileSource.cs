using System.Globalization;
using Svac.DomainCore.Contracts.Audit;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>
/// TILE #1 — THE SLICE METRIC (SLICE_S5_CONTRACT.md §8 seam 2: "the config-change tile is tile #1").
/// Counts every <c>config.set</c> event ever appended — the ACTUAL event type
/// <see cref="Svac.DomainCore.Config.ConfigRegistry.SetValue{T}"/> emits (ConfigRegistry.cs:69), never
/// the 4A ACTION key's own spelling (<c>core.config.set.founder</c>/<c>core.config.set.ops</c>, §3) —
/// those are POLICY action names, not event types; the event itself is always the single, literal
/// string <c>"config.set"</c> regardless of which action key authorized it (Ledger outcome: "config
/// change = audited 3A event, rendered from registry" — this tile IS that outcome, observed). Reads
/// <see cref="IAuditReader"/> only — never a second, parallel count mechanism (§0/§8 seam 16).
/// </summary>
public sealed class ConfigChangeTileSource(IAuditReader auditReader) : IMetricsTileSource
{
    public string TileId => "config-changes";
    public string TitleKey => "admin.dashboard.tile.config_changes.title";
    public IReadOnlySet<StaffRole> VisibleTo => TileRoles.AllSix;

    public async Task<MetricsTileResult> Query(CancellationToken ct = default)
    {
        AuditEntryView? mostRecent = null;
        var count = await AuditReaderPaging.ScanAll(
            auditReader,
            new AuditFilter(EventTypePrefix: "config.set"),
            entry =>
            {
                if (mostRecent is null || entry.RecordedAt > mostRecent.RecordedAt)
                {
                    mostRecent = entry;
                }
            },
            ct);

        var details = mostRecent is null
            ? Array.Empty<MetricsTileDetail>()
            : new[] { new MetricsTileDetail("admin.dashboard.tile.config_changes.most_recent", mostRecent.RecordedAt.ToString("u", CultureInfo.InvariantCulture)) };

        return new MetricsTileResult(count.ToString(CultureInfo.InvariantCulture), details);
    }
}
