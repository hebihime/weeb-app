using Svac.DomainCore.Contracts;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// 15A router chokepoint (SLICE_S1_CONTRACT.md §8, graduated to ARMED at SLICE_S2_CONTRACT.md §0/§8/
/// §10.1 — S2's one named strengthening pass on this file): "provider-SDK references legal ONLY in
/// Svac.AimlRouter (internal, never Contracts); red-fixture proven both directions (SDK ref outside the
/// module fails; the same ref inside passes)."
///
/// This pass proves the NON-VACUOUS half now that <c>Svac.AimlRouter</c> exists and really does carry
/// the Anthropic.SDK reference (<see cref="AllowlistedAssembly_ActuallyReferencesTheProviderSdk_TheRuleIsNotVacuous"/>)
/// — S1's version could only prove absence because nothing existed yet to prove presence against. The
/// full adversarial red-fixture (a deliberately-violating compiled assembly asserted to fail this exact
/// scan) is Security-phase material per SLICE_PLAYBOOK.md's phase split; this pass's job is arming the
/// rule against the real module tree, not authoring that fixture.
/// </summary>
public sealed class ProviderSdkArchTest
{
    private static readonly string[] ForbiddenProviderSdkNamePatterns =
    {
        "Anthropic", "OpenAI", "Azure.AI", "Google.Cloud.AIPlatform", "Cohere", "Mistral",
    };

    /// <summary>Svac.AimlRouter — SLICE_S2_CONTRACT.md §1a: "The single assembly allowlisted by the provider-SDK arch test." Nowhere else in this file's scan lists may this name appear.</summary>
    private const string AllowlistedAssemblyName = "Svac.AimlRouter";

    [Fact]
    public void NoBackendAssembly_ReferencesAProviderSdkDirectly_ExceptTheOneAllowlistedModule()
    {
        var assembliesToScan = new[]
        {
            typeof(RequestContext).Assembly, // Svac.DomainCore.Contracts
            typeof(Svac.DomainCore.Deterministic.Ulid).Assembly,
            typeof(Svac.DomainCore.Persistence.CoreDbContext).Assembly, // Svac.DomainCore
            typeof(Svac.DomainCore.Hosting.PolicyResults).Assembly,
            typeof(Svac.PublicApi.ClientConfigLoader).Assembly, // Svac.PublicApi — the one real host today
            typeof(Svac.AimlRouter.Contracts.IAimlRouter).Assembly, // Svac.AimlRouter.Contracts — SDK-free forever (§1a), the ONLY assembly consumers may reference
        };

        Assert.DoesNotContain(AllowlistedAssemblyName, assembliesToScan.Select(a => a.GetName().Name));

        var violations = FindProviderSdkReferences(assembliesToScan);

        Assert.Empty(violations);
    }

    [Fact]
    public void AllowlistedAssembly_ActuallyReferencesTheProviderSdk_TheRuleIsNotVacuous()
    {
        // "the same ref inside Svac.AimlRouter passes" only means something if AimlRouter really
        // carries the reference — this is the non-vacuous half S1's version could not prove because
        // Svac.AimlRouter did not exist yet (SLICE_S1_CONTRACT.md §9: "not read").
        var aimlRouterAssembly = typeof(Svac.AimlRouter.Providers.AnthropicApiKeyGuard).Assembly;

        Assert.Equal(AllowlistedAssemblyName, aimlRouterAssembly.GetName().Name);
        Assert.NotEmpty(FindProviderSdkReferences(new[] { aimlRouterAssembly }));
    }

    [Fact]
    public void AllowlistedAssembly_IsTheOnlyAssemblyEverAllowedToCarryTheReference()
    {
        // Belt-and-suspenders on the two tests above: restates the law as one sentence a future edit
        // to either scan list cannot silently weaken — "provider-SDK references legal ONLY in
        // Svac.AimlRouter" means exactly one name may ever appear on the allow side.
        Assert.Equal("Svac.AimlRouter", AllowlistedAssemblyName);
    }

    private static List<string> FindProviderSdkReferences(IReadOnlyList<System.Reflection.Assembly> assemblies)
    {
        var violations = new List<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (ForbiddenProviderSdkNamePatterns.Any(p => reference.Name?.Contains(p, StringComparison.OrdinalIgnoreCase) == true))
                {
                    violations.Add($"{assembly.GetName().Name} -> {reference.Name}");
                }
            }
        }
        return violations;
    }
}
