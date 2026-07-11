namespace Svac.DomainCore.Deterministic;

/// <summary>
/// Pure "which hat acted" rank selection (PHASE_2A_SUBSTRATE.md §3, SLICE_S5_CONTRACT.md §1b: "the
/// least-privileged role among the actor's grants that satisfies the policy row ... privilege order:
/// SuperAdmin &gt; the four operational roles &gt; Analyst; ties by enum ordinal"). Operates on raw enum
/// ordinals rather than the concrete <c>StaffRole</c> type: this assembly is zero-dependency and
/// arch-tested to stay that way (SLICE_S1_CONTRACT.md §1a), while <c>StaffRole</c> lives in
/// <c>Svac.DomainCore.Contracts.Policy</c>, which itself references THIS assembly — StaffRole could
/// never flow the other direction without a circular project reference. The real call site (S5 build,
/// <c>AdminActionExecutor</c>) converts <c>StaffRole</c> values to/from <see cref="int"/> at the boundary
/// via <c>(int)role</c> / <c>(StaffRole)rank</c>, which is exactly what a golden-vectored pure function
/// needs: plain integers in, plain integers out.
/// </summary>
public static class HatFor
{
    /// <summary>
    /// Among <paramref name="heldRanks"/> ∩ <paramref name="allowedRanks"/>, returns the LEAST-privileged
    /// rank — the largest ordinal (StaffRole's declared order runs most- to least-privileged, so a larger
    /// ordinal is always less privileged; ties are impossible since ordinals are unique per enum member,
    /// which is exactly what "ties by enum ordinal" resolves to in a total order). Null when the actor
    /// holds none of the allowed ranks — the caller's job to deny, not this function's.
    /// </summary>
    public static int? SelectLeastPrivileged(IReadOnlySet<int> heldRanks, IReadOnlySet<int> allowedRanks)
    {
        int? best = null;
        foreach (var rank in heldRanks)
        {
            if (allowedRanks.Contains(rank) && (best is null || rank > best.Value))
            {
                best = rank;
            }
        }
        return best;
    }
}
