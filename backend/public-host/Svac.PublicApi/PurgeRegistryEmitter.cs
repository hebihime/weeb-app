using System.Text.Json;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Purge;
using Svac.Identity.Purge;

namespace Svac.PublicApi;

/// <summary>
/// Emits backend/domain-core/purge-registry.json (SLICE_S1_CONTRACT.md §6, SLICE_S3_CONTRACT.md §6a):
/// "Registry compiled + emitted ... so the CI gate diffs EF surface vs registrations." Pure in-memory
/// data — no DB, no host needed: constructs the SAME boot-time union AddDomainCore/AddIdentityModule
/// assemble via DI (every registered IPurgeRegistrySource), directly, exactly like
/// <see cref="ExportRegistryEmitter"/>.
/// </summary>
public static class PurgeRegistryEmitter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        var outputPath = args.Length > 1 ? args[1] : DefaultOutputPath();

        var sources = new IPurgeRegistrySource[]
        {
            new CorePurgeRegistrySource(),
            new IdentityPurgeRegistrySource(),
        };
        var registry = new PurgeRegistry(sources);

        var json = JsonSerializer.Serialize(
            registry.Entries.Select(e => new { storeKey = e.StoreKey, purgeClass = e.PurgeClass.ToString(), verb = e.Verb.ToString(), reason = e.Reason }),
            SerializerOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json + Environment.NewLine);
        Console.WriteLine($"emit-purge-registry: wrote {outputPath} ({registry.Entries.Count} entries)");
        return 0;
    }

    private static string DefaultOutputPath()
    {
        // backend/public-host/Svac.PublicApi/bin/Debug/net10.0 -> backend/domain-core/purge-registry.json
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "backend", "domain-core", "purge-registry.json");
    }
}
