using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Export;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.Export;
using Svac.DomainCore.Purge;
using Svac.Identity.DependencyInjection;
using Svac.Identity.Export;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The export⋈purge CROSS-GATE (SLICE_S3_CONTRACT.md §6b, THE centerpiece): every store in
/// <c>purge-registry.json</c> declaring any subject-scoped purge verb MUST appear in
/// <c>export-registry.json</c> with a contributor OR a declared disposition — the structural form of
/// "preference_answers ALWAYS in export" (profilemodel §12). Red-fixture-proven BOTH directions: an
/// unregistered purge store fails the gate (<see
/// cref="RedFixture_APurgeStoreWithNoExportRegistryEntry_FailsTheGate"/>); a store with a declared
/// disposition passes (<see cref="RedFixture_AStoreWithADeclaredDisposition_PassesTheGate"/>).
/// </summary>
public sealed class ExportPurgeCrossGateTests
{
    private static readonly System.Text.Json.JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private static ExportRegistry BuildRealExportRegistry() =>
        new(new IExportRegistrySource[] { new CoreExportRegistrySource(), new IdentityExportRegistrySource() });

    [Fact]
    public void EveryPurgeRegistryStore_HasAnExportRegistryEntry()
    {
        var purgeRegistry = new PurgeRegistry();
        var exportRegistry = BuildRealExportRegistry();

        var missing = FindMissingExportEntries(purgeRegistry.RegisteredStoreKeys, exportRegistry.RegisteredStoreKeys);
        Assert.Empty(missing);
    }

    [Fact]
    public void RedFixture_APurgeStoreWithNoExportRegistryEntry_FailsTheGate()
    {
        var fixturePurgeStoreKeys = new HashSet<string>(new PurgeRegistry().RegisteredStoreKeys) { "fixture_purge_only_store" };
        var exportStoreKeys = BuildRealExportRegistry().RegisteredStoreKeys;

        var missing = FindMissingExportEntries(fixturePurgeStoreKeys, exportStoreKeys);
        Assert.Single(missing);
        Assert.Equal("fixture_purge_only_store", missing[0]);
    }

    [Fact]
    public void RedFixture_AStoreWithADeclaredDisposition_PassesTheGate()
    {
        // The reverse direction: the gate is "has a REGISTERED entry", never "has a contributor" — a
        // NotExportable/Withheld disposition is a legitimate pass, not a gap. quota_counters is real,
        // non-vacuous proof: it is a genuine purge-registry.json member with zero export contributor.
        var purgeRegistry = new PurgeRegistry();
        var exportRegistry = BuildRealExportRegistry();

        Assert.Contains("quota_counters", purgeRegistry.RegisteredStoreKeys);
        var entry = Assert.Single(exportRegistry.EntriesFor("quota_counters"));
        Assert.Equal(ExportRegistryState.NotExportable, entry.State);
        Assert.False(string.IsNullOrWhiteSpace(entry.Reason));

        var missing = FindMissingExportEntries(purgeRegistry.RegisteredStoreKeys, exportRegistry.RegisteredStoreKeys);
        Assert.DoesNotContain("quota_counters", missing);
    }

    [Fact]
    public void EveryContributesEntry_ResolvesToARealRegisteredIExportContributor()
    {
        // The runtime backstop ExportWorker.Execute throws on ("export-registry declares Contributes ...
        // but no IExportContributor is registered") can never fire in the real host: proven here via the
        // SAME DI composition Program.cs runs (AddDomainCore + AddIdentityModule), never a hand-typed
        // contributor list that could drift from the real registration set.
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-export-cross-gate-check-only", devSeamsEnabled: true);
        services.AddIdentityModule("Host=localhost;Database=svac-export-cross-gate-check-only", smtpOptions: null);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IExportRegistry>();
        var registeredContributorKeys = provider.GetServices<IExportContributor>().Select(c => c.StoreKey).ToHashSet();

        var missing = registry.Entries
            .Where(e => e.State == ExportRegistryState.Contributes)
            .Select(e => e.StoreKey)
            .Where(key => !registeredContributorKeys.Contains(key))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryNonContributesEntry_CarriesAStatedReason()
    {
        var exportRegistry = BuildRealExportRegistry();
        var unreasoned = exportRegistry.Entries
            .Where(e => e.State != ExportRegistryState.Contributes && string.IsNullOrWhiteSpace(e.Reason))
            .ToList();
        Assert.Empty(unreasoned);
    }

    [Fact]
    public void ExportRegistryJsonFile_MatchesTheCompiledUnion()
    {
        // The committed backend/domain-core/export-registry.json (emitted by ExportRegistryEmitter,
        // build/scripts/emit-export-registry.sh's drift gate) must always equal the compiled registry —
        // proven here as a fast, DB-free unit test rather than only at CI-script time.
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "backend", "domain-core", "export-registry.json");
        Assert.True(File.Exists(path), $"{path} does not exist — run build/scripts/emit-export-registry.sh");

        var committed = System.Text.Json.JsonSerializer.Deserialize<List<EmittedEntry>>(File.ReadAllText(path), CaseInsensitiveJson)!;
        var compiled = BuildRealExportRegistry().Entries
            .OrderBy(e => e.StoreKey, StringComparer.Ordinal)
            .Select(e => new EmittedEntry(e.StoreKey, e.State.ToString(), e.Reason))
            .ToList();

        Assert.Equal(compiled.Count, committed.Count);
        for (var i = 0; i < compiled.Count; i++)
        {
            Assert.Equal(compiled[i].StoreKey, committed.Single(c => c.StoreKey == compiled[i].StoreKey).StoreKey);
            var match = committed.Single(c => c.StoreKey == compiled[i].StoreKey);
            Assert.Equal(compiled[i].State, match.State);
            Assert.Equal(compiled[i].Reason, match.Reason);
        }
    }

    private sealed record EmittedEntry(string StoreKey, string State, string Reason);

    private static List<string> FindMissingExportEntries(IEnumerable<string> purgeStoreKeys, IReadOnlySet<string> exportStoreKeys) =>
        purgeStoreKeys.Where(k => !exportStoreKeys.Contains(k)).ToList();

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }
}
