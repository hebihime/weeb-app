using Svac.DomainCore.Contracts.Purge;

namespace Svac.Identity.Deletion;

/// <summary>
/// The 13A custody-hold consult seam (SLICE_S3_CONTRACT.md §2 Phase P step 1, ER-14): "consult the 13A
/// custody-hold registry and RECORD the answer on the job row EVEN WHEN EMPTY — no holds can exist before
/// S12; the check is structural now." Identity does not yet own any custody-hold DATA (S12 is the future
/// slice that opens/releases holds against open reports) — this seam exists so the consult is a real,
/// structural call from day one rather than a TODO, and so a red fixture (a test-registered
/// <c>InMemoryCustodyHoldRegistry</c> in Svac.Tests.Identity) can prove the "held stores are skipped, rest
/// proceeds, release re-enqueues" behavior end-to-end without S12 existing yet.
/// </summary>
/// <summary>One open custody hold, scoped to the specific store keys it protects (S1's bare <see cref="CustodyHold"/> carries no store scope — identity is the first real consumer of the seam, so this is where that scope is added). An empty <see cref="HeldStoreKeys"/> would mean "holds nothing" and is never produced by a real hold.</summary>
public sealed record CustodyHoldScope(CustodyHold Hold, IReadOnlySet<string> HeldStoreKeys);

public interface ICustodyHoldRegistry
{
    /// <summary>Every open custody hold for this subject. Empty (never null) when there are none — the caller records the empty answer, it never skips the consult.</summary>
    public Task<IReadOnlyList<CustodyHoldScope>> HoldsFor(string accountId, CancellationToken ct = default);
}

/// <summary>The real, production posture (SLICE_S3_CONTRACT.md §2): no custody-hold data exists before S12, so every consult truthfully answers "none" — a real empty answer, not a bypassed check.</summary>
public sealed class NoCustodyHoldRegistry : ICustodyHoldRegistry
{
    private static readonly IReadOnlyList<CustodyHoldScope> Empty = Array.Empty<CustodyHoldScope>();

    public Task<IReadOnlyList<CustodyHoldScope>> HoldsFor(string accountId, CancellationToken ct = default) => Task.FromResult(Empty);
}
