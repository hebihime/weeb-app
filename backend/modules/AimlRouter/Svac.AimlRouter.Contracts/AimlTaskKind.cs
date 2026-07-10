namespace Svac.AimlRouter.Contracts;

/// <summary>
/// CLOSED (SLICE_S2_CONTRACT.md §1b). Pre-declared from RATIFIED consumer specs only (T9 text triage,
/// T8, S25/S36 generate, plus the router's own eval probe) — activating a consumer is policy DATA (a
/// routing-policy task_chain entry), never a contract change. <c>ModerateImage</c>/<c>MediaScan</c> is
/// deliberately ABSENT: its payload shape (media refs) cannot be designed before S11; it arrives as ONE
/// additive versioned contract change (kind + payload together) when S11 exists to specify it. A kind
/// with no policy chain fails closed (<see cref="AimlFailure.NoRouteConfigured"/>).
/// </summary>
public enum AimlTaskKind
{
    Generate,
    ModerateText,
    Translate,
    EvalProbe,
}
