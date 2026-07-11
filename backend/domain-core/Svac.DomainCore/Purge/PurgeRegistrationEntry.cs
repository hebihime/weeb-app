using Svac.DomainCore.Contracts.Purge;

namespace Svac.DomainCore.Purge;

/// <summary>One compile-time cell of the 13A registry: what verb a store performs for a purge class (SLICE_S1_CONTRACT.md §6).</summary>
public sealed record PurgeRegistrationEntry(string StoreKey, PurgeClass PurgeClass, PurgeVerb Verb, string Reason);

/// <summary>
/// A module's own additive slice of the 13A registry (SLICE_S3_CONTRACT.md §6a) — the purge-side mirror
/// of <see cref="Svac.DomainCore.Contracts.Export.IExportRegistrySource"/>/<see
/// cref="Svac.DomainCore.Contracts.Policy.IPolicyTableSource"/>'s DI-union pattern. Domain-core registers
/// the source covering S1's own stores (<see cref="CorePurgeRegistrySource"/>); S3 (identity) registers
/// the source covering every identity store (SLICE_S3_CONTRACT.md §6a). A module ADDS its own store keys,
/// it never edits another source's rows — <see cref="PurgeRegistry"/>'s constructor boot-refuses on a
/// storeKey claimed by more than one source.
/// </summary>
public interface IPurgeRegistrySource
{
    public IReadOnlyList<PurgeRegistrationEntry> Entries { get; }
}

/// <summary>
/// Read-only access to the compiled 13A registry (SLICE_S1_CONTRACT.md §1b/§6). The CI gate (arch test)
/// enumerates every EF entity type + every declared blob/cache store and fails the build on any store
/// absent here — proven non-vacuous by a red fixture (an unregistered fixture table fails the gate).
/// EntriesFor is named to avoid CA1716 — "For" alone collides with a VB.NET reserved keyword.
/// </summary>
public interface IPurgeRegistry
{
    public IReadOnlyList<PurgeRegistrationEntry> Entries { get; }
    public IReadOnlySet<string> RegisteredStoreKeys { get; }
    public IEnumerable<PurgeRegistrationEntry> EntriesFor(string storeKey);
}
