using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Policy;

/// <summary>
/// The fail-closed default <see cref="IStaffRoleResolver"/> (PHASE_2A_SUBSTRATE.md §1, SLICE_S5_CONTRACT.
/// md §1d: "a staff actor with no real resolver has NO roles, fail-closed" — the <c>ThrowingPaymentService</c>
/// family). Every host resolves this unless it registers a real grant-table-backed resolver (the admin
/// host, S5 build) — a staff actor authorized against ANY StaffRoles-gated row is denied until then.
/// </summary>
public sealed class DenyAllStaffRoleResolver : IStaffRoleResolver
{
    private static readonly IReadOnlySet<StaffRole> Empty = new HashSet<StaffRole>();

    public Task<IReadOnlySet<StaffRole>> GrantsOf(ActorRef staff, CancellationToken ct = default) => Task.FromResult(Empty);
}
