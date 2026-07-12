using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Contracts;

/// <summary>
/// <c>GET /v1/me</c> response shape (SLICE_S3_CONTRACT.md §1c) — "the first policy-gated consumer read."
/// <see cref="AgeYears"/> is DERIVED (never the raw birthdate — arch-tested). <see cref="DeletionScheduledFor"/>
/// is present only during the grace window.
/// </summary>
public sealed record AccountSelf(
    OpaqueId AccountId,
    string Handle,
    string Email,
    int AgeYears,
    string Locale,
    string FandomTag,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletionScheduledFor);
