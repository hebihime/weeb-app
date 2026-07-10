using Svac.AimlRouter.Contracts;
using Svac.DomainCore.Contracts;

namespace Svac.AimlRouter.Routing;

/// <summary>
/// The pure, IO-free routing resolver (SLICE_S2_CONTRACT.md §1b): "Routing resolution is deterministic
/// space, structurally (15A's own text: a policy table, never a latent judgment)." No wall-clock read,
/// no config-registry read, no network call inside this type — every input arrives as an explicit
/// parameter, so the SAME inputs always resolve to the SAME chain (golden-vectored by the Build-phase
/// test suite; this type is what those vectors exercise).
/// </summary>
internal static class Resolver
{
    /// <summary>
    /// Effective set = allowlist (9A) ∩ DI-registered (environment truth). A chain member failing ANY
    /// law for THIS call — not on the allowlist, model undeclared for that provider, not DI-registered
    /// in this environment, or above the provider's payload-class ceiling — is SKIPPED, not tried
    /// (§1b failover law: "availability never buys a privacy downgrade"). A chain resolving empty is the
    /// <c>NoRouteConfigured</c> signal (<see cref="ProviderChain.IsEmpty"/>).
    /// </summary>
    /// <param name="region">First-class resolver input per §1b (residency_overrides); the v0 override list
    /// is empty, so this parameter is accepted now and wired the moment a non-empty override lands,
    /// rather than being retrofitted as a signature change later.</param>
    public static ProviderChain Resolve(
        RoutingPolicy policy,
        IReadOnlyList<ProviderAllowlistEntry> allowlist,
        IReadOnlySet<string> registeredProviderIds,
        AimlTaskKind task,
        PayloadClass payloadClass,
        RegionCode region)
    {
        var declaredChain = ChainFor(policy, task);
        var hops = new List<ResolvedHop>(declaredChain.Count);
        var anyCeilingSkip = false;

        foreach (var link in declaredChain)
        {
            var allowlisted = allowlist.FirstOrDefault(a => a.Name == link.Provider);
            if (allowlisted is null)
            {
                continue; // not allowlisted -> skip, never tried.
            }
            if (!allowlisted.Models.Contains(link.Model))
            {
                continue; // model undeclared for this provider -> skip.
            }
            if (!registeredProviderIds.Contains(link.Provider))
            {
                continue; // no DI-registered IModelProvider for this environment -> skip.
            }
            if (ExceedsCeiling(payloadClass, allowlisted.PayloadClassCeiling))
            {
                // privacy floor for THIS call -> skip, not tried (§1b failover law). This link was
                // otherwise a genuine candidate (allowlisted, model-declared, DI-registered) — an
                // empty chain caused ONLY by skips like this one is RefusedPrivacyFloor, not
                // NoRouteConfigured (ProviderChain.AnyCeilingSkip's own doc comment; §10.3 FINDING 2).
                anyCeilingSkip = true;
                continue;
            }

            hops.Add(new ResolvedHop(link.Provider, link.Model));
        }

        return new ProviderChain(hops, anyCeilingSkip);
    }

    /// <summary>
    /// Explicit-pin resolution (§1b): "honored verbatim, no policy override — but allowlist and privacy
    /// floor still bind." Returns a single-hop chain if the pin clears both laws, or an empty chain
    /// (never a silent reroute) if it does not — the caller distinguishes "pinned and cleared" from
    /// "pinned and refused" by inspecting <see cref="ProviderChain.IsEmpty"/>, exactly like the
    /// Automatic path's NoRouteConfigured signal.
    /// </summary>
    public static ProviderChain ResolveExplicitPin(
        ProviderPin pin,
        IReadOnlyList<ProviderAllowlistEntry> allowlist,
        IReadOnlySet<string> registeredProviderIds,
        PayloadClass payloadClass)
    {
        var allowlisted = allowlist.FirstOrDefault(a => a.Name == pin.Provider);
        if (allowlisted is null || !allowlisted.Models.Contains(pin.Model) || !registeredProviderIds.Contains(pin.Provider))
        {
            return new ProviderChain(Array.Empty<ResolvedHop>());
        }
        if (ExceedsCeiling(payloadClass, allowlisted.PayloadClassCeiling))
        {
            // The pin was otherwise a genuine candidate (allowlisted, model-declared, DI-registered) —
            // refused for a privacy reason distinct from "not allowlisted" (§10.3 FINDING 2; mirrors the
            // Automatic path's AnyCeilingSkip).
            return new ProviderChain(Array.Empty<ResolvedHop>(), AnyCeilingSkip: true);
        }
        return new ProviderChain(new[] { new ResolvedHop(pin.Provider, pin.Model) });
    }

    private static IReadOnlyList<TaskChainLink> ChainFor(RoutingPolicy policy, AimlTaskKind task) =>
        policy.TaskChains.TryGetValue(task.ToString(), out var taskChain) && taskChain.Count > 0
            ? taskChain
            : policy.DefaultChain;

    private static bool ExceedsCeiling(PayloadClass payloadClass, PayloadClass ceiling) => payloadClass > ceiling;
}
