using Svac.DomainCore.Contracts.Api;

namespace Svac.DomainCore.Contracts.Purge;

/// <summary>One rendered purge-run receipt row (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.md §1d).</summary>
public sealed record PurgeRunView(string RunId, string PurgeClass, string SubjectRef, string StoreKey, int RowsAffected, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

public sealed record PurgeRunPage(IReadOnlyList<PurgeRunView> Items, string? NextCursor, bool HasMore);

/// <summary>
/// Read-only pillar contract over <c>core.purge_runs</c> (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.md
/// §1d) — the desk-tile read side of 13A's receipts. Implemented in Svac.DomainCore this surgery; zero
/// callers exist at S1/S2.
/// </summary>
public interface IPurgeRunReader
{
    public Task<PurgeRunPage> Recent(CursorPageRequest page, CancellationToken ct = default);
}
