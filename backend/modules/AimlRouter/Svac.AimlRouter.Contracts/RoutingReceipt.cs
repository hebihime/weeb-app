namespace Svac.AimlRouter.Contracts;

/// <summary>How the effective (provider, model) pair for this invocation was decided (SLICE_S2_CONTRACT.md §1b).</summary>
public enum DecisionSource
{
    Policy,
    Explicit,
    Failover,
}

/// <summary>
/// Internal telemetry (SLICE_S2_CONTRACT.md §1b): "an arch scan ... proves neither the receipt nor
/// provider identity ever serializes into a user-bound DTO. A reported user can never probe
/// moderation-provider health." Returned to the calling BACKEND MODULE in-process only — never rendered
/// through any client-facing surface.
/// </summary>
public sealed record RoutingReceipt(
    AimlInvocationId InvocationId,
    string Provider,
    string Model,
    DecisionSource DecisionSource,
    int PolicyVersion,
    int FallbackDepth,
    long LatencyMs,
    int InputTokens,
    int OutputTokens,
    string? FailoverFrom = null);
