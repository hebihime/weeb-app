using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Policy;

/// <summary>
/// The grant-table-backed <see cref="IStaffRoleResolver"/> override (SLICE_S5_CONTRACT.md §1d/§0: "Roles
/// come from OUR grants table, never Entra claims"), superseding the domain-core
/// <c>DenyAllStaffRoleResolver</c> default on THIS host only (registered by <c>AddStaffAuth</c>, never by
/// <c>AddAdminHostModule</c> — DependencyInjectionTests.cs pins that boundary). Reads ONLY active grants
/// (<c>revoked_at IS NULL</c>) for a <see cref="ActorKind.Staff"/> actor; any other actor kind resolves to
/// the empty set — this resolver is never asked about a non-staff actor in practice (the Role axis only
/// evaluates when a policy row declares <c>StaffRoles</c>, which by construction only ever gates
/// <c>ActorKind.Staff</c> rows, §0 law a), but returning empty rather than throwing keeps the contract
/// total and fail-closed either way.
/// </summary>
public sealed class GrantTableStaffRoleResolver(AdminDbContext adminDb) : IStaffRoleResolver
{
    private static readonly IReadOnlySet<StaffRole> Empty = new HashSet<StaffRole>();

    public async Task<IReadOnlySet<StaffRole>> GrantsOf(ActorRef staff, CancellationToken ct = default)
    {
        if (staff.Kind != ActorKind.Staff)
        {
            return Empty;
        }

        var staffId = staff.Id.ToString();
        var codes = await adminDb.StaffRoleGrants
            .Where(g => g.StaffId == staffId && g.RevokedAt == null)
            .Select(g => g.Role)
            .ToListAsync(ct);

        return codes.Select(StaffRoleCodes.Parse).ToHashSet();
    }
}
