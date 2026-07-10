namespace Svac.DomainCore.Deterministic;

/// <summary>One appended ledger movement, enough to summate — not the full persisted row shape.</summary>
public readonly record struct LedgerMovement(int Points, int Xp, long Svac, bool IsSinkPurchase);

/// <summary>Summed balance, matching core.ledger_balances (SLICE_S1_CONTRACT.md §2).</summary>
public readonly record struct LedgerBalanceTotals(long Points, long Xp, long Svac);

/// <summary>
/// Pure ledger arithmetic (SLICE_S1_CONTRACT.md §1b, §2). Balances derive by summation — no mutable
/// balance column exists anywhere; this is the one place that summation logic lives so the projection
/// runner and every future reconciliation job call the same pure function instead of re-deriving it.
/// </summary>
public static class LedgerMath
{
    /// <summary>
    /// Validates the two database-enforced invariants BEFORE a movement is appended, so a caller gets a
    /// clear exception instead of relying solely on the Postgres CHECK constraint to catch a bug:
    /// xp = points (1:1 law) and svac is negative only for a sink_purchase movement.
    /// </summary>
    public static void ValidateMovement(LedgerMovement movement)
    {
        if (movement.Points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(movement), movement.Points, "points must never be negative.");
        }
        if (movement.Xp != movement.Points)
        {
            throw new ArgumentException($"xp ({movement.Xp}) must equal points ({movement.Points}) — the 1:1 law.", nameof(movement));
        }
        if (movement.Svac < 0 && !movement.IsSinkPurchase)
        {
            throw new ArgumentException("svac may only be negative on a sink_purchase movement.", nameof(movement));
        }
    }

    /// <summary>Sums a stream of movements into balance totals. Empty input yields the zero balance.</summary>
    public static LedgerBalanceTotals Sum(IEnumerable<LedgerMovement> movements)
    {
        long points = 0, xp = 0, svac = 0;
        foreach (var m in movements)
        {
            ValidateMovement(m);
            points += m.Points;
            xp += m.Xp;
            svac += m.Svac;
        }
        return new LedgerBalanceTotals(points, xp, svac);
    }

    /// <summary>Folds one new movement onto an existing running total (the projection runner's per-event step).</summary>
    public static LedgerBalanceTotals Apply(LedgerBalanceTotals running, LedgerMovement movement)
    {
        ValidateMovement(movement);
        return new LedgerBalanceTotals(
            running.Points + movement.Points,
            running.Xp + movement.Xp,
            running.Svac + movement.Svac);
    }

    /// <summary>Removes a previously-applied movement (a reversal): the inverse fold.</summary>
    public static LedgerBalanceTotals Reverse(LedgerBalanceTotals running, LedgerMovement movement)
    {
        return new LedgerBalanceTotals(
            running.Points - movement.Points,
            running.Xp - movement.Xp,
            running.Svac - movement.Svac);
    }
}
