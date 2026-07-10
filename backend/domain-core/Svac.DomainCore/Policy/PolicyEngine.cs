using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Policy;

/// <summary>
/// The 4A mutation chokepoint's runtime evaluator (SLICE_S1_CONTRACT.md §1b, §3). Fail-closed on an
/// unmapped action (never Allow by default) and fail-closed on an actor kind absent from the row's
/// allowlist. Axis evaluation (premium/reputation/mode/verification) is identity at S1 (§9: real
/// modifiers land S16/S19/S23) — an actor-kind match with no axis check failing is Allow.
///
/// Consumer-denial coercion (dedupe: SilentRej-L1/L2/L3, SECURITY_REVIEW_S1.md): DenyStandard is legal
/// only for staff/partner actor kinds (§1b) — a CONSUMER (User/Anonymous) must never observe it, however
/// it would have been reached. Before this fix, DenyFor applied a row's declared mode to WHOEVER was
/// denied regardless of THEIR kind (LEAK 1: every DenyStandard row in the shipped table handed a
/// consumer an observable 403 + reason key; LEAK 2: a staff-only row denies a consumer by OMISSION, which
/// the static "consumer kind explicitly listed" guard never catches), and the unmapped-action fail-closed
/// path returned DenyStandard for ANY actor including a consumer (LEAK 3). One coercion fixes all three:
/// whenever the ACTOR BEING DENIED is a consumer kind, the decision is ALWAYS DenyAsAbsence, regardless of
/// what the table row (or the unmapped-action fallback) would otherwise have produced.
/// </summary>
public sealed class PolicyEngine(IPolicyTable table) : IPolicyEngine
{
    private static readonly HashSet<ActorKind> ConsumerActorKinds = new() { ActorKind.User, ActorKind.Anonymous };

    public PolicyDecision Authorize(ActorRef actor, string action, TargetRef target)
    {
        var entry = table.Find(action);
        if (entry is null)
        {
            // Fail-closed on an action the table has never heard of. The startup boot-refusal check
            // (Hosting) should make this unreachable for any real mutation endpoint; a direct
            // IPolicyEngine.Authorize call for an unregistered action still gets a deny, never a
            // silent allow. A consumer actor gets absence (§8: "excluded read ≡ nonexistent read"), never
            // the standard deny shape a staff/partner actor gets to see (SilentRej-L3).
            return IsConsumer(actor.Kind) ? PolicyDecision.AsAbsence : PolicyDecision.Standard("policy.denied.unmapped_action");
        }

        if (!entry.ActorKinds.Contains(actor.Kind))
        {
            // Deny-by-omission (SilentRej-L1/L2): a consumer denied because their kind is absent from
            // the row's allowlist ALWAYS renders as absence, never the row's own declared DenyMode.
            return IsConsumer(actor.Kind) ? PolicyDecision.AsAbsence : DenyFor(entry);
        }

        // Role-level staff restriction (e.g. SuperAdmin/EconomyOps, recorded in StaffRoleAllowlistNote)
        // is not yet structurally checkable: staff role claims are Identity/auth's seam (S3/S5, §9
        // dependency classification — "staff policy rows exist but are unexercisable until S5's Entra").
        // Every other axis (premium/reputation/mode/verification) defaults identity at S1.
        return PolicyDecision.Allowed;
    }

    private static bool IsConsumer(ActorKind kind) => ConsumerActorKinds.Contains(kind);

    private static PolicyDecision DenyFor(PolicyTableEntry entry) => entry.DenyMode switch
    {
        PolicyDenyMode.DenyAsAbsence => PolicyDecision.AsAbsence,
        PolicyDenyMode.DenySilentAs404 => PolicyDecision.Silent404,
        PolicyDenyMode.DenyAsLimit => PolicyDecision.AsLimit(entry.QuotaKeyForLimit ?? entry.Action),
        PolicyDenyMode.DenyStandard => PolicyDecision.Standard(entry.ReasonKey),
        _ => throw new InvalidOperationException($"Unhandled PolicyDenyMode: {entry.DenyMode}"),
    };
}
