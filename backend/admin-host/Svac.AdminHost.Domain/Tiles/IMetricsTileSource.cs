using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Tiles;

/// <summary>One supplementary stat line under a tile's headline value — a keyed label (never a literal, §8 seam 14) paired with an already-rendered value string.</summary>
public sealed record MetricsTileDetail(string LabelKey, string Value);

/// <summary>
/// A tile's rendered content (SLICE_S5_CONTRACT.md §8 seam 2): <see cref="PrimaryValue"/> is the ONE
/// headline number backend/e2e/admin-host.e2e.mjs's wire contract renders as
/// <c>&lt;span data-testid="tile-value-&lt;tileId&gt;"&gt;</c> — a real, already-computed string (never a
/// live number formatted client-side), so "real-or-honestly-dark" is provable by reading this record's
/// own construction, not by trusting the markup. <see cref="Details"/> are zero or more secondary lines
/// (breakdowns, most-recent timestamps) rendered underneath, with no wire-contract testid of their own.
/// </summary>
public sealed record MetricsTileResult(string PrimaryValue, IReadOnlyList<MetricsTileDetail> Details);

/// <summary>
/// The tile-registration seam (SLICE_S5_CONTRACT.md §8 seam 2): "a tile with no live source is NOT
/// registered (real-or-honestly-dark, never fabricated)." Every registered <see cref="IMetricsTileSource"/>
/// wraps a REAL read over data already flowing (S1's config-change/purge-run/stream-volume/staff-sign-in
/// events, S2's aiml.route_decided) — there is no "empty tile" variant analogous to
/// <c>EmptyUserSearchSource</c>, because a tile with nothing to read is simply never registered at all,
/// per the seam's own law.
/// </summary>
public interface IMetricsTileSource
{
    /// <summary>Stable identity (log lines, the wire-contract's <c>data-testid="tile-&lt;tileId&gt;"</c>) — never rendered as text.</summary>
    public string TileId { get; }

    /// <summary>Keyed string (AdminStringCatalog) for the tile's own heading — never a literal.</summary>
    public string TitleKey { get; }

    /// <summary>Which staff roles ever see this tile. Every S1/S2 tile is aggregate-only data (counts,
    /// never a user-PII row), so every one of them is visible to all six roles — Analyst's whole scope
    /// (§3: "admin.dashboard.read ... all six roles — Analyst's whole scope").</summary>
    public IReadOnlySet<StaffRole> VisibleTo { get; }

    public Task<MetricsTileResult> Query(CancellationToken ct = default);
}
