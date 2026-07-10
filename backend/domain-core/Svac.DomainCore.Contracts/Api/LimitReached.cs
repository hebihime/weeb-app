namespace Svac.DomainCore.Contracts.Api;

/// <summary>
/// THE single 429 deny component every future limit $refs (SLICE_S1_CONTRACT.md §1c, §12.9). No cause
/// taxonomy that could distinguish tier floors from freemium caps (design 5.16: identical rendering);
/// premium_extends is a render hint for the one surface's optional CTA, not a cause. Shared between the
/// domain-side IQuotaService result and the wire-shape OpenAPI component — one type, one source of truth.
/// </summary>
public sealed record LimitReached(
    string QuotaKey,
    string MessageKey,
    DateTimeOffset ResetsAt,
    bool PremiumExtends);
