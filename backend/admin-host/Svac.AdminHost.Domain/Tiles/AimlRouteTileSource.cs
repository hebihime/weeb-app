using System.Globalization;
using System.Text.Json;
using Svac.DomainCore.Contracts.Audit;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>
/// S2's own tile promise, kept (SLICE_S5_CONTRACT.md §8 seam 2: "aiml.route_decided: volume, failover,
/// latency, provider/model mix, policy version — the S2 §10.6 promise kept"). Reads
/// <c>aiml.route_decided</c> off the SHARED event substrate via <see cref="IAuditReader"/> — never a
/// reference to <c>backend/modules/AimlRouter</c> (arch-gated, <c>AdminHostBoundaryTests</c>: the admin
/// host never references a feature module). The payload shape parsed here is a structural MIRROR of
/// <c>Svac.AimlRouter.Audit.AimlRouteDecidedEvent</c> — duplicated deliberately (never a project
/// reference to the module that owns it) and read defensively (unknown/missing fields default rather
/// than throw), so a future S2-side payload addition can never break this tile's boot.
/// </summary>
public sealed class AimlRouteTileSource(IAuditReader auditReader) : IMetricsTileSource
{
    private const int MixTop = 3;

    public string TileId => "aiml-routing";
    public string TitleKey => "admin.dashboard.tile.aiml_routing.title";
    public IReadOnlySet<StaffRole> VisibleTo => TileRoles.AllSix;

    public async Task<MetricsTileResult> Query(CancellationToken ct = default)
    {
        var failovers = 0;
        long latencySumMs = 0;
        var latencySamples = 0;
        var providerModelMix = new Dictionary<string, int>(StringComparer.Ordinal);
        var policyVersions = new SortedSet<int>();

        var total = await AuditReaderPaging.ScanAll(
            auditReader,
            new AuditFilter(EventTypePrefix: "aiml.route_decided"),
            entry =>
            {
                if (entry.PayloadJson is null)
                {
                    return; // tombstoned — never fabricate a decision from a purged payload.
                }

                using var doc = JsonDocument.Parse(entry.PayloadJson);
                var root = doc.RootElement;

                if (TryGetNonEmptyString(root, "failover_from") is not null)
                {
                    failovers++;
                }
                if (root.TryGetProperty("latency_ms", out var latencyEl) && latencyEl.TryGetInt64(out var latencyMs))
                {
                    latencySumMs += latencyMs;
                    latencySamples++;
                }
                if (root.TryGetProperty("policy_version", out var policyEl) && policyEl.TryGetInt32(out var policyVersion))
                {
                    policyVersions.Add(policyVersion);
                }

                var provider = TryGetNonEmptyString(root, "provider") ?? "unknown";
                var model = TryGetNonEmptyString(root, "model") ?? "unknown";
                var mixKey = $"{provider}/{model}";
                providerModelMix[mixKey] = providerModelMix.GetValueOrDefault(mixKey) + 1;
            },
            ct);

        var details = new List<MetricsTileDetail>
        {
            new("admin.dashboard.tile.aiml_routing.failovers", failovers.ToString(CultureInfo.InvariantCulture)),
            new(
                "admin.dashboard.tile.aiml_routing.avg_latency_ms",
                latencySamples > 0 ? (latencySumMs / latencySamples).ToString(CultureInfo.InvariantCulture) : "n/a"),
            new(
                "admin.dashboard.tile.aiml_routing.policy_versions",
                policyVersions.Count > 0 ? string.Join(", ", policyVersions) : "n/a"),
        };

        foreach (var (mixKey, count) in providerModelMix.OrderByDescending(kv => kv.Value).Take(MixTop))
        {
            details.Add(new MetricsTileDetail("admin.dashboard.tile.aiml_routing.mix_entry", string.Create(CultureInfo.InvariantCulture, $"{mixKey}: {count}")));
        }

        return new MetricsTileResult(total.ToString(CultureInfo.InvariantCulture), details);
    }

    private static string? TryGetNonEmptyString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(el.GetString())
            ? el.GetString()
            : null;
}
