using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Contracts;

/// <summary>
/// "Which hat acted" as a first-class typed field (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.md §1d).
/// <see cref="ActingHat"/> answers "acting in what capacity" (computed by <see
/// cref="Deterministic.HatFor"/>, never hand-picked); <see cref="RolesHeld"/> is the full grant snapshot
/// answering "with what total power" — both are stamped into audit payloads verbatim. Null on every
/// non-admin host's <see cref="RequestContext"/> (S1/S2 byte-identical).
/// </summary>
public sealed record StaffContext(OpaqueId StaffId, IReadOnlySet<StaffRole> RolesHeld, StaffRole ActingHat);
