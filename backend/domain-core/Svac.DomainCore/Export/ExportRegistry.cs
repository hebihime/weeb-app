using Svac.DomainCore.Contracts.Export;

namespace Svac.DomainCore.Export;

/// <summary>
/// The boot-time UNION of every registered <see cref="IExportRegistrySource"/> (SLICE_S3_CONTRACT.md
/// §6b) — the export-side mirror of <see cref="Svac.DomainCore.Policy.PolicyTable"/>'s union-of-sources
/// pattern. A duplicate store key across two sources is a boot refusal (a store's disposition must have
/// exactly one owner), mirroring <c>PolicyTable</c>'s own duplicate-action-key throw.
/// </summary>
public sealed class ExportRegistry : IExportRegistry
{
    public IReadOnlyList<ExportRegistrationEntry> Entries { get; }

    public IReadOnlySet<string> RegisteredStoreKeys { get; }

    public ExportRegistry(IEnumerable<IExportRegistrySource> sources)
    {
        var entries = sources.SelectMany(s => s.Entries).ToList();

        var duplicates = entries
            .GroupBy(e => e.StoreKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"export-registry BOOT REFUSAL: store key(s) [{string.Join(", ", duplicates)}] are registered by more than one IExportRegistrySource — a store's export disposition must have exactly one owner.");
        }

        Entries = entries;
        RegisteredStoreKeys = entries.Select(e => e.StoreKey).ToHashSet();
    }

    public IEnumerable<ExportRegistrationEntry> EntriesFor(string storeKey) => Entries.Where(e => e.StoreKey == storeKey);
}
