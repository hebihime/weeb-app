using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The Config Registry desk (SLICE_S5_CONTRACT.md §0/§4/§8 seam 1, Pass C) — THE LEDGER HEADLINE's own
/// nav registration, proving the seam composes a third time (zero edits to DashboardDeskModule/
/// StaffRolesDeskModule/AdminLayout). No <c>admin.config.read</c> policy row exists (§3's table: config
/// edits ride the EXISTING <c>core.config.set.founder</c>/<c>core.config.set.ops</c> rows, zero new rows)
/// — reading the registry's own values carries no more restriction than any other page reachable behind
/// <c>admin.host.transport</c>, so every staff role sees this nav entry, exactly like <see
/// cref="DashboardDeskModule"/>. WHO may actually SUBMIT an edit is gated later, per-key, by the Role
/// axis on those two existing rows (SuperAdmin for founder-scope; SuperAdmin/EconomyOps for ops-scope) —
/// evaluated inside <see cref="Svac.AdminHost.Domain.Execution.AdminActionExecutor"/>, never by this nav
/// registration.
/// </summary>
public sealed class ConfigRegistryDeskModule : IDeskModule
{
    public string DeskId => "config_registry";
    public string TitleKey => "admin.nav.config_registry";
    public int NavOrder => 1;
    public IReadOnlySet<StaffRole> VisibleTo { get; } = new HashSet<StaffRole>(Enum.GetValues<StaffRole>());
    public Type RootComponent => typeof(Svac.AdminHost.Components.Pages.ConfigRegistry);
    public string RouteHref => "/config";
}
