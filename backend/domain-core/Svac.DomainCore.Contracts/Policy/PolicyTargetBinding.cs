namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// How a mapped endpoint CONVEYS the 4A target to <see cref="IPolicyEngine.Authorize"/> (PHASE_2A_
/// SUBSTRATE.md §1, SLICE_S3_CONTRACT.md §3a — the Auth-F3 chokepoint redesign). Closed union:
/// <see cref="None"/> is today's S1/S2 behavior (<c>TargetRef.ForAction(action)</c>, byte-identical);
/// <see cref="SelfAccount"/> conveys the caller's OWN id, resolved from the session, never from
/// route/body/client input; <see cref="FromRoute"/> conveys a real resource id read out of the route.
/// </summary>
public abstract record PolicyTargetBinding
{
    public sealed record NoneBinding : PolicyTargetBinding;

    public sealed record SelfAccountBinding : PolicyTargetBinding;

    public sealed record FromRouteBinding(string ParamName, string ResourceType) : PolicyTargetBinding;

    /// <summary>Today's S1/S2 behavior — the endpoint conveys no real target, byte-identical.</summary>
    public static readonly PolicyTargetBinding None = new NoneBinding();

    /// <summary>The target IS the caller's own account id, taken from the session — never client input.</summary>
    public static readonly PolicyTargetBinding SelfAccount = new SelfAccountBinding();

    /// <summary>The target is a real resource id read from the named route parameter.</summary>
    public static PolicyTargetBinding FromRoute(string paramName, string resourceType) => new FromRouteBinding(paramName, resourceType);
}
