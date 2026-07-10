using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

public sealed class LedgerMathTests
{
    [Fact]
    public void ValidateMovement_PointsNegative_Throws()
    {
        var movement = new LedgerMovement(Points: -1, Xp: -1, Svac: 0, IsSinkPurchase: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => LedgerMath.ValidateMovement(movement));
    }

    [Fact]
    public void ValidateMovement_XpNotEqualToPoints_Throws()
    {
        var movement = new LedgerMovement(Points: 10, Xp: 5, Svac: 0, IsSinkPurchase: false);
        Assert.Throws<ArgumentException>(() => LedgerMath.ValidateMovement(movement));
    }

    [Fact]
    public void ValidateMovement_NegativeSvacOnNonSinkPurchase_Throws()
    {
        var movement = new LedgerMovement(Points: 10, Xp: 10, Svac: -5, IsSinkPurchase: false);
        Assert.Throws<ArgumentException>(() => LedgerMath.ValidateMovement(movement));
    }

    [Fact]
    public void ValidateMovement_NegativeSvacOnSinkPurchase_IsValid()
    {
        var movement = new LedgerMovement(Points: 0, Xp: 0, Svac: -5, IsSinkPurchase: true);
        LedgerMath.ValidateMovement(movement); // does not throw
    }

    [Fact]
    public void ValidateMovement_ValidEarnMovement_IsValid()
    {
        var movement = new LedgerMovement(Points: 10, Xp: 10, Svac: 2, IsSinkPurchase: false);
        LedgerMath.ValidateMovement(movement); // does not throw
    }

    [Fact]
    public void Sum_EmptySequence_YieldsZeroBalance()
    {
        var totals = LedgerMath.Sum(Array.Empty<LedgerMovement>());
        Assert.Equal(new LedgerBalanceTotals(0, 0, 0), totals);
    }

    [Fact]
    public void Sum_MultipleMovements_AccumulatesCorrectly()
    {
        var movements = new[]
        {
            new LedgerMovement(10, 10, 2, IsSinkPurchase: false),
            new LedgerMovement(5, 5, 1, IsSinkPurchase: false),
            new LedgerMovement(0, 0, -3, IsSinkPurchase: true),
        };

        var totals = LedgerMath.Sum(movements);

        Assert.Equal(new LedgerBalanceTotals(15, 15, 0), totals);
    }

    [Fact]
    public void ApplyThenReverse_ReturnsToTheOriginalBalance()
    {
        var running = new LedgerBalanceTotals(100, 100, 20);
        var movement = new LedgerMovement(10, 10, 3, IsSinkPurchase: false);

        var applied = LedgerMath.Apply(running, movement);
        var reversed = LedgerMath.Reverse(applied, movement);

        Assert.Equal(running, reversed);
    }

    [Fact]
    public void Sum_RejectsAnInvalidMovementEvenMidSequence()
    {
        var movements = new[]
        {
            new LedgerMovement(10, 10, 0, IsSinkPurchase: false),
            new LedgerMovement(5, 5, -1, IsSinkPurchase: false), // invalid: negative svac, not a sink
        };

        Assert.Throws<ArgumentException>(() => LedgerMath.Sum(movements));
    }
}
