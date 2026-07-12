using Svac.DomainCore.Contracts;

namespace Svac.AdminHost.Domain.Execution;

/// <summary>
/// The audited-action chokepoint's closed outcome union (SLICE_S5_CONTRACT.md §1c) — mirrors <see
/// cref="Svac.DomainCore.Contracts.Policy.PolicyDecision"/>'s own closed-union shape so callers
/// pattern-match, never string-sniff.
/// </summary>
public abstract record AdminActionResult
{
    /// <summary>
    /// <paramref name="Context"/> is the SAME enriched <see cref="RequestContext"/> (hat + full
    /// roles_held snapshot on <see cref="RequestContext.Staff"/>) that <c>work</c> itself received —
    /// handed back so the caller (a Razor page / form-post handler) can render "acting as &lt;hat&gt;"
    /// without a second lookup.
    /// </summary>
    public sealed record Success(RequestContext Context) : AdminActionResult;

    /// <summary>
    /// Refused BEFORE <c>Authorize</c> (§1c: "whitespace = refused before Authorize") — a
    /// <c>requires_reason</c> row given a null/whitespace reason. NEVER audited (nothing chokepoint-worthy
    /// was even attempted) and <c>work</c> is NEVER invoked.
    /// </summary>
    public sealed record ReasonRequired : AdminActionResult;

    /// <summary>
    /// <c>policyEngine.Authorize</c> denied (or the staff actor's own row was inactive/missing — the
    /// re-read at step 1 denies the same shape). <c>work</c> NEVER invoked. Audited:
    /// <c>admin.action.refused</c> (metadata only — action/target_ref/hat/roles_held/reason, never a
    /// second copy of <c>work</c>'s own effects, because <c>work</c> never ran).
    /// </summary>
    public sealed record Denied(string ReasonKey) : AdminActionResult;

    /// <summary>
    /// 9A <c>admin.four_eyes_required</c> is true AND the computed hat != <see
    /// cref="Svac.DomainCore.Contracts.Policy.StaffRole.SuperAdmin"/> AND the row <c>RequiresReason</c> —
    /// fail-closed placeholder (§4). <c>work</c> NEVER invoked. Audited exactly like <see cref="Denied"/>
    /// (same <c>admin.action.refused</c> shape) — a four-eyes refusal IS a refusal.
    /// </summary>
    public sealed record FourEyesRequired : AdminActionResult;
}
