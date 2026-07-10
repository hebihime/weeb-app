namespace Svac.AimlRouter.Quota;

/// <summary>
/// The module's 10A quota declaration (SLICE_S2_CONTRACT.md §5): "One internal key, live, the router
/// itself as consumer ... the runaway-spend/runaway-loop circuit breaker at the single egress, day one."
/// Naming convention for future consumer-facing keys (reserved, NOT this module's to spend):
/// <c>aiml.&lt;task&gt;.&lt;window&gt;</c>, consumed at the CONSUMER's call site upstream of the router
/// (§5) — this module registers exactly one key, its own.
/// </summary>
public static class AimlRouterQuotaKeys
{
    /// <summary>Actor = `sys_&lt;caller_module&gt;`, window daily/UTC, cap = 9A `aiml.daily_call_ceiling` (§5).</summary>
    public const string CallDaily = "aiml.call.daily";
}
