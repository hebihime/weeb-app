using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Policy;

/// <summary>
/// The 4A mutation chokepoint's runtime evaluator (SLICE_S1_CONTRACT.md §1b, §3; PHASE_2A_SUBSTRATE.md
/// §1). Fail-closed on an unmapped action (never Allow by default) and fail-closed on an actor kind
/// absent from the row's allowlist. Axis evaluation (premium/reputation/mode/verification) is identity at
/// S1 (§9: real modifiers land S16/S19/S23) — an actor-kind match with no axis check failing is Allow.
///
/// Consumer-denial coercion (dedupe: SilentRej-L1/L2/L3, SECURITY_REVIEW_S1.md): DenyStandard is legal
/// only for staff/partner actor kinds (§1b) — a CONSUMER (User/Anonymous) must never observe it, however
/// it would have been reached. One coercion fixes all three leaks: whenever the ACTOR BEING DENIED is a
/// consumer kind, the decision is ALWAYS DenyAsAbsence, regardless of what the table row (or the
/// unmapped-action fallback, or any of the three Phase-2a axes below) would otherwise have produced.
///
/// PHASE_2A_SUBSTRATE.md §1 — three ADDITIVE axes, ANDed onto the existing actor-kind check, evaluated
/// only when a row actually declares them (every S1/S2 row leaves all three at their default, so this is
/// a structural no-op for every existing decision):
///   1. Target-ownership (TargetRule: SelfOnly / OwnedResource) — S3.
///   2. accountState (PolicyTableEntry.AllowedAccountStates) — S3.
///   3. Staff Role (PolicyTableEntry.StaffRoles, via IStaffRoleResolver) — S5.
/// Authorize is now ASYNC because axes 1 and 3 are async DB reads (IResourceOwnershipResolver.OwnerOf,
/// IStaffRoleResolver.GrantsOf) — a signature change to a DONE interface, byte-identical in DECISION for
/// every S1/S2 caller, which now just <c>await</c>s.
/// </summary>
public sealed class PolicyEngine : IPolicyEngine
{
    private static readonly HashSet<ActorKind> ConsumerActorKinds = new() { ActorKind.User, ActorKind.Anonymous };

    private readonly IPolicyTable _table;
    private readonly Dictionary<string, IResourceOwnershipResolver> _ownershipResolversByType;
    private readonly IStaffRoleResolver _staffRoleResolver;
    private readonly IRequestContextAccessor? _requestContextAccessor;

    public PolicyEngine(
        IPolicyTable table,
        IEnumerable<IResourceOwnershipResolver>? ownershipResolvers = null,
        IStaffRoleResolver? staffRoleResolver = null,
        IRequestContextAccessor? requestContextAccessor = null)
    {
        _table = table;
        _ownershipResolversByType = (ownershipResolvers ?? Enumerable.Empty<IResourceOwnershipResolver>())
            .ToDictionary(r => r.ResourceType);
        _staffRoleResolver = staffRoleResolver ?? new DenyAllStaffRoleResolver();
        _requestContextAccessor = requestContextAccessor;
    }

    public async Task<PolicyDecision> Authorize(ActorRef actor, string action, TargetRef target, CancellationToken ct = default)
    {
        var entry = _table.Find(action);
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

        // --- Phase-2a axis 1: target-ownership (S3). Every S1/S2 row leaves TargetRule null/ActionScoped
        // — this switch's default arm (no case matches) is the S1/S2 no-op. ---
        switch (entry.TargetRule)
        {
            case TargetRule.SelfOnlyRule:
                if (target.ResourceId is null || target.ResourceId != actor.Id.ToString())
                {
                    return IsConsumer(actor.Kind) ? PolicyDecision.AsAbsence : DenyFor(entry);
                }
                break;

            case TargetRule.OwnedResourceRule owned:
                if (target.ResourceId is null || !_ownershipResolversByType.TryGetValue(owned.ResourceType, out var resolver))
                {
                    // No resolver registered for this resource type (S1/S2: none are) — unresolvable
                    // ownership denies exactly like an unknown/foreign resource (deny-as-absence folded
                    // into one branch, SilentRej-L4).
                    return IsConsumer(actor.Kind) ? PolicyDecision.AsAbsence : DenyFor(entry);
                }

                var owner = await resolver.OwnerOf(target.ResourceId, ct);
                if (owner is null || owner.Value != actor.Id)
                {
                    // Unknown id ⇒ owner null ⇒ deny-as-absence — nonexistent and foreign are ONE branch.
                    return IsConsumer(actor.Kind) ? PolicyDecision.AsAbsence : DenyFor(entry);
                }
                break;
        }

        // --- Phase-2a axis 2: accountState (S3). Null AllowedAccountStates (every S1/S2 row) is a no-op. ---
        if (entry.AllowedAccountStates is not null && actor.Kind == ActorKind.User)
        {
            var currentAccountState = _requestContextAccessor?.Current.AccountState;
            if (currentAccountState is null || !entry.AllowedAccountStates.Contains(currentAccountState))
            {
                return IsConsumer(actor.Kind) ? PolicyDecision.AsAbsence : DenyFor(entry);
            }
        }

        // --- Phase-2a axis 3: staff Role (S5). Null StaffRoles (every S1/S2 row) is a no-op; this axis
        // only ever evaluates for a Staff actor, never a consumer, so no consumer-coercion branch here. ---
        if (actor.Kind == ActorKind.Staff && entry.StaffRoles is not null)
        {
            var grants = await _staffRoleResolver.GrantsOf(actor, ct);
            if (!grants.Overlaps(entry.StaffRoles))
            {
                return DenyFor(entry);
            }
        }

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
