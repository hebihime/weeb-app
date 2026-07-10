namespace Svac.AimlRouter.Routing;

/// <summary>One resolved, walkable hop — every law (allowlisted, model declared, DI-registered, privacy floor) has already passed for this hop.</summary>
public sealed record ResolvedHop(string Provider, string Model);

/// <summary>
/// The Resolver's pure output (SLICE_S2_CONTRACT.md §1b): "a chain resolving empty fails closed" — an
/// empty <see cref="Hops"/> list means the caller maps to a typed refusal, never a silent drop.
/// <see cref="AnyCeilingSkip"/> distinguishes WHY it is empty (build-phase fix, SLICE_S2_CONTRACT.md
/// §10.3 FINDING 2, backend/e2e/aiml-router.e2e.mjs's own header): a candidate link that was otherwise
/// allowlisted, model-declared, and DI-registered but skipped SPECIFICALLY because the call's
/// <see cref="Svac.AimlRouter.Contracts.PayloadClass"/> exceeded the provider's ceiling is a genuine
/// route that privacy law blocked ("availability never buys a privacy downgrade", §1b) — the caller maps
/// that to <see cref="Svac.AimlRouter.Contracts.AimlFailure.RefusedPrivacyFloor"/>. A chain with NO such
/// skip that still resolves empty (nothing allowlisted, no model declared, nothing DI-registered, or —
/// for <see cref="Resolver.Resolve"/> only — the task has no configured chain at all) is the distinct
/// <c>NoRouteConfigured</c> signal <see cref="Svac.AimlRouter.Contracts.AimlTaskKind"/>'s own doc comment
/// names: "a kind with no policy chain fails closed."
/// </summary>
public sealed record ProviderChain(IReadOnlyList<ResolvedHop> Hops, bool AnyCeilingSkip = false)
{
    public bool IsEmpty => Hops.Count == 0;
}
