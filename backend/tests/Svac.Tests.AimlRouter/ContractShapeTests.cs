using Svac.AimlRouter.Contracts;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>Trivial container test (SLICE_PLAYBOOK.md Phase 1 gate): the module's public contract types compile, construct, and round-trip.</summary>
public sealed class ContractShapeTests
{
    [Fact]
    public void AimlInvocationId_New_ProducesAivPrefixedUlid()
    {
        var id = AimlInvocationId.New(DateTimeOffset.UtcNow, Random.Shared);

        Assert.StartsWith("aiv_", id.Value, StringComparison.Ordinal);
        Assert.Equal(26, id.Value.Length - "aiv_".Length);
    }

    [Fact]
    public void AimlResult_ClosedUnion_DistinguishesSuccessFromFailure()
    {
        AimlResult success = AimlResult.Ok(AimlPayload.ForOutput("hi"), MakeReceipt());
        AimlResult failure = AimlResult.Failed(AimlFailure.NoRouteConfigured);

        Assert.IsType<AimlResult.Success>(success);
        Assert.IsType<AimlResult.Failure>(failure);
        Assert.Equal(AimlFailure.NoRouteConfigured, Assert.IsType<AimlResult.Failure>(failure).Cause);
    }

    [Fact]
    public void AimlRequest_RequiresPayloadClass_NoImplicitDefault()
    {
        // Compiles only because every positional parameter, including PayloadClass, is supplied
        // explicitly — SLICE_S2_CONTRACT.md §1b: "PayloadClass ... REQUIRED; no default."
        var request = new AimlRequest(
            Task: AimlTaskKind.EvalProbe,
            Caller: CallerModule.System,
            PayloadClass: PayloadClass.NonPersonal,
            Subject: null,
            Payload: AimlPayload.ForUserTurn("ping"),
            TargetLocale: null,
            ExplicitPin: null);

        Assert.Equal(PayloadClass.NonPersonal, request.PayloadClass);
    }

    private static RoutingReceipt MakeReceipt() => new(
        InvocationId: AimlInvocationId.New(DateTimeOffset.UtcNow, Random.Shared),
        Provider: "seed",
        Model: "seed-v0",
        DecisionSource: DecisionSource.Policy,
        PolicyVersion: 1,
        FallbackDepth: 0,
        LatencyMs: 1,
        InputTokens: 1,
        OutputTokens: 1);
}
