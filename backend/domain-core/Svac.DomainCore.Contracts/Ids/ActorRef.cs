namespace Svac.DomainCore.Contracts.Ids;

/// <summary>The kind of actor performing an action (SLICE_S1_CONTRACT.md §1b, RequestContext).</summary>
public enum ActorKind
{
    Anonymous,
    User,
    Staff,
    Partner,
    System,
}

/// <summary>
/// A reference to the actor performing an action. The id's prefix carries the kind (IdPrefixes), so
/// ActorKind here is redundant-but-explicit: it lets 4A policy rows match on kind without re-parsing the
/// id, and it is the field an arch test cross-checks against the id prefix for consistency.
/// </summary>
public readonly record struct ActorRef(OpaqueId Id, ActorKind Kind)
{
    /// <summary>The well-known system actor used for scheduler/seed-loader/purge-pipeline writes.</summary>
    public static ActorRef System(OpaqueId systemId) => new(systemId, ActorKind.System);

    public override string ToString() => $"{Kind}:{Id}";
}

/// <summary>A reference to the target of an authorization check — a resource, or an action's own scope.</summary>
public readonly record struct TargetRef(string ResourceType, string? ResourceId)
{
    public static TargetRef ForAction(string resourceType) => new(resourceType, null);
}
