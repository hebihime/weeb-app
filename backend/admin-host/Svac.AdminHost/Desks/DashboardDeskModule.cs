using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The first live <see cref="IDeskModule"/> registrant (SLICE_S5_CONTRACT.md §0/§8 seam 1) — proves the
/// seam is non-vacuous on day one. Gated by <c>admin.dashboard.read</c> (§3: "staff: all six roles —
/// Analyst's whole scope"), so every staff role sees this nav entry regardless of what else they can do.
/// Real tile registration (<c>IMetricsTileSource</c>) is Pass D's deliverable — this scaffold's Dashboard
/// page renders an honest empty shell until then (§8 seam 16: real-or-honestly-dark).
/// </summary>
public sealed class DashboardDeskModule : IDeskModule
{
    public string DeskId => "dashboard";
    public string TitleKey => "admin.nav.dashboard";
    public int NavOrder => 0;
    public IReadOnlySet<StaffRole> VisibleTo { get; } = new HashSet<StaffRole>(Enum.GetValues<StaffRole>());
    public Type RootComponent => typeof(Svac.AdminHost.Components.Pages.Dashboard);
    public string RouteHref => "/dashboard";
}
