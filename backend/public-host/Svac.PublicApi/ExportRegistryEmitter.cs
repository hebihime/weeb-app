using System.Text.Json;
using Svac.DomainCore.Contracts.Export;
using Svac.DomainCore.Export;
using Svac.Identity.Export;

namespace Svac.PublicApi;

/// <summary>
/// Emits backend/domain-core/export-registry.json (SLICE_S3_CONTRACT.md §6b) — the export-side mirror of
/// <see cref="PurgeRegistryEmitter"/>. Pure in-memory data — no DB, no host needed: constructs the SAME
/// boot-time union <c>AddDomainCore</c>/<c>AddIdentityModule</c> assemble via DI (every registered <see
/// cref="IExportRegistrySource"/>), directly, so this CLI mode never needs a live service provider.
/// </summary>
public static class ExportRegistryEmitter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        var outputPath = args.Length > 1 ? args[1] : DefaultOutputPath();

        var sources = new IExportRegistrySource[]
        {
            new CoreExportRegistrySource(),
            new IdentityExportRegistrySource(),
        };
        var registry = new ExportRegistry(sources);

        var json = JsonSerializer.Serialize(
            registry.Entries
                .OrderBy(e => e.StoreKey, StringComparer.Ordinal)
                .Select(e => new { storeKey = e.StoreKey, state = e.State.ToString(), reason = e.Reason }),
            SerializerOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json + Environment.NewLine);
        Console.WriteLine($"emit-export-registry: wrote {outputPath} ({registry.Entries.Count} entries)");
        return 0;
    }

    private static string DefaultOutputPath()
    {
        // backend/public-host/Svac.PublicApi/bin/Debug/net10.0 -> backend/domain-core/export-registry.json
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "backend", "domain-core", "export-registry.json");
    }
}
