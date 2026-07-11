using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

/// <summary>
/// Golden-vector proof for <see cref="HatFor"/> (PHASE_2A_SUBSTRATE.md §3, SLICE_S5_CONTRACT.md §1b:
/// "the least-privileged role among the actor's grants that satisfies the policy row ... privilege
/// order: SuperAdmin &gt; the four operational roles &gt; Analyst; ties by enum ordinal"). Uses raw
/// ordinals matching StaffRole's declared order (SuperAdmin=0, SafetyAgent=1, ContentModerator=2,
/// VenueConOps=3, EconomyOps=4, Analyst=5) — the real StaffRole enum lives in Contracts, which this
/// zero-dependency assembly cannot reference (see HatFor.cs's own doc comment).
/// </summary>
public sealed class HatForTests
{
    private const int SuperAdmin = 0;
    private const int SafetyAgent = 1;
    private const int ContentModerator = 2;
    private const int VenueConOps = 3;
    private const int EconomyOps = 4;
    private const int Analyst = 5;

    [Fact]
    public void SingleHeldRankInAllowedSet_IsSelected()
    {
        var selected = HatFor.SelectLeastPrivileged(
            heldRanks: new HashSet<int> { SuperAdmin },
            allowedRanks: new HashSet<int> { SuperAdmin, EconomyOps });

        Assert.Equal(SuperAdmin, selected);
    }

    [Fact]
    public void MultipleHeldRanksInAllowedSet_SelectsTheLeastPrivileged_LargestOrdinal()
    {
        // SuperAdmin=0 (most privileged) and EconomyOps=4 both held and both allowed — the LEAST
        // privileged (largest ordinal, EconomyOps) is the correct "acting hat".
        var selected = HatFor.SelectLeastPrivileged(
            heldRanks: new HashSet<int> { SuperAdmin, EconomyOps },
            allowedRanks: new HashSet<int> { SuperAdmin, EconomyOps });

        Assert.Equal(EconomyOps, selected);
    }

    [Fact]
    public void HeldRanksOutsideAllowedSet_AreIgnored()
    {
        var selected = HatFor.SelectLeastPrivileged(
            heldRanks: new HashSet<int> { SafetyAgent, ContentModerator, Analyst },
            allowedRanks: new HashSet<int> { SuperAdmin, EconomyOps });

        Assert.Null(selected);
    }

    [Fact]
    public void NoOverlapBetweenHeldAndAllowed_ReturnsNull_TheCallersJobToDeny()
    {
        var selected = HatFor.SelectLeastPrivileged(
            heldRanks: new HashSet<int> { Analyst },
            allowedRanks: new HashSet<int> { SuperAdmin });

        Assert.Null(selected);
    }

    [Fact]
    public void EmptyHeldRanks_ReturnsNull()
    {
        var selected = HatFor.SelectLeastPrivileged(
            heldRanks: new HashSet<int>(),
            allowedRanks: new HashSet<int> { SuperAdmin, EconomyOps, VenueConOps });

        Assert.Null(selected);
    }

    [Fact]
    public void AllSixRolesHeld_RowAllowsAll_SelectsAnalyst_TheLeastPrivileged()
    {
        var allRoles = new HashSet<int> { SuperAdmin, SafetyAgent, ContentModerator, VenueConOps, EconomyOps, Analyst };

        var selected = HatFor.SelectLeastPrivileged(heldRanks: allRoles, allowedRanks: allRoles);

        Assert.Equal(Analyst, selected);
    }
}
