using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The Audit Trail desk (SLICE_S5_CONTRACT.md §0/§8 seam 1/7, Pass D). Nav visibility mirrors
/// <c>admin.audit.read</c>'s own Role allowlist (§3: "SuperAdmin (v0) — raw events carry user refs/PII →
/// least privilege") exactly, same pattern as <see cref="ConfigRegistryDeskModule"/>/<see
/// cref="StaffRolesDeskModule"/> before it.
/// </summary>
public sealed class AuditTrailDeskModule : IDeskModule
{
    public string DeskId => "audit_trail";
    public string TitleKey => "admin.nav.audit_trail";
    public int NavOrder => 2;
    public IReadOnlySet<StaffRole> VisibleTo { get; } = new HashSet<StaffRole> { StaffRole.SuperAdmin };
    public Type RootComponent => typeof(Svac.AdminHost.Components.Pages.AuditTrail);
    public string RouteHref => "/audit";
}
