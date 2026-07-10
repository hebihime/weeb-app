using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AimlRouter.Policy;

/// <summary>
/// The module's 4A policy declaration (SLICE_S2_CONTRACT.md §3): "One row, internal chokepoint, S1
/// `core.ledger.append` precedent — the row's existence makes an ungated route unshippable forever."
///
/// Unlike `core.ledger.append` (which LedgerService calls live on every Append), `aiml.invoke` has no
/// live caller to gate at S2: it is "system only — never client-reachable, never staff-reachable from
/// consumer apps; no request DTO maps to it (structural, arch-asserted)" (§3), and S2 ships zero
/// consumers (§0) — there is no request path for a live Authorize() call to sit on yet, the same way
/// `core.quota.consume`'s row documents an internal chokepoint without QuotaService itself calling
/// IPolicyEngine (verified against QuotaService.cs while building this module). This type is therefore
/// the module-owned, testable DECLARATION of the row's exact shape (§3's table, made a real C# object,
/// `.Validate()`-checked exactly like every domain-core row) — the first-real-consumer wiring that
/// SPLICES this row into a live host's `IPolicyTable` is deliberately NOT built here: S2's own scope
/// ruling (§0) names only `backend/modules/AimlRouter/**` + one strengthening pass on
/// `ProviderSdkArchTest.cs` + this module's own test/eval projects + one 9A manifest — it does not
/// authorize editing `Svac.DomainCore.Policy.PolicyTable`. The security phase's job (Correction 2) is to
/// CONFIRM the absence this row documents, not to wire it live; whichever slice gives `aiml.invoke` a
/// real caller wires the splice then.
/// </summary>
public static class AimlRouterPolicyEntries
{
    public const string Invoke = "aiml.invoke";

    public static readonly PolicyTableEntry AimlInvoke = new PolicyTableEntry(
        Action: Invoke,
        ActorKinds: new HashSet<ActorKind> { ActorKind.System },
        Axes: PolicyAxis.None,
        DenyMode: PolicyDenyMode.DenyStandard,
        RequiresReason: false,
        ReasonKey: "policy.denied.aiml_invoke",
        StaffRoleAllowlistNote: "system only — never client-reachable, never staff-reachable from consumer apps; no request DTO maps to it (structural, arch-asserted)").Validate();

    /// <summary>Every row this module contributes to the 4A table — exactly one, per §3.</summary>
    public static readonly IReadOnlyList<PolicyTableEntry> Entries = new[] { AimlInvoke };
}
