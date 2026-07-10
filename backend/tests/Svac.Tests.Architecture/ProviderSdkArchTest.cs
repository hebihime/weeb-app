using Svac.DomainCore.Contracts;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// 15A router chokepoint (SLICE_S1_CONTRACT.md §8): "arch rule 'no provider SDK outside
/// Svac.AimlRouter' lands now — vacuously green, arms S2 the moment it exists." Svac.AimlRouter does
/// not exist at S1 (§9: "not read"); this test proves the CURRENT backend has zero provider-SDK
/// references anywhere, so the rule is real today, not deferred prose.
/// </summary>
public sealed class ProviderSdkArchTest
{
    private static readonly string[] ForbiddenProviderSdkNamePatterns =
    {
        "Anthropic", "OpenAI", "Azure.AI", "Google.Cloud.AIPlatform", "Cohere", "Mistral",
    };

    [Fact]
    public void NoBackendAssembly_ReferencesAProviderSdkDirectly()
    {
        var assembliesToScan = new[]
        {
            typeof(RequestContext).Assembly, // Svac.DomainCore.Contracts
            typeof(Svac.DomainCore.Deterministic.Ulid).Assembly,
            typeof(Svac.DomainCore.Persistence.CoreDbContext).Assembly, // Svac.DomainCore
            typeof(Svac.DomainCore.Hosting.PolicyResults).Assembly,
        };

        var violations = new List<string>();
        foreach (var assembly in assembliesToScan)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (ForbiddenProviderSdkNamePatterns.Any(p => reference.Name?.Contains(p, StringComparison.OrdinalIgnoreCase) == true))
                {
                    violations.Add($"{assembly.GetName().Name} -> {reference.Name}");
                }
            }
        }

        Assert.Empty(violations);
    }
}
