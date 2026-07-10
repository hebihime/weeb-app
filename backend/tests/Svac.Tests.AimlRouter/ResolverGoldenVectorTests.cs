using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Routing;
using Svac.DomainCore.Contracts;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// A first, representative slice of the Resolver's golden vectors (SLICE_S2_CONTRACT.md §1b/§10.2: pure,
/// IO-free, arch-tested). The full vector set (region variants, failover-order proofs, non-Claude-default
/// bounds rejection) is Build-phase/hardened-gate material; this Scaffold-phase file proves the shape —
/// allowlist ∩ registered, privacy-floor skip, explicit-pin verbatim, empty-chain fail-closed — compiles
/// and behaves correctly today.
/// </summary>
public sealed class ResolverGoldenVectorTests
{
    private static readonly ProviderAllowlistEntry AnthropicEntry = new(
        Name: "anthropic",
        Family: "claude",
        Kinds: new[] { "llm" },
        PayloadClassCeiling: PayloadClass.Pseudonymous,
        DpaSigned: false,
        SpecialCategoryOk: false,
        Residency: "global",
        Models: new[] { "claude-opus-4-8" });

    private static readonly RoutingPolicy DefaultPolicy = new(
        Version: 1,
        DefaultChain: new[] { new TaskChainLink("anthropic", "claude-opus-4-8") },
        TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
        ResidencyOverrides: Array.Empty<string>());

    [Fact]
    public void Resolve_AutomaticPath_DefaultChain_ProducesOneHop()
    {
        var chain = Resolver.Resolve(
            DefaultPolicy, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" },
            AimlTaskKind.Generate, PayloadClass.NonPersonal, RegionCode.Unknown);

        Assert.False(chain.IsEmpty);
        Assert.Equal(new ResolvedHop("anthropic", "claude-opus-4-8"), Assert.Single(chain.Hops));
    }

    [Fact]
    public void Resolve_PrivacyFloorExceeded_SkipsTheHop_NeverTriesIt()
    {
        // Personal > the allowlist entry's Pseudonymous ceiling — §1b: "availability never buys a
        // privacy downgrade" — the hop is skipped, and with only one candidate the chain resolves empty.
        var chain = Resolver.Resolve(
            DefaultPolicy, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" },
            AimlTaskKind.Generate, PayloadClass.Personal, RegionCode.Unknown);

        Assert.True(chain.IsEmpty);
    }

    [Fact]
    public void Resolve_ProviderNotDiRegistered_SkipsTheHop_EmptyChainFailsClosed()
    {
        var chain = Resolver.Resolve(
            DefaultPolicy, new[] { AnthropicEntry }, registeredProviderIds: new HashSet<string>(), // nothing registered
            AimlTaskKind.Generate, PayloadClass.NonPersonal, RegionCode.Unknown);

        Assert.True(chain.IsEmpty); // NoRouteConfigured signal.
    }

    [Fact]
    public void Resolve_ModelUndeclaredForProvider_SkipsTheHop()
    {
        var policyWithUndeclaredModel = DefaultPolicy with
        {
            DefaultChain = new[] { new TaskChainLink("anthropic", "some-future-model") },
        };

        var chain = Resolver.Resolve(
            policyWithUndeclaredModel, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" },
            AimlTaskKind.Generate, PayloadClass.NonPersonal, RegionCode.Unknown);

        Assert.True(chain.IsEmpty);
    }

    [Fact]
    public void ResolveExplicitPin_ClearsAllowlistAndCeiling_HonoredVerbatim()
    {
        var pin = new ProviderPin("anthropic", "claude-opus-4-8");
        var chain = Resolver.ResolveExplicitPin(pin, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" }, PayloadClass.NonPersonal);

        Assert.False(chain.IsEmpty);
        Assert.Equal("anthropic", chain.Hops[0].Provider);
        Assert.Equal("claude-opus-4-8", chain.Hops[0].Model);
    }

    [Fact]
    public void ResolveExplicitPin_NotAllowlisted_RefusedNeverSilentlyRerouted()
    {
        var pin = new ProviderPin("some-other-vendor", "some-model");
        var chain = Resolver.ResolveExplicitPin(pin, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" }, PayloadClass.NonPersonal);

        Assert.True(chain.IsEmpty); // a typed, audited refusal at the caller — never a reroute to a different provider.
    }

    [Fact]
    public void ResolveExplicitPin_AboveCeiling_Refused()
    {
        var pin = new ProviderPin("anthropic", "claude-opus-4-8");
        var chain = Resolver.ResolveExplicitPin(pin, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" }, PayloadClass.SpecialCategory);

        Assert.True(chain.IsEmpty);
    }

    [Fact]
    public void Resolve_TaskSpecificChain_OverridesDefaultChain_WhenPresent()
    {
        var otherModelEntry = AnthropicEntry with { Models = new[] { "claude-opus-4-8", "claude-haiku-4-5" } };
        var policy = DefaultPolicy with
        {
            TaskChains = new Dictionary<string, IReadOnlyList<TaskChainLink>>
            {
                [AimlTaskKind.ModerateText.ToString()] = new[] { new TaskChainLink("anthropic", "claude-haiku-4-5") },
            },
        };

        var chain = Resolver.Resolve(
            policy, new[] { otherModelEntry }, new HashSet<string> { "anthropic" },
            AimlTaskKind.ModerateText, PayloadClass.NonPersonal, RegionCode.Unknown);

        Assert.Equal("claude-haiku-4-5", Assert.Single(chain.Hops).Model);
    }
}
