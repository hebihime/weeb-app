using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Policy;

/// <summary>Axes a policy row can gate on (SLICE_S1_CONTRACT.md §1b). Identity/no-op modifiers at S1 (§9 dependency seams); real evaluation lands S16/S19/S23.</summary>
[Flags]
public enum PolicyAxis
{
    None = 0,
    Role = 1,
    Premium = 2,
    Reputation = 4,
    Mode = 8,
    Verification = 16,

    /// <summary>The account-state axis (PHASE_2A_SUBSTRATE.md §1, SLICE_S3_CONTRACT.md §3a) for the CI action×axis matrix generator. Real evaluation reads <see cref="PolicyTableEntry.AllowedAccountStates"/>.</summary>
    AccountState = 32,
}

/// <summary>The declarative deny mode a table row carries, closed to match PolicyDecision's union (SLICE_S1_CONTRACT.md §1b, §3).</summary>
public enum PolicyDenyMode
{
    DenyStandard,
    DenyAsAbsence,
    DenySilentAs404,
    DenyAsLimit,
}

/// <summary>
/// One row of the checked-in typed C# policy table (SLICE_S1_CONTRACT.md §1b, §3): {action, actorKinds,
/// axes, denyMode, requires_reason}. CI generates the action×axis matrix suite FROM this same table
/// object — there is exactly one source of truth for what the engine enforces.
/// </summary>
public sealed record PolicyTableEntry(
    string Action,
    IReadOnlySet<ActorKind> ActorKinds,
    PolicyAxis Axes,
    PolicyDenyMode DenyMode,
    bool RequiresReason,
    string ReasonKey,
    string? StaffRoleAllowlistNote = null,
    string? QuotaKeyForLimit = null,
    bool DynamicQuotaKey = false,
    // Marks a row as authorizing a READ, not a mutation (SilentRej-L2, SECURITY_REVIEW_S1.md: "an arch
    // test fails any policy entry mapping a consumer actor to DenyStandard on a read path"). Every S1
    // row is false — S1 ships zero consumer-reachable reads (§0) — so this activates the static
    // deny-by-omission guard the moment a future slice adds a real read-path row, rather than firing
    // noisily against today's System/Staff-only mutation rows (which the 4A engine's own consumer-denial
    // coercion, PolicyEngine.cs, already renders as absence for any consumer regardless of this flag).
    bool IsReadPath = false,
    // --- Phase-2a additive (PHASE_2A_SUBSTRATE.md §1) — every S1/S2 row leaves all three at their
    // default, so every existing decision stays byte-identical; these three axes are ANDed onto the
    // existing actor-kind check inside PolicyEngine.Authorize. ---
    /// <summary>What the row DEMANDS of its target (SLICE_S3_CONTRACT.md §3a). Default ActionScoped = today's S1/S2 behavior (no target-ownership check).</summary>
    TargetRule? TargetRule = null,
    /// <summary>Null = "any account state" (S1/S2 default). Non-null restricts a User actor to these account states, resolved server-side, never client input.</summary>
    IReadOnlySet<string>? AllowedAccountStates = null,
    /// <summary>Null = "no role restriction". Non-null requires a Staff actor's grants (IStaffRoleResolver) to intersect this set.</summary>
    IReadOnlySet<StaffRole>? StaffRoles = null)
{
    /// <summary>
    /// DenyAsLimit rows must carry either a fixed quota key OR be marked DynamicQuotaKey (the row is the
    /// "internal chokepoint" documentation entry for core.quota.consume, §3 — the real key is supplied
    /// per-call by IQuotaService.Consume's caller, not baked into the static table). Enforced at
    /// construction, not by convention.
    /// </summary>
    public PolicyTableEntry Validate()
    {
        if (DenyMode == PolicyDenyMode.DenyAsLimit && string.IsNullOrEmpty(QuotaKeyForLimit) && !DynamicQuotaKey)
        {
            throw new InvalidOperationException($"policy row \"{Action}\": DenyAsLimit requires QuotaKeyForLimit or DynamicQuotaKey.");
        }
        return this;
    }
}
