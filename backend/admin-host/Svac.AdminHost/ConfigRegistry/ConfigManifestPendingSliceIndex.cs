using System.Text.Json;

namespace Svac.AdminHost.ConfigRegistry;

/// <summary>
/// Builds the key → pending-consumer-slice map the Config Registry desk renders as a "consumer lands at
/// S&lt;N&gt;" chip (SLICE_S5_CONTRACT.md §4: "honestly dark, still editable"). <see
/// cref="Svac.DomainCore.Contracts.Config.ConfigEntryView"/> (PHASE_2A_SUBSTRATE.md §2, the Phase-2a
/// domain-core delta) carries no <c>pending_consumer_slice</c> field — that field is additive manifest
/// DATA, deliberately NOT threaded through the locked domain-core contract (PHASE_2A_SUBSTRATE.md §6:
/// "shared node tooling under tools/, Pass C's own deliverable... rides the S5 build" — the SAME
/// reasoning applies to this host-side reader). This type reads the two REAL committed manifest files
/// directly, at the SAME relative paths <c>Program.cs</c>'s own <c>SeedConfigOnStartup</c> uses, so the
/// chip a staff member sees is always sourced from the actual shipped manifest, never a second,
/// independently-maintained copy of the pending-slice data.
/// </summary>
public static class ConfigManifestPendingSliceIndex
{
    public static IReadOnlyDictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in ManifestPaths())
        {
            if (!File.Exists(path))
            {
                continue; // mirrors SeedConfigOnStartup's own "log and continue" posture — a missing
                          // manifest at request time means an honest absence of chips, never a crash.
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (entry.TryGetProperty("pending_consumer_slice", out var pending) && pending.ValueKind == JsonValueKind.String)
                {
                    var slice = pending.GetString();
                    if (!string.IsNullOrWhiteSpace(slice))
                    {
                        result[keyEl.GetString()!] = slice!;
                    }
                }
            }
        }

        return result;
    }

    private static string[] ManifestPaths() => new[]
    {
        Path.Combine(AppContext.BaseDirectory, "config", "admin-host.config.json"),
        Path.Combine(AppContext.BaseDirectory, "config", "v0-batch.config.json"),
    };
}
