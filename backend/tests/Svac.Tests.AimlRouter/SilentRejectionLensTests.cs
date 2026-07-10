#pragma warning disable CA1861 // constant array args in per-test builders are intentional; this is a red adversary fixture, not hot-path code.
using System.Threading;
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
/// ADVERSARY LENS: silent-rejection leaks — a deny / void / exclusion / timeout that is UNOBSERVABLE
/// (no distinct typed cause, no distinct audit tell) is a broken slice, because the audit stream is the
/// ONLY observability surface a system-actor router has (SLICE_S2_CONTRACT.md §7: "System actor; no
/// consumer-visible state change"). Each test asserts the CONTRACT-PROMISED behaviour, so each one FAILS
/// against the running AimlRouterService — it is a red proof of a specific leak, not a regression guard.
///
/// The leaks proven here all live in AimlRouterService.InvokeAsync's failover loop
/// (AimlRouterService.cs:99-135), whose blanket `catch (Exception ex) when (ex is not OutOfMemoryException)`
/// collapses every distinguishable rejection reason into the single opaque ChainExhausted terminal, and
/// whose single `failoverFrom` local (line 130) is overwritten each hop so only the LAST attempt leaves a
/// tell. AimlFailure.Timeout, AimlFailure.ProviderError and AimlFailure.InvalidRequest are declared in the
/// contract union (AimlFailure.cs) but constructed by ZERO code paths.
/// </summary>
public sealed class SilentRejectionLensTests
{
    private static ProviderAllowlistEntry Entry(string name, params string[] models) => new(
        Name: name, Family: "claude", Kinds: new[] { "llm" },
        PayloadClassCeiling: PayloadClass.Pseudonymous, DpaSigned: false, SpecialCategoryOk: false,
        Residency: "global", Models: models);

    private static RoutingPolicy PolicyOver(params (string provider, string model)[] chain) => new(
        Version: 7,
        DefaultChain: chain.Select(c => new TaskChainLink(c.provider, c.model)).ToArray(),
        TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
        ResidencyOverrides: Array.Empty<ResidencyOverride>());

    private static FakeConfigRegistry Config(RoutingPolicy policy, params ProviderAllowlistEntry[] allowlist) =>
        new FakeConfigRegistry()
            .With("aiml.provider_allowlist", (IReadOnlyList<ProviderAllowlistEntry>)allowlist)
            .With("aiml.routing_policy", policy)
            .With("aiml.invoke_timeout_seconds", 5);

    private static RequestContext SystemCtx() => RequestContext.System(
        ActorRef.System(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared)), "test");

    private static AimlRequest EvalProbe() => new(
        Task: AimlTaskKind.EvalProbe, Caller: CallerModule.System, PayloadClass: PayloadClass.NonPersonal,
        Subject: null, Payload: AimlPayload.ForUserTurn("ping"), TargetLocale: null, ExplicitPin: null);

    private static AimlRouterService Router(FakeConfigRegistry config, FakeEventStore store, params IModelProvider[] providers) =>
        new AimlRouterService(
            providers: providers,
            egressAuthorizer: new RefuseAllSpecialCategoryAuthorizer(),
            configRegistry: config,
            quotaService: new FakeQuotaService(),
            eventStore: store);

    // ── FINDING 1 ────────────────────────────────────────────────────────────────────────────────
    // A provider TIMEOUT is unobservable. AnthropicApiTransport.cs:74-79 correctly surfaces a timeout as
    // TimeoutException, but the router's blanket catch (AimlRouterService.cs:128) swallows it into the
    // generic failover path. On the v0 single-hop chain the caller receives ChainExhausted — never
    // AimlFailure.Timeout — and the audit outcome reads "ChainExhausted". A timeout is indistinguishable
    // from any other hop failure. AimlFailure.Timeout is dead; aiml.invoke_timeout_seconds (§4, "feeds
    // the Timeout failure kind") is decorative. EXPECTED: Timeout. ACTUAL: ChainExhausted.
    [Fact]
    public async Task Timeout_IsSurfacedAsTimeout_NotSilentlyReclassifiedAsChainExhausted()
    {
        var store = new FakeEventStore();
        var router = Router(
            Config(PolicyOver(("anthropic", "claude-opus-4-8")), Entry("anthropic", "claude-opus-4-8")),
            store,
            new ThrowingProvider("anthropic", new TimeoutException("provider exceeded its timeout")));

        var result = await router.InvokeAsync(EvalProbe(), SystemCtx());

        var failure = Assert.IsType<AimlResult.Failure>(result);
        Assert.Equal(AimlFailure.Timeout, failure.Cause);
        Assert.Contains("Timeout", store.Appended.Single().PayloadJson!);
    }

    // ── FINDING 2 ────────────────────────────────────────────────────────────────────────────────
    // A genuine PROVIDER ERROR is unobservable. On a single-hop chain (there is no "next" to fail over
    // to), a provider that throws should surface AimlFailure.ProviderError. The router instead returns
    // ChainExhausted and stamps the audit outcome "ChainExhausted", so an operator investigating provider
    // health cannot tell an active provider error from a chain that ran out of options. AimlFailure.
    // ProviderError is constructed by zero code paths. EXPECTED: ProviderError. ACTUAL: ChainExhausted.
    [Fact]
    public async Task SingleHopProviderError_IsSurfacedAsProviderError_NotChainExhausted()
    {
        var store = new FakeEventStore();
        var router = Router(
            Config(PolicyOver(("anthropic", "claude-opus-4-8")), Entry("anthropic", "claude-opus-4-8")),
            store,
            new ThrowingProvider("anthropic", new InvalidOperationException("provider 503")));

        var result = await router.InvokeAsync(EvalProbe(), SystemCtx());

        Assert.Equal(AimlFailure.ProviderError, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    // ── FINDING 3 ────────────────────────────────────────────────────────────────────────────────
    // Caller CANCELLATION is swallowed and masqueraded as ChainExhausted. The blanket catch
    // (AimlRouterService.cs:128) catches OperationCanceledException raised off the CALLER's token, sets
    // failoverFrom, and continues the loop — re-invoking every remaining hop under an already-cancelled
    // token — then returns ChainExhausted. Cancellation is neither propagated nor surfaced, and work is
    // re-attempted against a dead token. EXPECTED: OperationCanceledException propagates, only the first
    // hop is touched. ACTUAL: no throw, every hop invoked, ChainExhausted returned.
    [Fact]
    public async Task CallerCancellation_Propagates_AndDoesNotReAttemptRemainingHopsUnderDeadToken()
    {
        var store = new FakeEventStore();
        var invocations = 0;
        var provider = new CancellationObservingProvider("anthropic", () => Interlocked.Increment(ref invocations));
        var router = Router(
            Config(
                PolicyOver(("anthropic", "claude-opus-4-8"), ("anthropic-2", "claude-opus-4-8")),
                Entry("anthropic", "claude-opus-4-8"), Entry("anthropic-2", "claude-opus-4-8")),
            store,
            provider,
            new CancellationObservingProvider("anthropic-2", () => Interlocked.Increment(ref invocations)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.InvokeAsync(EvalProbe(), SystemCtx(), cts.Token));
        Assert.Equal(1, invocations); // second hop must NOT be tried once the token is already cancelled.
    }

    // ── FINDING 4 ────────────────────────────────────────────────────────────────────────────────
    // Intermediate failover-hop rejections are unobservable. `failoverFrom` (AimlRouterService.cs:130) is
    // overwritten every iteration, so the single terminal ChainExhausted event keeps ONLY the LAST failed
    // hop. For a 3-hop chain where every hop fails, the first two attempts leave no tell anywhere on the
    // audit stream — the only observability surface. §1b ("every hop is audited with failover_from") and
    // §10.3 ("both hops audited") are violated regardless of the one-event-per-invoke choice (§12.6): even
    // one event must preserve WHICH hops were tried. EXPECTED: the audit event names the first hop's
    // provider. ACTUAL: only the last hop appears.
    [Fact]
    public async Task ChainExhausted_AuditEvent_PreservesEveryAttemptedHop_NotOnlyTheLast()
    {
        var store = new FakeEventStore();
        var router = Router(
            Config(
                PolicyOver(("prov-alpha", "m"), ("prov-beta", "m"), ("prov-gamma", "m")),
                Entry("prov-alpha", "m"), Entry("prov-beta", "m"), Entry("prov-gamma", "m")),
            store,
            new ThrowingProvider("prov-alpha", new InvalidOperationException("a down")),
            new ThrowingProvider("prov-beta", new InvalidOperationException("b down")),
            new ThrowingProvider("prov-gamma", new InvalidOperationException("c down")));

        var result = await router.InvokeAsync(EvalProbe(), SystemCtx());

        Assert.Equal(AimlFailure.ChainExhausted, Assert.IsType<AimlResult.Failure>(result).Cause);
        var payload = store.Appended.Single().PayloadJson!;
        Assert.Contains("prov-alpha", payload); // the FIRST rejected hop must be observable, not just prov-gamma.
    }

    // ── FINDING 5 ────────────────────────────────────────────────────────────────────────────────
    // An INVALID REQUEST is silently accepted — the deny that should fire never does. The contract (§1b,
    // §8 i18n row) requires TargetLocale ∈ i18n/locales.json for a Translate task; AimlRouterService never
    // reads req.TargetLocale, so a Translate with a bogus locale is not rejected with AimlFailure.
    // InvalidRequest — it flows to a provider and returns Success. AimlFailure.InvalidRequest is
    // constructed by zero code paths. EXPECTED: InvalidRequest refusal. ACTUAL: Success, invalid data
    // egressed to the model with no tell.
    [Fact]
    public async Task TranslateWithInvalidTargetLocale_IsRejectedAsInvalidRequest_NotSilentlyAccepted()
    {
        var store = new FakeEventStore();
        var router = Router(
            Config(PolicyOver(("seed", "seed-v0")), Entry("seed", "seed-v0")),
            store,
            new SeedProvider());

        var badLocale = new AimlRequest(
            Task: AimlTaskKind.Translate, Caller: CallerModule.System, PayloadClass: PayloadClass.NonPersonal,
            Subject: null, Payload: AimlPayload.ForUserTurn("hello"), TargetLocale: "zz-ZZ", ExplicitPin: null);

        var result = await router.InvokeAsync(badLocale, SystemCtx());

        Assert.Equal(AimlFailure.InvalidRequest, Assert.IsType<AimlResult.Failure>(result).Cause);
    }

    /// <summary>A provider that always throws a chosen exception — models a timeout / provider error / down transport.</summary>
    private sealed class ThrowingProvider(string providerId, Exception toThrow) : IModelProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            ProviderId: providerId,
            Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
            Transport: ProviderTransport.Api,
            CredentialRequirement: true);

        public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct) =>
            throw toThrow;
    }

    /// <summary>A provider that honours cancellation the way a real transport does: it throws off the passed token.</summary>
    private sealed class CancellationObservingProvider(string providerId, Action onCalled) : IModelProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            ProviderId: providerId,
            Capabilities: new[] { AimlTaskKind.EvalProbe },
            Transport: ProviderTransport.Api,
            CredentialRequirement: true);

        public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct)
        {
            onCalled();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ProviderExecutionResult(AimlPayload.ForOutput("unreachable"), 1, 1));
        }
    }
}
