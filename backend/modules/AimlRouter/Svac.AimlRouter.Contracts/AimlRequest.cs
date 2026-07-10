using Svac.DomainCore.Contracts.Ids;

namespace Svac.AimlRouter.Contracts;

/// <summary>
/// The router's one request shape (SLICE_S2_CONTRACT.md §1b, §12.3): one verb, closed pre-declared
/// <see cref="AimlTaskKind"/> — activating a consumer is policy data, never a contract change.
/// </summary>
/// <param name="Subject">Opaque; region + purge scoping. Null = subject-less system work.</param>
/// <param name="TargetLocale">Translate only; must be a member of <c>i18n/locales.json</c> x4.</param>
/// <param name="ExplicitPin">Null ⇒ automatic (policy) mode.</param>
public sealed record AimlRequest(
    AimlTaskKind Task,
    CallerModule Caller,
    PayloadClass PayloadClass,
    ActorRef? Subject,
    AimlPayload Payload,
    string? TargetLocale,
    ProviderPin? ExplicitPin);
