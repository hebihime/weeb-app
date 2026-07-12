using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>Shared VisibleTo set for every S1/S2 tile (§3: "admin.dashboard.read ... all six roles").</summary>
internal static class TileRoles
{
    public static readonly IReadOnlySet<StaffRole> AllSix = new HashSet<StaffRole>(Enum.GetValues<StaffRole>());
}
