namespace Svac.AdminHost.Domain.Config;

/// <summary>
/// The pending_consumer_slice decision function (SLICE_S5_CONTRACT.md §4 judge synthesis §12.7;
/// PHASE_2A_SUBSTRATE.md §6: "this field + its CI wiring is shared node tooling under tools/, Pass C's
/// own deliverable"). Pins the pure decision half of the dead-tunable lint that <c>tools/dead-tunable-
/// lint/dead-tunable-lint.mjs</c> re-implements over the raw manifest JSON for its repo-wide CI sweep
/// (the "a slice marked DONE in BUILD.md with no registered consumer claiming the key" check needs
/// BUILD.md + SLICE_PLAYBOOK.md text, which is node's job, not this assembly's) — this type is the
/// authoritative SPEC of what "a key passes" means, exercised directly by
/// Svac.Tests.AdminHost.V0BatchManifestTests.cs so the rule has a deterministic, gate-lane-fast proof
/// independent of the node tool's own file-parsing.
///
/// A key passes iff EITHER:
///   (a) its manifest row names NO <see cref="Entry.PendingConsumerSlice"/> AND its own
///       <see cref="Entry.Consumer"/> field is non-empty (a REAL, already-shipped consumer) — the
///       ordinary "not pending at all" case every S1/S5 host-tunable key is in; OR
///   (b) its row DOES name a <see cref="Entry.PendingConsumerSlice"/>, and that value is a MEMBER of
///       <paramref name="knownLedgerSlices"/> (a real BUILD.md §7 ledger slice id) — the "honestly
///       pending, naming a real future consumer" case every v0-batch key is in today.
///
/// A row naming a pending slice is judged SOLELY on that slice's validity — the manifest's own
/// <c>consumer</c> text for a pending key is deliberately prose ("pending -- lands with BUILD.md §7
/// slice S18 ...", <see cref="Svac.DomainCore.Config.ConfigManifestEntry.Consumer"/>'s own non-empty-
/// string invariant, enforced elsewhere by <c>ConfigSeedLoader.SeedFromFile</c>), never itself a "real
/// consumer" — if it were, ANY non-empty prose would legalize an invented or already-DONE pending slice
/// forever, exactly the <c>desk_rendered</c> satisfaction mode §12.7 REJECTS ("the desk renders every
/// key regardless, so rendering can never be what makes a key legal"). A row with BOTH an empty consumer
/// and no pending slice is a dead tunable — case (a) and (b) both fail, and this function says so.
/// </summary>
public static class PendingConsumerSliceLint
{
    /// <summary>One manifest row's lint-relevant fields, key-for-key with the real JSON manifest's own
    /// <c>key</c>/<c>consumer</c>/<c>pending_consumer_slice</c> properties.</summary>
    public sealed record Entry(string Key, string Consumer, string? PendingConsumerSlice);

    /// <summary>Returns every violation message; empty means the manifest passes.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<Entry> entries, IReadOnlySet<string> knownLedgerSlices)
    {
        var violations = new List<string>();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.PendingConsumerSlice))
            {
                if (!knownLedgerSlices.Contains(entry.PendingConsumerSlice))
                {
                    violations.Add(
                        $"config key \"{entry.Key}\" names pending_consumer_slice \"{entry.PendingConsumerSlice}\", " +
                        "which is not a real BUILD.md §7 ledger slice — a typo'd or invented slice id is not a free pass.");
                }

                // A validly-pending key needs no real consumer YET, regardless of what its own prose
                // `consumer` text says — the desk-rendered/prose-is-enough mode is rejected by design.
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Consumer))
            {
                violations.Add(
                    $"config key \"{entry.Key}\" has neither a real consumer nor a pending_consumer_slice " +
                    "naming a real BUILD.md §7 ledger slice — a dead tunable.");
            }
        }

        return violations;
    }
}
