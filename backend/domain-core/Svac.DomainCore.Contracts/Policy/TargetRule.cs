namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// What a PolicyTable row DEMANDS of the target it is authorized against (PHASE_2A_SUBSTRATE.md §1,
/// SLICE_S3_CONTRACT.md §3a). Closed union: <see cref="ActionScoped"/> is the S1/S2 default (no
/// target-ownership check at all — the row cares only about action + actor kind, byte-identical);
/// <see cref="SelfOnly"/> demands the target's resource id equal the actor's own id; <see
/// cref="OwnedResource"/> demands the registered <see cref="IResourceOwnershipResolver"/> for that
/// resource type resolve the target to the actor.
/// </summary>
public abstract record TargetRule
{
    public sealed record ActionScopedRule : TargetRule;

    public sealed record SelfOnlyRule : TargetRule;

    public sealed record OwnedResourceRule(string ResourceType) : TargetRule;

    /// <summary>The S1/S2 default: no target-ownership check — the row is scoped to the ACTION alone.</summary>
    public static readonly TargetRule ActionScoped = new ActionScopedRule();

    /// <summary>The target must be the caller's own account/resource id.</summary>
    public static readonly TargetRule SelfOnly = new SelfOnlyRule();

    /// <summary>The target must resolve, via the registered resolver for <paramref name="resourceType"/>, to the acting caller.</summary>
    public static TargetRule OwnedResource(string resourceType) => new OwnedResourceRule(resourceType);
}
