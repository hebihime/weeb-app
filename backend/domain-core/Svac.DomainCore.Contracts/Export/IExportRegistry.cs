namespace Svac.DomainCore.Contracts.Export;

/// <summary>
/// The declarative state a store carries in the export registry (SLICE_S3_CONTRACT.md §6b) — the export
/// mirror of 13A's <c>PurgeVerb</c>. <see cref="Contributes"/> means a real, DI-registered <see
/// cref="IExportContributor"/> exists for the store's key and the export worker calls it; <see
/// cref="NotExportable"/>/<see cref="Withheld"/> are REGISTERED dispositions (mirrors <see
/// cref="ExportDisposition"/>'s closed union at the per-call level) — never a silent omission.
/// </summary>
public enum ExportRegistryState
{
    Contributes,
    NotExportable,
    Withheld,
}

/// <summary>One compile-time cell of the export registry: what a store DOES for export (SLICE_S3_CONTRACT.md §6b).</summary>
public sealed record ExportRegistrationEntry(string StoreKey, ExportRegistryState State, string Reason);

/// <summary>
/// A module's own additive slice of the export registry (SLICE_S3_CONTRACT.md §6b) — the export-side
/// mirror of <see cref="Svac.DomainCore.Contracts.Policy.IPolicyTableSource"/>'s DI-union pattern. Every
/// PII-holding module registers its OWN source; the boot-time union (<see cref="IExportRegistry"/>) is
/// what the export⋈purge cross-gate and the export worker both read. Domain-core registers the source
/// covering S1's own stores; S3 (identity) registers the source covering identity's stores AND the S1
/// event/ledger stores it is the first real consumer of (§6b: "S3 registers contributors for every
/// existing subject-bearing store").
/// </summary>
public interface IExportRegistrySource
{
    public IReadOnlyList<ExportRegistrationEntry> Entries { get; }
}

/// <summary>
/// Read-only access to the boot-time UNION of every registered <see cref="IExportRegistrySource"/>
/// (SLICE_S3_CONTRACT.md §6b) — the export-side mirror of <see
/// cref="Svac.DomainCore.Contracts.Purge.IPurgeRegistry"/>. The export⋈purge cross-gate arch test reads
/// this against <see cref="Svac.DomainCore.Contracts.Purge.IPurgeRegistry"/>'s registered store keys; the
/// export worker reads it to resolve which <see cref="IExportContributor"/> to call for each
/// <see cref="ExportRegistryState.Contributes"/> entry.
/// </summary>
public interface IExportRegistry
{
    public IReadOnlyList<ExportRegistrationEntry> Entries { get; }
    public IReadOnlySet<string> RegisteredStoreKeys { get; }
    public IEnumerable<ExportRegistrationEntry> EntriesFor(string storeKey);
}
