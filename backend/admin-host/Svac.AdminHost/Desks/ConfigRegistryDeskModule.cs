using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The Config Registry desk (SLICE_S5_CONTRACT.md §0/§4/§8 seam 1, Pass C) — THE LEDGER HEADLINE's own
/// nav registration, proving the seam composes a third time (zero edits to DashboardDeskModule/
/// StaffRolesDeskModule/AdminLayout).
///
/// SECURITY_REVIEW_S5.md S5-01: nav visibility now mirrors the real <c>admin.config.read</c> policy row's
/// own Role allowlist (SuperAdmin, EconomyOps — the union of every role that can commit at least one
/// core.config.set.* action) exactly, the SAME "nav-absence law" <see cref="UserSearchDeskModule"/> and
/// <see cref="DashboardDeskModule"/> already follow — a role that cannot view the registry never even
/// sees the link. Before this fix, no <c>admin.config.read</c> row existed at all and this desk
/// (wrongly) showed the link to all six roles unconditionally; <see cref="Components.Pages.ConfigRegistry"/>'s
/// own defensive re-check on a direct URL navigation guards the same way StaffRoles.razor.cs's pattern
/// does. WHO may actually SUBMIT an edit is gated separately, per-key, by the Role axis on
/// <c>core.config.set.founder</c>/<c>core.config.set.ops</c> — evaluated inside
/// <see cref="Svac.AdminHost.Domain.Execution.AdminActionExecutor"/>, never by this nav registration.
/// </summary>
public sealed class ConfigRegistryDeskModule : IDeskModule
{
    public string DeskId => "config_registry";
    public string TitleKey => "admin.nav.config_registry";
    public int NavOrder => 1;
    public IReadOnlySet<StaffRole> VisibleTo { get; } = new HashSet<StaffRole>
    {
        StaffRole.SuperAdmin, StaffRole.EconomyOps,
    };
    public Type RootComponent => typeof(Svac.AdminHost.Components.Pages.ConfigRegistry);
    public string RouteHref => "/config";
}
