using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Policy;

/// <summary>
/// The enforced <see cref="IPolicyTable"/> — the boot-time UNION of every registered <see
/// cref="IPolicyTableSource"/> (PHASE_2A_SUBSTRATE.md §1, SLICE_S5_CONTRACT.md §1d: "the concrete
/// IPolicyTable becomes the boot-time UNION of all registered sources; a duplicate action key across
/// sources ⇒ boot refusal"). No slice ever edits this table directly — every module contributes rows by
/// registering its OWN <see cref="IPolicyTableSource"/>.
///
/// The convenience parameterless/optional constructor defaults to <c>[new CorePolicyTableSource()]</c> —
/// exactly what the pre-Phase-2a S1 <c>PolicyTable</c> shipped — so every existing direct construction
/// site (<c>new PolicyTable()</c>, dozens of tests) stays byte-identical without edits. DI composition
/// (<c>AddDomainCore</c>) instead resolves the real <c>IEnumerable&lt;IPolicyTableSource&gt;</c> from the
/// container, which always supplies an actual (possibly multi-element) collection — the optional
/// default only ever fires for direct, non-DI construction.
/// </summary>
public sealed class PolicyTable : IPolicyTable
{
    public IReadOnlyList<PolicyTableEntry> Entries { get; }

    private readonly IReadOnlyDictionary<string, PolicyTableEntry> _byAction;

    public PolicyTable(IEnumerable<IPolicyTableSource>? sources = null)
    {
        var effectiveSources = sources ?? new IPolicyTableSource[] { new CorePolicyTableSource() };

        var entries = new List<PolicyTableEntry>();
        var byAction = new Dictionary<string, PolicyTableEntry>();
        foreach (var source in effectiveSources)
        {
            foreach (var entry in source.Entries)
            {
                if (!byAction.TryAdd(entry.Action, entry))
                {
                    throw new InvalidOperationException(
                        $"4A boot refusal: action key \"{entry.Action}\" is registered by more than one " +
                        "IPolicyTableSource (PHASE_2A_SUBSTRATE.md §1) — every action key must be owned by " +
                        "exactly one source; a module ADDS its own verbs, it never edits another source's.");
                }
                entries.Add(entry);
            }
        }

        Entries = entries;
        _byAction = byAction;
    }

    public PolicyTableEntry? Find(string action) => _byAction.GetValueOrDefault(action);
}
