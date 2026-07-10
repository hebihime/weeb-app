using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;

namespace Svac.DomainCore.Quota;

/// <summary>
/// Premium cap modifier — identity function at S1 (SLICE_S1_CONTRACT.md §5, §9). Real impl lands S23.
/// </summary>
public sealed class PremiumCapModifier : ICapModifier
{
    public int Modify(int baseCap, ActorRef actor) => baseCap;
}

/// <summary>
/// Reputation-tier cap modifier — identity function at S1 (SLICE_S1_CONTRACT.md §5, §9). Real impl lands S16.
/// </summary>
public sealed class ReputationCapModifier : ICapModifier
{
    public int Modify(int baseCap, ActorRef actor) => baseCap;
}

/// <summary>
/// Mode cap modifier — identity function at S1 (SLICE_S1_CONTRACT.md §5, §9). Real impl lands S19.
/// </summary>
public sealed class ModeCapModifier : ICapModifier
{
    public int Modify(int baseCap, ActorRef actor) => baseCap;
}
