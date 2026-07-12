using Svac.DomainCore.Contracts.Purge;

namespace Svac.DomainCore.Purge;

/// <summary>
/// The enforced <see cref="IPurgeRegistry"/> — the boot-time UNION of every registered <see
/// cref="IPurgeRegistrySource"/> (SLICE_S3_CONTRACT.md §6a) — the purge-side mirror of <see
/// cref="Svac.DomainCore.Policy.PolicyTable"/>/<see cref="Svac.DomainCore.Export.ExportRegistry"/>'s
/// union-of-sources pattern. A storeKey registered by more than one source is a boot refusal (a store's
/// purge posture must have exactly one owner), mirroring <c>PolicyTable</c>/<c>ExportRegistry</c>'s own
/// duplicate-key throw.
///
/// The convenience parameterless/optional constructor defaults to <c>[new CorePurgeRegistrySource()]</c>
/// — exactly what the pre-Phase-2a S1 <c>PurgeRegistry</c> shipped — so every existing direct
/// construction site (<c>new PurgeRegistry()</c>, dozens of S1 tests) stays byte-identical without edits.
/// DI composition (<c>AddDomainCore</c>) instead resolves the real <c>IEnumerable&lt;IPurgeRegistrySource&gt;</c>
/// from the container, which always supplies every registered source (domain-core's own, PLUS identity's,
/// once <c>AddIdentityModule</c> registers it) — the optional default only ever fires for direct,
/// non-DI construction.
///
/// Entries preserve first-occurrence (registration) order rather than a source-internal-only order — this
/// is what lets a source declare an intentional cross-store execution dependency (SLICE_S3_CONTRACT.md
/// §6a's email_challenges-keyed-by-email S2 scar: identity's own source lists
/// <c>identity.email_challenges</c> before <c>identity.accounts</c> so the challenge purge can still read
/// the account's live email before the account's own Tombstone verb nulls it — see
/// <c>PurgePipeline.Run</c>'s iteration, which now walks <see cref="Entries"/> in this same order instead
/// of an unordered <see cref="IReadOnlySet{T}"/>).
/// </summary>
public sealed class PurgeRegistry : IPurgeRegistry
{
    public IReadOnlyList<PurgeRegistrationEntry> Entries { get; }

    public IReadOnlySet<string> RegisteredStoreKeys { get; }

    public PurgeRegistry(IEnumerable<IPurgeRegistrySource>? sources = null)
    {
        var effectiveSources = sources ?? new IPurgeRegistrySource[] { new CorePurgeRegistrySource() };

        var entries = new List<PurgeRegistrationEntry>();
        var seenStoreKeys = new HashSet<string>();
        foreach (var source in effectiveSources)
        {
            var sourceStoreKeys = source.Entries.Select(e => e.StoreKey).ToHashSet();
            var collision = sourceStoreKeys.FirstOrDefault(seenStoreKeys.Contains);
            if (collision is not null)
            {
                throw new InvalidOperationException(
                    $"purge-registry BOOT REFUSAL: store key \"{collision}\" is registered by more than one " +
                    "IPurgeRegistrySource (SLICE_S3_CONTRACT.md §6a) — a store's purge posture must have " +
                    "exactly one owner; a module ADDS its own store keys, it never edits another source's.");
            }
            seenStoreKeys.UnionWith(sourceStoreKeys);
            entries.AddRange(source.Entries);
        }

        Entries = entries;
        RegisteredStoreKeys = seenStoreKeys;
    }

    public IEnumerable<PurgeRegistrationEntry> EntriesFor(string storeKey) => Entries.Where(e => e.StoreKey == storeKey);
}
