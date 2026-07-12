using Svac.DomainCore.Contracts.Ids;

namespace Svac.Identity.Contracts;

/// <summary>
/// The frozen event envelope (SLICE_S3_CONTRACT.md §1b) appended to the audit stream in the SAME
/// transaction as every real <see cref="IAccountLifecycle"/> transition — "the single publication point
/// every later module's §1c cascade projection keys on." The shape is frozen —
/// <c>{account_id, from, to, reason_key, effective_at}</c> verbatim — extend only via a versioned contract
/// change, never by adding a field silently.
/// </summary>
public sealed record AccountStateChanged(
    OpaqueId AccountId,
    AccountState From,
    AccountState To,
    string? ReasonKey,
    DateTimeOffset EffectiveAt);
