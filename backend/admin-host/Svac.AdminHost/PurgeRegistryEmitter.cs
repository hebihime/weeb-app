using System.Text.Json;
using Svac.AdminHost.Domain.Purge;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Purge;

namespace Svac.AdminHost;

/// <summary>
/// Emits ONLY the admin host's own additive slice of the 13A registry (SLICE_S5_CONTRACT.md §6) — pure
/// in-memory data, no DB, no host, mirrors Svac.PublicApi.PurgeRegistryEmitter's shape exactly. This
/// process may never reference Svac.PublicApi (§0 law c, the OTHER direction of the admin trust-boundary
/// rule — even though the rule as WRITTEN only names the PublicApi->AdminHost direction, keeping this
/// emitter's own dependency surface admin-only is what makes that boundary hold both ways in practice),
/// so backend/domain-core/purge-registry.json — the ONE committed, repo-wide union of every host's
/// registrations — is assembled by build/scripts/emit-purge-registry.sh MERGING this fragment with
/// Svac.PublicApi's own (core+identity) fragment, never by one binary knowing about every host.
/// </summary>
public static class PurgeRegistryEmitter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        var outputPath = args.Length > 1 ? args[1] : DefaultOutputPath();

        var registry = new PurgeRegistry(new IPurgeRegistrySource[] { new AdminPurgeRegistrySource() });

        var json = JsonSerializer.Serialize(
            registry.Entries.Select(e => new { storeKey = e.StoreKey, purgeClass = e.PurgeClass.ToString(), verb = e.Verb.ToString(), reason = e.Reason }),
            SerializerOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json + Environment.NewLine);
        Console.WriteLine($"emit-purge-registry (admin fragment): wrote {outputPath} ({registry.Entries.Count} entries)");
        return 0;
    }

    private static string DefaultOutputPath()
    {
        // backend/admin-host/Svac.AdminHost/bin/Debug/net10.0 -> repo root -> a scratch fragment path.
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "backend", "domain-core", "purge-registry.admin-fragment.json");
    }
}
