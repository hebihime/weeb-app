namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// The staff Role axis on <see cref="Ids.ActorKind.Staff"/> (PHASE_2A_SUBSTRATE.md §1, SLICE_S5_CONTRACT.
/// md §1d). CLOSED enum — roles are NOT new <see cref="Ids.ActorKind"/> values; ActorKind stays a closed
/// shipped enum. Declared privilege order (most to least): SuperAdmin, then the four operational roles,
/// then Analyst — <see cref="Deterministic.HatFor"/> selects the least-privileged satisfying role by this
/// exact ordinal ordering.
/// </summary>
public enum StaffRole
{
    SuperAdmin,
    SafetyAgent,
    ContentModerator,
    VenueConOps,
    EconomyOps,
    Analyst,
}
