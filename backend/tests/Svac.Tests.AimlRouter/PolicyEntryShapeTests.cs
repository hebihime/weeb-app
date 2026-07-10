using Svac.AimlRouter.Policy;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// Confirms the SLICE_S2_CONTRACT.md §3 row's exact shape, AND (Correction 2, §13 ratification) confirms
/// the absence Auth-F3 would otherwise apply to: "aiml.invoke carries no target resource, no request DTO
/// maps to it, and no consumer-actor reachability exists." This IS the S2 security-phase auth/IDOR
/// deliverable for this action — a confirmation, not a redesign.
/// </summary>
public sealed class PolicyEntryShapeTests
{
    [Fact]
    public void AimlInvoke_MatchesContractShape_SystemOnlyDenyStandard()
    {
        var entry = AimlRouterPolicyEntries.AimlInvoke;

        Assert.Equal("aiml.invoke", entry.Action);
        Assert.Equal(new HashSet<ActorKind> { ActorKind.System }, entry.ActorKinds);
        Assert.Equal(PolicyAxis.None, entry.Axes);
        Assert.Equal(PolicyDenyMode.DenyStandard, entry.DenyMode);
        Assert.False(entry.RequiresReason); // "the audit event carries full provenance" (§3).
    }

    [Fact]
    public void AimlInvoke_NoConsumerActorKindReachable()
    {
        // Correction 2: "no consumer-actor reachability exists." Anonymous/User/Staff/Partner must
        // never appear in this row's allowlist — the row is system-only, structurally.
        var entry = AimlRouterPolicyEntries.AimlInvoke;

        Assert.DoesNotContain(ActorKind.Anonymous, entry.ActorKinds);
        Assert.DoesNotContain(ActorKind.User, entry.ActorKinds);
        Assert.DoesNotContain(ActorKind.Staff, entry.ActorKinds);
        Assert.DoesNotContain(ActorKind.Partner, entry.ActorKinds);
    }

    [Fact]
    public void Entries_ContainsExactlyOneRow_PerContractSection3()
    {
        var only = Assert.Single(AimlRouterPolicyEntries.Entries);
        Assert.Same(AimlRouterPolicyEntries.AimlInvoke, only);
    }

    [Fact]
    public void IAimlRouter_InvokeAsync_CarriesNoTargetRefParameter()
    {
        // Structural proof (Correction 2) that no request DTO / resource id maps to aiml.invoke: the
        // ONLY method on the router's public contract takes an AimlRequest + RequestContext, never a
        // TargetRef/resource id — reflected here so a future signature change that added one would
        // fail this test rather than silently reopening the IDOR surface Auth-F3 describes.
        var method = typeof(Svac.AimlRouter.Contracts.IAimlRouter).GetMethod(nameof(Svac.AimlRouter.Contracts.IAimlRouter.InvokeAsync));
        Assert.NotNull(method);
        var parameterTypeNames = method!.GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.DoesNotContain("TargetRef", parameterTypeNames);
    }
}
