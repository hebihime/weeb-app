using System.Globalization;
using Svac.DomainCore.Contracts.Audit;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>
/// One of S1's four named dashboard tiles (SLICE_S5_CONTRACT.md §8 seam 2: "staff sign-ins"). Reads the
/// <c>admin.signin.*</c> event-type prefix off <see cref="IAuditReader"/> — both legs
/// <see cref="Auth.StaffSignInPipeline"/> appends: <c>admin.signin.succeeded</c> (this Pass D's own fix
/// to a real gap — Pass A's pipeline audited every REFUSAL but never the ALLOWED leg, so no live source
/// existed for this tile until now, §8 seam 2's own "never fabricated" law) and the pre-existing
/// <c>admin.signin.refused</c>. PrimaryValue is total sign-in ATTEMPTS (succeeded + refused) — the
/// breakdown is the detail lines, so the headline number is never silently just "successful" or just
/// "refused" without the reader knowing which.
/// </summary>
public sealed class StaffSignInsTileSource(IAuditReader auditReader) : IMetricsTileSource
{
    public string TileId => "staff-signins";
    public string TitleKey => "admin.dashboard.tile.staff_signins.title";
    public IReadOnlySet<StaffRole> VisibleTo => TileRoles.AllSix;

    public async Task<MetricsTileResult> Query(CancellationToken ct = default)
    {
        var succeeded = 0;
        var refused = 0;

        var total = await AuditReaderPaging.ScanAll(
            auditReader,
            new AuditFilter(EventTypePrefix: "admin.signin."),
            entry =>
            {
                if (entry.EventType == "admin.signin.succeeded")
                {
                    succeeded++;
                }
                else if (entry.EventType == "admin.signin.refused")
                {
                    refused++;
                }
            },
            ct);

        var details = new[]
        {
            new MetricsTileDetail("admin.dashboard.tile.staff_signins.succeeded", succeeded.ToString(CultureInfo.InvariantCulture)),
            new MetricsTileDetail("admin.dashboard.tile.staff_signins.refused", refused.ToString(CultureInfo.InvariantCulture)),
        };

        return new MetricsTileResult(total.ToString(CultureInfo.InvariantCulture), details);
    }
}
