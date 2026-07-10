using Svac.AimlRouter;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Svac.AimlRouter.Routing;
using Svac.AimlRouter.Security;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// The Scaffold-phase "trivial container test" (SLICE_PLAYBOOK.md Phase 1 gate): wires
/// <see cref="AimlRouterService"/> end to end against <see cref="SeedProvider"/> + in-memory fakes —
/// deterministic, &lt;2s, zero network, zero Postgres. Exercises the real production code path
/// (quota → egress authorization → resolve → provider dispatch → audit append), just against fakes for
/// every domain-core dependency, exactly as a future consuming module's own tests will.
/// </summary>
public sealed class AimlRouterServiceTests
{
    private static readonly ProviderAllowlistEntry AnthropicEntry = new(
        Name: "anthropic", Family: "claude", Kinds: new[] { "llm" },
        PayloadClassCeiling: PayloadClass.Pseudonymous, DpaSigned: false, SpecialCategoryOk: false,
        Residency: "global", Models: new[] { "claude-opus-4-8" });

    private static readonly RoutingPolicy DefaultPolicy = new(
        Version: 7,
        DefaultChain: new[] { new TaskChainLink("seed", "seed-v0") },
        TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
        ResidencyOverrides: Array.Empty<ResidencyOverride>());

    private static readonly string[] SeedModels = { "seed-v0" };

    private static readonly IReadOnlyList<ProviderAllowlistEntry> SeedAllowlist =
        new[] { AnthropicEntry with { Name = "seed", Models = SeedModels } };

    private static FakeConfigRegistry MakeConfig() => new FakeConfigRegistry()
        .With("aiml.provider_allowlist", SeedAllowlist)
        .With("aiml.routing_policy", DefaultPolicy)
        .With("aiml.invoke_timeout_seconds", 5);

    private static RequestContext SystemCtx() => RequestContext.System(
        ActorRef.System(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared)), "test");

    private static AimlRequest MakeRequest(PayloadClass payloadClass = PayloadClass.NonPersonal) => new(
        Task: AimlTaskKind.EvalProbe, Caller: CallerModule.System, PayloadClass: payloadClass,
        Subject: null, Payload: AimlPayload.ForUserTurn("ping"), TargetLocale: null, ExplicitPin: null);

    [Fact]
    public async Task InvokeAsync_HappyPath_ReturnsSuccessAndAppendsOneAuditEvent()
    {
        var eventStore = new FakeEventStore();
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new SeedProvider() },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService(),
            eventStore: eventStore);

        var result = await router.InvokeAsync(MakeRequest(), SystemCtx());

        var success = Assert.IsType<AimlResult.Success>(result);
        Assert.Equal("seed-echo:ping", success.Output.OutputText);
        Assert.Equal("seed", success.Receipt.Provider);
        Assert.Equal(DecisionSource.Policy, success.Receipt.DecisionSource);

        var appended = Assert.Single(eventStore.Appended);
        Assert.Equal(StreamType.Audit, appended.Stream);
        Assert.Equal("aiml.route_decided", appended.EventType);
        Assert.NotNull(appended.PayloadJson);
        Assert.DoesNotContain("ping", appended.PayloadJson!); // metadata only — never prompt/completion text.
    }

    [Fact]
    public async Task InvokeAsync_SpecialCategoryPayload_RefusedByEgressAuthorizer_BeforeAnyProviderCall()
    {
        var providerCalled = false;
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new RecordingSeedProvider(() => providerCalled = true) },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService(),
            eventStore: new FakeEventStore());

        var result = await router.InvokeAsync(MakeRequest(PayloadClass.SpecialCategory), SystemCtx());

        var failure = Assert.IsType<AimlResult.Failure>(result);
        Assert.Equal(AimlFailure.RefusedPrivacyFloor, failure.Cause);
        Assert.False(providerCalled, "the refuse-all-special-category lock must fire BEFORE any provider is ever reached.");
    }

    [Fact]
    public async Task InvokeAsync_BudgetDenied_ReturnsFailure_NeverThrows()
    {
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new SeedProvider() },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService { AlwaysLimited = true },
            eventStore: new FakeEventStore());

        var result = await router.InvokeAsync(MakeRequest(), SystemCtx());

        Assert.Equal(AimlFailure.BudgetDenied, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    [Fact]
    public async Task InvokeAsync_NoRouteConfigured_WhenNothingIsDiRegisteredForTheTask()
    {
        var router = new AimlRouterService(
            providers: Array.Empty<IModelProvider>(), // nothing registered -> Resolve's intersection is empty.
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService(),
            eventStore: new FakeEventStore());

        var result = await router.InvokeAsync(MakeRequest(), SystemCtx());

        Assert.Equal(AimlFailure.NoRouteConfigured, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    [Fact]
    public async Task InvokeAsync_ExplicitPin_NotAllowlisted_TypedRefusal_NeverSilentReroute()
    {
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new SeedProvider() },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService(),
            eventStore: new FakeEventStore());

        var pinnedRequest = MakeRequest() with { ExplicitPin = new ProviderPin("not-on-allowlist", "some-model") };
        var result = await router.InvokeAsync(pinnedRequest, SystemCtx());

        Assert.Equal(AimlFailure.NotAllowlisted, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    [Fact]
    public async Task InvokeAsync_AutomaticPath_PayloadClassAboveCeiling_RefusedPrivacyFloor_NeverNoRouteConfigured()
    {
        // SLICE_S2_CONTRACT.md §10.3 FINDING 2 (backend/e2e/aiml-router.e2e.mjs's own header): the
        // contract's literal drill text is "Personal payload vs pseudonymous ceiling => RefusedPrivacyFloor
        // on the event". Proven here at gate speed (fake providers, no live CLI, no Postgres): the "seed"
        // allowlist entry's ceiling is Pseudonymous, so a Personal-class request is a genuine candidate
        // that privacy law blocks, not an unconfigured route.
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new SeedProvider() },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService(),
            eventStore: new FakeEventStore());

        var result = await router.InvokeAsync(MakeRequest(PayloadClass.Personal), SystemCtx());

        Assert.Equal(AimlFailure.RefusedPrivacyFloor, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    [Fact]
    public async Task InvokeAsync_ExplicitPin_PayloadClassAboveCeiling_RefusedPrivacyFloor_NeverNotAllowlisted()
    {
        // Mirrors the Automatic-path fix above for the Explicit-pin path (§1b: "the pin bypasses the
        // routing policy, never the laws" — the privacy floor is one of those laws, distinct from
        // allowlist membership).
        var router = new AimlRouterService(
            providers: new IModelProvider[] { new SeedProvider() },
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: MakeConfig(),
            quotaService: new FakeQuotaService(),
            eventStore: new FakeEventStore());

        var pinnedRequest = MakeRequest(PayloadClass.Personal) with { ExplicitPin = new ProviderPin("seed", "seed-v0") };
        var result = await router.InvokeAsync(pinnedRequest, SystemCtx());

        Assert.Equal(AimlFailure.RefusedPrivacyFloor, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    /// <summary>A SeedProvider variant that reports whether it was ever invoked, for the egress-refusal ordering proof above.</summary>
    private sealed class RecordingSeedProvider(Action onExecute) : IModelProvider
    {
        private readonly SeedProvider _inner = new();
        public ProviderDescriptor Descriptor => _inner.Descriptor;

        public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct)
        {
            onExecute();
            return _inner.ExecuteAsync(invocation, ct);
        }
    }
}
