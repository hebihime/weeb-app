namespace Svac.AimlRouter.Routing;

/// <summary>One resolved, walkable hop — every law (allowlisted, model declared, DI-registered, privacy floor) has already passed for this hop.</summary>
public sealed record ResolvedHop(string Provider, string Model);

/// <summary>
/// The Resolver's pure output (SLICE_S2_CONTRACT.md §1b): "a chain resolving empty fails closed" — an
/// empty <see cref="Hops"/> list IS the <c>NoRouteConfigured</c> signal; the caller never needs a
/// separate "was this resolved" flag.
/// </summary>
public sealed record ProviderChain(IReadOnlyList<ResolvedHop> Hops)
{
    public bool IsEmpty => Hops.Count == 0;
}
