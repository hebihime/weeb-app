using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The User Search desk (SLICE_S5_CONTRACT.md §0/§8 seam 1/6, Pass D). Nav visibility mirrors
/// <c>admin.user_search.execute</c>'s own Role allowlist (§3: "SuperAdmin, SafetyAgent, ContentModerator
/// — Analyst structurally excluded") exactly — a role that cannot search never even sees the link (§0
/// nav-absence law), and <see cref="Components.Pages.UserSearch"/>'s own defensive re-check (mirrors
/// StaffRoles.razor.cs's pattern) guards a direct URL navigation the same way.
/// </summary>
public sealed class UserSearchDeskModule : IDeskModule
{
    public string DeskId => "user_search";
    public string TitleKey => "admin.nav.user_search";
    public int NavOrder => 2;
    public IReadOnlySet<StaffRole> VisibleTo { get; } = new HashSet<StaffRole>
    {
        StaffRole.SuperAdmin, StaffRole.SafetyAgent, StaffRole.ContentModerator,
    };
    public Type RootComponent => typeof(Svac.AdminHost.Components.Pages.UserSearch);
    public string RouteHref => "/user-search";
}
