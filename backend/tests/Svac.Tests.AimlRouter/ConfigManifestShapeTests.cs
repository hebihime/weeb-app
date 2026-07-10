using System.Text.Json;
using Svac.AimlRouter.Routing;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// Structural proof of the SLICE_S2_CONTRACT.md §4 manifest, WITHOUT touching Postgres/ConfigSeedLoader
/// (gate-lane discipline: deterministic, &lt;2s). Mirrors the dead-tunable-lint shape
/// (ConfigSeedLoader.SeedFromFile's own "no consumer" throw) by asserting the same invariant directly
/// off the raw file, and locks in Correction 2 (§13 ratification): the v0 default model is
/// claude-opus-4-8 in BOTH places, never a smaller/cheaper model.
/// </summary>
public sealed class ConfigManifestShapeTests
{
    private static JsonElement LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "aiml-router.config.json");
        Assert.True(File.Exists(path), $"expected the 9A manifest at {path} (copied via the test project's own Content item).");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void Manifest_DeclaresExactlyFourEntries_PerContractSection4()
    {
        var entries = LoadManifest().GetProperty("entries");
        Assert.Equal(4, entries.GetArrayLength());
    }

    [Fact]
    public void Manifest_EveryEntry_HasANonEmptyConsumer_DeadTunableLintParity()
    {
        foreach (var entry in LoadManifest().GetProperty("entries").EnumerateArray())
        {
            var consumer = entry.GetProperty("consumer").GetString();
            Assert.False(string.IsNullOrWhiteSpace(consumer), $"key \"{entry.GetProperty("key").GetString()}\" has no consumer — ConfigSeedLoader.SeedFromFile would throw on this at boot.");
        }
    }

    [Fact]
    public void Manifest_EveryEntry_HasAValidScope()
    {
        var validScopes = new HashSet<string> { "founder", "ops", "set" };
        foreach (var entry in LoadManifest().GetProperty("entries").EnumerateArray())
        {
            var scope = entry.GetProperty("scope").GetString();
            Assert.NotNull(scope);
            Assert.Contains(scope!, validScopes);
        }
    }

    [Fact]
    public void ProviderAllowlist_DeserializesToTypedShape_AndDefaultModelIsBestAvailableClaude()
    {
        var raw = LoadManifest().GetProperty("entries")
            .EnumerateArray().First(e => e.GetProperty("key").GetString() == "aiml.provider_allowlist")
            .GetProperty("value").GetRawText();

        var allowlist = JsonSerializer.Deserialize<IReadOnlyList<ProviderAllowlistEntry>>(raw)!;
        var anthropic = Assert.Single(allowlist);

        Assert.Equal("anthropic", anthropic.Name);
        Assert.Equal("claude", anthropic.Family);
        Assert.False(anthropic.DpaSigned); // OQ-A ratified conservative posture.
        Assert.False(anthropic.SpecialCategoryOk);
        // Correction 1 (§13 ratification): "the v0 default model is the best-available Claude, not
        // claude-fable-5" — CLAUDE.md's "no silent downgrades to a cheaper or smaller model for cost."
        Assert.Contains("claude-opus-4-8", anthropic.Models);
    }

    [Fact]
    public void RoutingPolicy_DeserializesToTypedShape_AndDefaultChainModelIsBestAvailableClaude()
    {
        var raw = LoadManifest().GetProperty("entries")
            .EnumerateArray().First(e => e.GetProperty("key").GetString() == "aiml.routing_policy")
            .GetProperty("value").GetRawText();

        var policy = JsonSerializer.Deserialize<RoutingPolicy>(raw)!;
        var firstHop = Assert.Single(policy.DefaultChain);

        Assert.Equal("anthropic", firstHop.Provider);
        Assert.Equal("claude-opus-4-8", firstHop.Model); // Correction 1, verbatim.
        Assert.Empty(policy.ResidencyOverrides); // "empty v0" per §1b.
    }

    [Fact]
    public void RoutingPolicy_DefaultChainModel_IsDeclaredInTheAllowlistModelsList()
    {
        // §4: "The allowlist models list must contain whatever default_chain[0].model names, or
        // set-time bounds must reject the policy — keep them consistent." Both live in this same
        // manifest file, so a gate test can prove consistency without a running ConfigRegistry.
        var manifest = LoadManifest().GetProperty("entries");
        var allowlist = JsonSerializer.Deserialize<IReadOnlyList<ProviderAllowlistEntry>>(
            manifest.EnumerateArray().First(e => e.GetProperty("key").GetString() == "aiml.provider_allowlist").GetProperty("value").GetRawText())!;
        var policy = JsonSerializer.Deserialize<RoutingPolicy>(
            manifest.EnumerateArray().First(e => e.GetProperty("key").GetString() == "aiml.routing_policy").GetProperty("value").GetRawText())!;

        var defaultModel = policy.DefaultChain[0].Model;
        var declaringEntry = allowlist.First(a => a.Name == policy.DefaultChain[0].Provider);
        Assert.Contains(defaultModel, declaringEntry.Models);
    }
}
