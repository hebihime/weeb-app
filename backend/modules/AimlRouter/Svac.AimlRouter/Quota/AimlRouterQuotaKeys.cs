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
    /// <summary>
    /// Actor = `sys_&lt;caller_module&gt;`, window daily/UTC, cap = 9A
    /// <see cref="Svac.AimlRouter.Config.AimlRouterConfigKeys.DailyCallCeiling"/> (§5) — the REAL runtime
    /// key <c>Svac.DomainCore.Quota.QuotaService.Consume</c> derives as <c>quota.{this}.cap</c>.
    /// </summary>
    public const string CallDaily = "aiml.call.daily";
}
