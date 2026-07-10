namespace Svac.AimlRouter.Contracts;

/// <summary>
/// Internal cause union (SLICE_S2_CONTRACT.md §1b) — never a silent drop (15A failure row). FAILURE
/// UNOBSERVABILITY (P3, §12.7): consumers map ANY of these to their EXISTING standard error path. There
/// is no per-cause message key — a per-cause key would build a rendering surface for an actor with no
/// UI and leak provider state to adversaries.
/// </summary>
public enum AimlFailure
{
    NoRouteConfigured,
    NotAllowlisted,
    RefusedPrivacyFloor,
    BudgetDenied,
    Timeout,
    ProviderError,
    ChainExhausted,
    InvalidRequest,
}
