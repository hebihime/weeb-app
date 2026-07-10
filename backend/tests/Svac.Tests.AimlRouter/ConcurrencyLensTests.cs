using Svac.AimlRouter;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Svac.AimlRouter.Routing;
using Svac.AimlRouter.Security;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// CONCURRENCY LENS — adversarial suite (security phase, SLICE_PLAYBOOK). Every test in this file
/// is written to FAIL against the running S2 code: each demonstrates one concurrency /
/// cancellation-interleaving / atomicity break in AimlRouterService or its use of the S1 substrate.
/// A test here going green means the corresponding finding is fixed; do NOT "fix" a test by
/// weakening its assertion.
///
/// Findings covered:
///   CONC-S2-1  caller cancellation is misclassified as a failed hop -> failover egress AFTER cancel
///   CONC-S2-2  cancellation during the chain walk loses the aiml.route_decided audit event entirely
///   CONC-S2-3  AimlFailure.Timeout is unreachable — a timed-out hop audits as ChainExhausted
///   CONC-S2-4  quota Consume and the audit append are two transactions — they can diverge, and the
///              closed result union is violated when the append fails after budget was burned
///   CONC-S2-5  allowlist and routing_policy are read in two non-atomic queries — a coordinated
///              config change interleaving between them fails calls that every committed snapshot allows
/// </summary>
public sealed class ConcurrencyLensTests
{
    private static readonly ProviderAllowlistEntry SeedEntry = new(
        Name: "seed", Family: "claude", Kinds: new[] { "llm" },
        PayloadClassCeiling: PayloadClass.Pseudonymous, DpaSigned: false, SpecialCategoryOk: false,
        Residency: "global", Models: new[] { "seed-v0" });

    private static readonly ProviderAllowlistEntry Seed2Entry = SeedEntry with { Name = "seed2", Models = new[] { "seed2-v0" } };

    private static RoutingPolicy Policy(params TaskChainLink[] chain) => new(
        Version: 7,
        DefaultChain: chain,
        TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
        ResidencyOverrides: Array.Empty<ResidencyOverride>());

    private static FakeConfigRegistry Config(IReadOnlyList<ProviderAllowlistEntry> allowlist, RoutingPolicy policy) =>
        new FakeConfigRegistry()
            .With("aiml.provider_allowlist", allowlist)
            .With("aiml.routing_policy", policy)
            .With("aiml.invoke_timeout_seconds", 5);

    private static RequestContext SystemCtx() => RequestContext.System(
        ActorRef.System(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared)), "test");

    private static AimlRequest Request() => new(
        Task: AimlTaskKind.EvalProbe, Caller: CallerModule.System, PayloadClass: PayloadClass.NonPersonal,
        Subject: null, Payload: AimlPayload.ForUserTurn("ping"), TargetLocale: null, ExplicitPin: null);

    // ---------------------------------------------------------------------------------------------
    // CONC-S2-1 — AimlRouterService.cs:128: `catch (Exception ex) when (ex is not OutOfMemoryException)`
    // also catches OperationCanceledException raised by the CALLER's own token. A cancelled call is
    // recorded as a failed hop and the walk proceeds to the NEXT provider — a fresh vendor-egress
    // attempt (spend + user data leaving the trust boundary) AFTER the caller cancelled. §1b's failover
    // law covers provider failure, never caller cancellation.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task CONC_S2_1_CallerCancellation_MustNotFailoverToTheNextProvider()
    {
        using var cts = new CancellationTokenSource();
        var secondProviderCalled = false;

        var providers = new IModelProvider[]
        {
            new ScriptedProvider("seed", (_, ct) =>
            {
                cts.Cancel(); // the caller cancels mid-hop (timeout, request aborted, host shutdown...)
                throw new OperationCanceledException(ct);
            }),
            new ScriptedProvider("seed2", (_, _) =>
            {
                secondProviderCalled = true;
                return new ProviderExecutionResult(AimlPayload.ForOutput("late-egress"), 1, 1);
            }),
        };

        var router = new AimlRouterService(
            providers,
            new RefuseAllSpecialCategoryAuthorizer(),
            Config(new[] { SeedEntry, Seed2Entry }, Policy(new TaskChainLink("seed", "seed-v0"), new TaskChainLink("seed2", "seed2-v0"))),
            new FakeQuotaService(),
            new FakeEventStore());

        try
        {
            await router.InvokeAsync(Request(), SystemCtx(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // propagating the cancellation is the correct behavior; the assertion below is the law.
        }

        Assert.False(secondProviderCalled,
            "caller cancellation was treated as a failed hop: the router performed a NEW vendor-egress " +
            "attempt (hop 2) AFTER the caller's CancellationToken was already cancelled — failover " +
            "(§1b) is for provider failure, never for the caller giving up.");
    }

    // ---------------------------------------------------------------------------------------------
    // CONC-S2-2 — same root cause, second consequence: when every hop "fails" by cancellation, the
    // terminal AppendDecision(ChainExhausted) call runs with the already-cancelled token. Against the
    // real PostgresEventStore (EF honors the token) the append itself throws, so an invocation that
    // REALLY attempted vendor egress leaves ZERO aiml.route_decided event — "every InvokeAsync appends
    // ONE event" (§1b audit law) silently broken exactly when calls are torn down under load.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task CONC_S2_2_CancellationDuringChainWalk_MustStillAuditTheEgressAttempt()
    {
        using var cts = new CancellationTokenSource();
        var eventStore = new CancellationHonoringEventStore(); // behaves like PostgresEventStore: EF throws on a cancelled token

        var providers = new IModelProvider[]
        {
            new ScriptedProvider("seed", (_, ct) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(ct);
            }),
        };

        var router = new AimlRouterService(
            providers,
            new RefuseAllSpecialCategoryAuthorizer(),
            Config(new[] { SeedEntry }, Policy(new TaskChainLink("seed", "seed-v0"))),
            new FakeQuotaService(),
            eventStore);

        try
        {
            await router.InvokeAsync(Request(), SystemCtx(), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // however the call ends, the audit law must hold.
        }

        Assert.Equal(1, eventStore.AppendedCount);
    }

    // ---------------------------------------------------------------------------------------------
    // CONC-S2-3 — AimlFailure.Timeout is unreachable. The contract (§1b closed union; §4
    // "aiml.invoke_timeout_seconds ... feeds the Timeout failure kind") and
    // AnthropicLocalTransport.cs:94-95's own comment ("AimlRouterService maps this exception type to
    // AimlFailure.Timeout") both promise a Timeout outcome. The chain walk's catch-all converts the
    // transports' TimeoutException into a generic failed hop, so a single-hop chain that times out
    // returns — and AUDITS — ChainExhausted. Under slow-provider contention the S5 tiles and the T15
    // burst checkpoint (§12.6) cannot tell provider-down from provider-slow.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task CONC_S2_3_HopTimeout_OnSingleHopChain_ReturnsTimeout_NotChainExhausted()
    {
        var providers = new IModelProvider[]
        {
            new ScriptedProvider("seed", (inv, _) => throw new TimeoutException($"exceeded {inv.Timeout}")),
        };

        var router = new AimlRouterService(
            providers,
            new RefuseAllSpecialCategoryAuthorizer(),
            Config(new[] { SeedEntry }, Policy(new TaskChainLink("seed", "seed-v0"))),
            new FakeQuotaService(),
            new FakeEventStore());

        var result = await router.InvokeAsync(Request(), SystemCtx());

        var failure = Assert.IsType<AimlResult.Failure>(result);
        Assert.Equal(AimlFailure.Timeout, failure.Cause);
    }

    // ---------------------------------------------------------------------------------------------
    // CONC-S2-4 — quota Consume (QuotaService.cs:41, ExecuteSqlInterpolatedAsync = its own implicit
    // transaction, committed immediately) and the audit append (PostgresEventStore.Append ->
    // SaveChangesAsync, a SECOND transaction) are not atomic, and AimlRouterService opens no
    // transaction around them. If the append fails after Consume committed (db blip, the
    // (stream_id, seq) race PostgresEventStore translates to ConcurrencyConflictException, crash):
    //   (a) daily budget is burned with ZERO decision record — §8 "decision and record cannot diverge";
    //   (b) the raw exception escapes InvokeAsync — the §1b CLOSED union ("never a silent drop",
    //       consumers map failures to their standard error path) is bypassed entirely.
    // ---------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW finding CONC-S2-4a — quota Consume and the audit append are " +
        "two unrelated autocommitted transactions; the true fix is a shared transaction / transactional-" +
        "outbox shape across QuotaService and PostgresEventStore (§8 row), an architectural change, not a " +
        "patch. The exception-escape half is fixed separately (CONC-S2-4b, AppendDecision's own try/catch).")]
    public async Task CONC_S2_4a_WhenAuditAppendFails_BudgetMustNotHaveBeenBurnedWithoutARecord()
    {
        var quota = new CountingQuotaService();
        var eventStore = new FailingEventStore();

        var router = new AimlRouterService(
            new IModelProvider[] { new ScriptedProvider("seed", (_, _) => new ProviderExecutionResult(AimlPayload.ForOutput("ok"), 1, 1)) },
            new RefuseAllSpecialCategoryAuthorizer(),
            Config(new[] { SeedEntry }, Policy(new TaskChainLink("seed", "seed-v0"))),
            quota,
            eventStore);

        try
        {
            await router.InvokeAsync(Request(), SystemCtx());
        }
        catch (Exception)
        {
            // the divergence assertion below is the law, however the call ends.
        }

        Assert.True(quota.ConsumeCount == 0 || eventStore.AppendedCount > 0,
            $"quota Consume committed ({quota.ConsumeCount} consume) but zero aiml.route_decided events " +
            "exist — the two writes ride two separate transactions with no shared scope, so the budget " +
            "counter and the decision record diverged (SLICE_S2_CONTRACT.md §8 transactional-outbox row).");
    }

    [Fact]
    public async Task CONC_S2_4b_WhenAuditAppendFails_InvokeAsyncMustStillReturnTheClosedUnion()
    {
        var router = new AimlRouterService(
            new IModelProvider[] { new ScriptedProvider("seed", (_, _) => new ProviderExecutionResult(AimlPayload.ForOutput("ok"), 1, 1)) },
            new RefuseAllSpecialCategoryAuthorizer(),
            Config(new[] { SeedEntry }, Policy(new TaskChainLink("seed", "seed-v0"))),
            new FakeQuotaService(),
            new FailingEventStore());

        var escaped = await Record.ExceptionAsync(() => router.InvokeAsync(Request(), SystemCtx()));

        Assert.True(escaped is null,
            $"InvokeAsync leaked a raw {escaped?.GetType().Name} instead of returning the closed " +
            "Success|Failure union (§1b: 'never a silent drop; the closed result union has no code path " +
            "that returns nothing') — every consumer must now know to catch event-store exceptions.");
    }

    // ---------------------------------------------------------------------------------------------
    // CONC-S2-5 — AimlRouterService reads `aiml.provider_allowlist` (line 59) and `aiml.routing_policy`
    // (line 73) as two separate registry queries with no snapshot. A coordinated two-key config change
    // (add provider to allowlist, then point the chain at it — each key individually bounds-valid at
    // SetValue) interleaving between the two reads gives the resolver a (old allowlist, new policy)
    // pair that NEVER existed as a committed state: every in-flight call fails NoRouteConfigured — and
    // is AUDITED as a routing gap at the new policy version — although both the before and the after
    // snapshot resolve the call successfully.
    // ---------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW finding CONC-S2-5 — the torn two-key config read " +
        "(allowlist + routing_policy) needs an atomic multi-key snapshot read in ConfigRegistry; " +
        "fail-closed today (worst case is a spurious NoRouteConfigured during a coordinated two-SetValue " +
        "change), no privacy downgrade possible.")]
    public async Task CONC_S2_5_TornConfigRead_AcrossAllowlistAndPolicy_MustNotFailACallEveryCommittedSnapshotAllows()
    {
        // Committed state 1: allowlist [seed],        policy chain [seed]   -> resolves fine.
        // Committed state 2: allowlist [seed, seed2], policy chain [seed2]  -> resolves fine.
        // Torn read: allowlist from state 1 + policy from state 2 -> empty chain, NoRouteConfigured.
        var registry = new TornReadConfigRegistry(
            allowlistFirstRead: new[] { SeedEntry },
            policyAfterCommit: Policy(new TaskChainLink("seed2", "seed2-v0")));

        var router = new AimlRouterService(
            new IModelProvider[]
            {
                new ScriptedProvider("seed", (_, _) => new ProviderExecutionResult(AimlPayload.ForOutput("s1"), 1, 1)),
                new ScriptedProvider("seed2", (_, _) => new ProviderExecutionResult(AimlPayload.ForOutput("s2"), 1, 1)),
            },
            new RefuseAllSpecialCategoryAuthorizer(),
            registry,
            new FakeQuotaService(),
            new FakeEventStore());

        var result = await router.InvokeAsync(Request(), SystemCtx());

        Assert.True(result is AimlResult.Success,
            "a config edit that is valid before AND after its commit produced a NoRouteConfigured " +
            "failure for a concurrent in-flight call, because the allowlist and the routing policy were " +
            "read in two non-atomic queries (AimlRouterService.cs:59 vs :73).");
    }

    // --------------------------------------- lens fakes ---------------------------------------

    /// <summary>An IModelProvider whose ExecuteAsync behavior is supplied per test.</summary>
    private sealed class ScriptedProvider(string providerId, Func<ProviderInvocation, CancellationToken, ProviderExecutionResult> script) : IModelProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            ProviderId: providerId,
            Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
            Transport: ProviderTransport.LocalProcess,
            CredentialRequirement: false);

        public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct) =>
            Task.FromResult(script(invocation, ct));
    }

    /// <summary>Counts Consume calls; each successful call models one committed quota transaction (QuotaService.cs:41 commits immediately).</summary>
    private sealed class CountingQuotaService : IQuotaService
    {
        public int ConsumeCount { get; private set; }

        public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default)
        {
            ConsumeCount++;
            return Task.FromResult<QuotaResult>(new QuotaResult.Ok(new Consumed(Remaining: 9999, ResetsAt: context.Now.AddDays(1))));
        }
    }

    /// <summary>Append always fails — models the second transaction (the audit SaveChanges) failing after the first (quota) committed.</summary>
    private sealed class FailingEventStore : IEventStore
    {
        public int AppendedCount { get; private set; }

        public Task<RecordedEvent> Append(StreamType stream, string streamId, string eventType, string? payloadJson, RequestContext ctx, ExpectedVersion expectedVersion, CancellationToken ct = default) =>
            throw new InvalidOperationException("simulated audit-append transaction failure (db blip / seq race / crash window).");

        public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Honors the cancellation token exactly like the real PostgresEventStore (EF's SaveChangesAsync throws on a cancelled token).</summary>
    private sealed class CancellationHonoringEventStore : IEventStore
    {
        public int AppendedCount { get; private set; }

        public Task<RecordedEvent> Append(StreamType stream, string streamId, string eventType, string? payloadJson, RequestContext ctx, ExpectedVersion expectedVersion, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            AppendedCount++;
            return Task.FromResult(new RecordedEvent(
                EventId: $"evt_lens{AppendedCount:D24}", StreamId: streamId, Seq: AppendedCount, EventType: eventType,
                PayloadJson: payloadJson, ReversalOf: null, Tombstone: false, ActorRef: ctx.Actor.ToString(),
                Region: ctx.Region.ToString(), LawfulBasis: ctx.LawfulBasisVariant.Key,
                OccurredAt: DateTimeOffset.UtcNow, RecordedAt: DateTimeOffset.UtcNow));
        }

        public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Models the torn read: the allowlist query returns the PRE-commit value, and the act of reading it
    /// "lands" the coordinated two-key config change, so the subsequent routing_policy query returns the
    /// POST-commit value — exactly what a concurrent SetValue pair does to an in-flight InvokeAsync.
    /// </summary>
    private sealed class TornReadConfigRegistry(
        IReadOnlyList<ProviderAllowlistEntry> allowlistFirstRead,
        RoutingPolicy policyAfterCommit) : Svac.DomainCore.Contracts.Config.IConfigRegistry
    {
        public Task<T> GetValue<T>(string key, CancellationToken ct = default) => key switch
        {
            "aiml.provider_allowlist" => Task.FromResult((T)(object)allowlistFirstRead),
            "aiml.routing_policy" => Task.FromResult((T)(object)policyAfterCommit),
            "aiml.invoke_timeout_seconds" => Task.FromResult((T)(object)5),
            _ => throw new KeyNotFoundException(key),
        };

        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
