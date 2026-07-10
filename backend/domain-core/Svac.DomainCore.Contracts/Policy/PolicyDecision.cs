namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// The 4A mutation chokepoint's decision — a CLOSED UNION (SLICE_S1_CONTRACT.md §1b). The deny MODE is a
/// column of the declarative PolicyTable, not per-endpoint judgment.
/// </summary>
public abstract record PolicyDecision
{
    public sealed record Allow : PolicyDecision;

    /// <summary>The excluded read is indistinguishable from a resource that never existed (token law 3).</summary>
    public sealed record DenyAsAbsence : PolicyDecision;

    /// <summary>Same wire shape as a nonexistent resource — silent-rejection unobservability (R5/12A-r).</summary>
    public sealed record DenySilentAs404 : PolicyDecision;

    /// <summary>Maps 1:1 to the single LimitReached component — there is no second deny shape to render.</summary>
    public sealed record DenyAsLimit(string QuotaKey) : PolicyDecision;

    /// <summary>
    /// A staff/partner-actor-only standard deny with a message key. An arch test fails any policy entry
    /// mapping a consumer actor to DenyStandard on a read path (contract-lint invariant 4, source-level).
    /// </summary>
    public sealed record DenyStandard(string ReasonKey) : PolicyDecision;

    public static readonly PolicyDecision Allowed = new Allow();
    public static readonly PolicyDecision AsAbsence = new DenyAsAbsence();
    public static readonly PolicyDecision Silent404 = new DenySilentAs404();
    public static PolicyDecision AsLimit(string quotaKey) => new DenyAsLimit(quotaKey);
    public static PolicyDecision Standard(string reasonKey) => new DenyStandard(reasonKey);

    public bool IsAllowed => this is Allow;
}
