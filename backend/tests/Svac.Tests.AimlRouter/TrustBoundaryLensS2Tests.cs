using Svac.AimlRouter;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Svac.AimlRouter.Routing;
using Svac.AimlRouter.Security;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// ADVERSARIAL trust-boundary lens, S2 pass — the internals-visible half (the Svac.Tests.Architecture
/// sibling file TrustBoundaryLensS2Tests carries the config-door/guard/DTO-scan breaks; this file
/// carries the ones that need InternalsVisibleTo: Resolver + AimlRouterService). Every [Fact] asserts
/// the SECURE behavior SLICE_S2_CONTRACT.md promises and FAILS against the shipped S2 code.
///
/// Run just this file:
///   dotnet test backend/tests/Svac.Tests.AimlRouter \
///     --filter "FullyQualifiedName~TrustBoundaryLensS2"
/// </summary>
public sealed class TrustBoundaryLensS2Tests
{
    private static readonly ProviderAllowlistEntry AnthropicEntry = new(
        Name: "anthropic", Family: "claude", Kinds: new[] { "llm" },
        PayloadClassCeiling: PayloadClass.Pseudonymous, DpaSigned: false, SpecialCategoryOk: false,
        Residency: "global", Models: new[] { "claude-opus-4-8" });

    private static RequestContext SystemCtx() => RequestContext.System(
        ActorRef.System(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared)), "trust-lens");

    // -----------------------------------------------------------------------------------------------
    // BREAK 5 — TASK-KIND DOOR FAILS OPEN: an EMPTY task chain falls back to the default chain.
    //
    // SLICE_S2_CONTRACT.md, AimlTaskKind's own contract text (§1b): "A kind with no policy chain fails
    // closed (NoRouteConfigured)." §9 (consumers row): "empty policy chains keep unbuilt kinds
    // fail-closed; each consumer's activation = adapter/policy data." Resolver.ChainFor
    // (Resolver.cs:96-99) codes the OPPOSITE: `taskChain.Count > 0 ? taskChain : policy.DefaultChain` —
    // an explicitly-empty task chain (the natural per-kind kill switch an ops actor would reach for,
    // e.g. to cut off ModerateText spend during an incident) is silently overridden by the default
    // chain. Consequence at v0: with task_chains == {} every kind (Generate, ModerateText, Translate)
    // is ALREADY live through the default chain — consumer "activation as policy data" never gates
    // anything, and no policy edit short of emptying default_chain for everyone can close one kind.
    //
    // Inputs -> wrong result: policy{task_chains: {"ModerateText": []}} + Resolve(ModerateText) ->
    // 1 hop (anthropic/claude-opus-4-8) where the contract demands an empty chain (NoRouteConfigured).
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public void Resolve_ExplicitlyEmptyTaskChain_FailsClosed_NeverFallsOpenToDefaultChain()
    {
        var policy = new RoutingPolicy(
            Version: 3,
            DefaultChain: new[] { new TaskChainLink("anthropic", "claude-opus-4-8") },
            TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>
            {
                ["ModerateText"] = Array.Empty<TaskChainLink>(), // the ops kill switch for ONE kind
            },
            ResidencyOverrides: Array.Empty<ResidencyOverride>());

        var chain = Resolver.Resolve(
            policy, new[] { AnthropicEntry }, new HashSet<string> { "anthropic" },
            AimlTaskKind.ModerateText, PayloadClass.NonPersonal, RegionCode.Unknown);

        Assert.True(chain.IsEmpty,
            "an explicitly-empty task chain must fail closed (§1b: 'a kind with no policy chain fails " +
            "closed'); instead ChainFor falls open to the default chain, so a per-kind kill switch is " +
            "structurally impossible and every pre-declared kind is live the moment default_chain is.");
        // FAILS today: chain has one hop — the default chain served a kind whose own chain was emptied.
    }

    // -----------------------------------------------------------------------------------------------
    // BREAK 6 — TIMEOUT IS AUDITED AS ChainExhausted: AimlFailure.Timeout is unreachable code.
    //
    // SLICE_S2_CONTRACT.md §4: `aiml.invoke_timeout_seconds` "feeds the Timeout failure kind."
    // AnthropicLocalTransport.cs's own catch block says "AimlRouterService maps this exception type to
    // AimlFailure.Timeout." No such mapping exists: AimlRouterService.InvokeAsync's chain walk
    // (AimlRouterService.cs:128-131) catches EVERY exception (except OOM) identically as a failed hop,
    // so a single-hop chain that times out returns ChainExhausted and the audit event records
    // outcome=ChainExhausted. The 3A decision record — the thing S5's desk tiles and incident review
    // read — misclassifies every timeout as total chain exhaustion, and the closed failure union's
    // Timeout member is dead code the day it shipped.
    //
    // Inputs -> wrong result: one-hop chain, provider throws TimeoutException -> Failure(ChainExhausted)
    // + audit outcome "ChainExhausted", where the contract's failure kind for this cause is Timeout.
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public async Task InvokeAsync_ProviderTimeout_SurfacesAsTimeout_NeverMisauditedAsChainExhausted()
    {
        var eventStore = new FakeEventStore();
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new TimingOutProvider() },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: new FakeConfigRegistry()
                .With("aiml.provider_allowlist", (IReadOnlyList<ProviderAllowlistEntry>)new[] { AnthropicEntry })
                .With("aiml.routing_policy", new RoutingPolicy(
                    Version: 1,
                    DefaultChain: new[] { new TaskChainLink("anthropic", "claude-opus-4-8") },
                    TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
                    ResidencyOverrides: Array.Empty<ResidencyOverride>()))
                .With("aiml.invoke_timeout_seconds", 5),
            quotaService: new FakeQuotaService(),
            eventStore: eventStore);

        var request = new AimlRequest(
            Task: AimlTaskKind.EvalProbe, Caller: CallerModule.System, PayloadClass: PayloadClass.NonPersonal,
            Subject: null, Payload: AimlPayload.ForUserTurn("ping"), TargetLocale: null, ExplicitPin: null);

        var result = await router.InvokeAsync(request, SystemCtx());

        var failure = Assert.IsType<AimlResult.Failure>(result);
        Assert.Equal(AimlFailure.Timeout, failure.Cause);
        // FAILS today: Cause == ChainExhausted — the catch-all hop handler erases the timeout, the audit
        // stream records the wrong outcome, and AimlFailure.Timeout has no producing code path.
    }

    // -----------------------------------------------------------------------------------------------
    // BREAK 7 — TargetLocale IS NEVER VALIDATED: AimlFailure.InvalidRequest is unreachable code.
    //
    // SLICE_S2_CONTRACT.md §1b: TargetLocale "must ∈ i18n/locales.json ×4". §8 (i18n seam row):
    // "TargetLocale validated ∈ i18n/locales.json ×4" — stated as a MADE-CONCRETE seam, not future
    // work. AimlRouterService.InvokeAsync never reads req.TargetLocale (grep the module: the property
    // is consumed nowhere), so a Translate call carrying a locale outside the four shipped ones routes
    // straight through to the provider. DTO-trust absence at the module boundary: the router forwards
    // an unvalidated consumer-supplied field across the trust boundary toward the vendor, and the
    // closed failure union's InvalidRequest member has no producing code path.
    //
    // Inputs -> wrong result: Translate + TargetLocale "xx-XX" -> Success (provider invoked) where the
    // contract demands the typed refusal InvalidRequest before any egress.
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public async Task InvokeAsync_TranslateWithLocaleOutsideTheShippedFour_IsInvalidRequest_NeverRouted()
    {
        var providerCalled = false;
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new RecordingProvider(() => providerCalled = true) },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: new FakeConfigRegistry()
                .With("aiml.provider_allowlist", (IReadOnlyList<ProviderAllowlistEntry>)new[] { AnthropicEntry })
                .With("aiml.routing_policy", new RoutingPolicy(
                    Version: 1,
                    DefaultChain: new[] { new TaskChainLink("anthropic", "claude-opus-4-8") },
                    TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
                    ResidencyOverrides: Array.Empty<ResidencyOverride>()))
                .With("aiml.invoke_timeout_seconds", 5),
            quotaService: new FakeQuotaService(),
            eventStore: new FakeEventStore());

        var request = new AimlRequest(
            Task: AimlTaskKind.Translate, Caller: CallerModule.Conversations, PayloadClass: PayloadClass.NonPersonal,
            Subject: null, Payload: AimlPayload.ForUserTurn("bonjour"), TargetLocale: "xx-XX", ExplicitPin: null);

        var result = await router.InvokeAsync(request, SystemCtx());

        var failure = Assert.IsType<AimlResult.Failure>(result);
        Assert.Equal(AimlFailure.InvalidRequest, failure.Cause);
        Assert.False(providerCalled, "an invalid TargetLocale must be refused BEFORE any vendor egress.");
        // FAILS today: the call SUCCEEDS — TargetLocale is read by nothing, the provider runs, and
        // InvalidRequest is a dead enum member.
    }

    /// <summary>A hop that times out — same descriptor shape as the real anthropic adapters.</summary>
    private sealed class TimingOutProvider : IModelProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            ProviderId: "anthropic",
            Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
            Transport: ProviderTransport.Api,
            CredentialRequirement: false);

        public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct) =>
            throw new TimeoutException($"provider exceeded its {invocation.Timeout} timeout.");
    }

    /// <summary>Records whether egress actually happened; echoes deterministically otherwise.</summary>
    private sealed class RecordingProvider(Action onExecute) : IModelProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            ProviderId: "anthropic",
            Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
            Transport: ProviderTransport.LocalProcess,
            CredentialRequirement: false);

        public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct)
        {
            onExecute();
            return Task.FromResult(new ProviderExecutionResult(AimlPayload.ForOutput("echo"), 1, 1));
        }
    }
}
