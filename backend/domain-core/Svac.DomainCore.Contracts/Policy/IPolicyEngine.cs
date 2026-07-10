using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// The 4A mutation chokepoint (SLICE_S1_CONTRACT.md §1b). Authorize is the ONE place a mutation is
/// allowed or denied; middleware refuses any mutation endpoint with no policy entry, fail-closed, twice
/// (startup boot-refusal AND request-time refusal).
/// </summary>
public interface IPolicyEngine
{
    public PolicyDecision Authorize(ActorRef actor, string action, TargetRef target);
}

/// <summary>
/// Read-only access to the declarative policy table an IPolicyEngine evaluates against (SLICE_S1_
/// CONTRACT.md §3). Exposed separately from IPolicyEngine so arch tests / the generated action×axis
/// matrix suite / the startup boot-refusal check can enumerate rows without needing a live engine.
/// </summary>
public interface IPolicyTable
{
    public IReadOnlyList<PolicyTableEntry> Entries { get; }
    public PolicyTableEntry? Find(string action);
}

/// <summary>
/// Marks a mapped endpoint/verb with the 4A action it authorizes against (SLICE_S1_CONTRACT.md §3). The
/// Hosting startup check refuses to boot if any non-GET endpoint lacks this attribute mapping to a table
/// row; the Hosting request-time filter calls IPolicyEngine.Authorize using the action named here.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class PolicyActionAttribute(string action) : Attribute
{
    public string Action { get; } = action;
}
