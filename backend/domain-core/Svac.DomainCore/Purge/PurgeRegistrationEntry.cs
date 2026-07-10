using Svac.DomainCore.Contracts.Purge;

namespace Svac.DomainCore.Purge;

/// <summary>One compile-time cell of the 13A registry: what verb a store performs for a purge class (SLICE_S1_CONTRACT.md §6).</summary>
public sealed record PurgeRegistrationEntry(string StoreKey, PurgeClass PurgeClass, PurgeVerb Verb, string Reason);

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
