using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Ledger;

/// <summary>One append to the ledger (SLICE_S1_CONTRACT.md §1b, §2; questsystem §Day-One verbatim).</summary>
public sealed record LedgerEntry(
    string UserId,
    string? CrewId,
    string EventType,
    int Points,
    int Xp,
    long Svac,
    string? QuestId,
    string? EvidenceRef);

/// <summary>Balance snapshot returned by BalanceOf — derived by summation, never a mutable column.</summary>
public sealed record LedgerBalance(long Points, long Xp, long Svac);

/// <summary>
/// The ledger seam (SLICE_S1_CONTRACT.md §1b), quest-ready day one. xp = points 1:1 enforced at append
/// AND by CHECK; svac accrues only on enumerated event types (empty enumeration at S1); sink_purchase =
/// negative svac only; points/xp never negative; balances derive by summation.
/// </summary>
public interface ILedger
{
    public Task<string> Append(LedgerEntry entry, ActorRef actor, RequestContext ctx, CancellationToken ct = default);

    /// <summary>Reversal is the ONLY correction verb — data surgery has no policy entry, hence impossible.</summary>
    public Task<string> Reverse(string entryId, ActorRef actor, string reason, RequestContext ctx, CancellationToken ct = default);

    public Task<LedgerBalance> BalanceOf(string userId, CancellationToken ct = default);
}
