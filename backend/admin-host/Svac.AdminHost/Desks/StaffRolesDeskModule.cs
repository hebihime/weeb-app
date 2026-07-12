using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The Staff & Roles desk (SLICE_S5_CONTRACT.md §0/§8 seam 1, Pass B): provision/deactivate/reactivate/
/// role_grant/role_revoke as SSR form posts (<see cref="Svac.AdminHost.Staff.StaffRolesEndpointExtensions"/>),
/// every mutation routed through <see cref="Svac.AdminHost.Domain.Execution.IAdminActionExecutor"/>. Every
/// one of the five §3 policy rows this desk's forms exercise is <c>SuperAdmin</c>-only
/// (<see cref="Svac.AdminHost.Domain.Policy.AdminPolicyTableSource"/>), so the desk itself is visible ONLY
/// to SuperAdmin in the nav — a role that cannot act here never even sees the link (§0 nav-absence law).
/// </summary>
public sealed class StaffRolesDeskModule : IDeskModule
{
    public string DeskId => "staff_roles";
    public string TitleKey => "admin.nav.staff_roles";
    public int NavOrder => 1;
    public IReadOnlySet<StaffRole> VisibleTo { get; } = new HashSet<StaffRole> { StaffRole.SuperAdmin };
    public Type RootComponent => typeof(Svac.AdminHost.Components.Pages.StaffRoles);
    public string RouteHref => "/staff";
}
