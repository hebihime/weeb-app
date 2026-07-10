namespace Svac.AimlRouter.Config;

/// <summary>9A key names this module's manifest declares (SLICE_S2_CONTRACT.md §4) — one constant class so no call site hand-types the string twice.</summary>
public static class AimlRouterConfigKeys
{
    public const string ProviderAllowlist = "aiml.provider_allowlist";
    public const string RoutingPolicy = "aiml.routing_policy";
    public const string InvokeTimeoutSeconds = "aiml.invoke_timeout_seconds";

    /// <summary>
    /// The REAL runtime 9A key the daily-call-ceiling budget breaker resolves through
    /// <c>Svac.DomainCore.Quota.QuotaService.Consume</c>'s own <c>quota.&lt;quotaKey&gt;.cap</c>
    /// convention (QuotaService.cs:24) for quotaKey=<see cref="Svac.AimlRouter.Quota.AimlRouterQuotaKeys.CallDaily"/>.
    /// Build-phase fix (backend/tests/Svac.Tests.AimlRouter/BudgetCapConfigKeyWiringTests.cs,
    /// backend/e2e/aiml-router.e2e.mjs FINDING 1): the contract §4 table's literal spelling
    /// <c>aiml.daily_call_ceiling</c> was never read by anything — QuotaService.Consume derives the key
    /// name itself from the quota key, it is never passed a config key directly — so this constant now
    /// names the key that actually gets seeded and read.
    /// </summary>
    public const string DailyCallCeiling = "quota.aiml.call.daily.cap";
}
