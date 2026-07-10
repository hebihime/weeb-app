namespace Svac.AimlRouter.Config;

/// <summary>9A key names this module's manifest declares (SLICE_S2_CONTRACT.md §4) — one constant class so no call site hand-types the string twice.</summary>
public static class AimlRouterConfigKeys
{
    public const string ProviderAllowlist = "aiml.provider_allowlist";
    public const string RoutingPolicy = "aiml.routing_policy";
    public const string InvokeTimeoutSeconds = "aiml.invoke_timeout_seconds";
    public const string DailyCallCeiling = "aiml.daily_call_ceiling";
}
