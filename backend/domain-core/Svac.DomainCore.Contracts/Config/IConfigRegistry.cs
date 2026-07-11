using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Config;

/// <summary>Scope a config key resolves at (SLICE_S1_CONTRACT.md §2 CHECK constraint).</summary>
public enum ConfigScope
{
    Founder,
    Ops,
    Set,
}

/// <summary>One typed 9A config-table row's static declaration (name/type/scope/bounds), keyed by the manifest loader.</summary>
public sealed record ConfigKeyDeclaration(
    string Key,
    ConfigScope Scope,
    Type ValueType,
    object? Bounds,
    bool RequiresReason,
    string Consumer);

/// <summary>
/// One stored 9A row's editor-facing read shape (PHASE_2A_SUBSTRATE.md §2, SLICE_S5_CONTRACT.md §1d) —
/// the admin config editor's <see cref="IConfigRegistry.ListEntries"/> source, never a second bounds
/// implementation (the editor renders THIS row's own bounds, it never re-validates).
/// </summary>
public sealed record ConfigEntryView(
    string Key,
    string Type,
    string Scope,
    string ValueJson,
    string? BoundsJson,
    bool RequiresReason,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>
/// The 9A typed config seam (SLICE_S1_CONTRACT.md §1b, §4). GetValue is typed + bounds-validated.
/// SetValue is staff/system only via 4A and appends an Audit-stream event in the SAME tx. A config key
/// with no registered consumer fails a lint (§4) — the registry never accretes dead tunables.
/// (Contract pseudocode names these Get/Set; renamed for CA1716 — both collide with VB.NET reserved
/// keywords and this repo's analyzers treat that as a build-breaking warning.)
/// </summary>
public interface IConfigRegistry
{
    public Task<T> GetValue<T>(string key, CancellationToken ct = default);
    public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default);

    /// <summary>[S5, PHASE_2A_SUBSTRATE.md §2] Lists every registered 9A row for the admin config editor. No S1/S2 code calls this — additive, unreferenced.</summary>
    public Task<IReadOnlyList<ConfigEntryView>> ListEntries(CancellationToken ct = default);
}
