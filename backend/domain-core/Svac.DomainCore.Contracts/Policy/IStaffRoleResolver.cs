using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// Resolves the staff roles currently granted to a staff actor (PHASE_2A_SUBSTRATE.md §1, SLICE_S5_
/// CONTRACT.md §1d). The default registration on every host is fail-closed (the <c>ThrowingPaymentService</c>
/// family): a staff actor with no real resolver wired has NO roles, so every StaffRoles-gated row denies
/// until a host registers a real grant-table-backed resolver (the admin host, S5 build).
/// </summary>
public interface IStaffRoleResolver
{
    public Task<IReadOnlySet<StaffRole>> GrantsOf(ActorRef staff, CancellationToken ct = default);
}
