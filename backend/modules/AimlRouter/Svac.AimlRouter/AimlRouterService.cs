using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Svac.AimlRouter.Audit;
using Svac.AimlRouter.Config;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Svac.AimlRouter.Routing;
using Svac.AimlRouter.Security;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;

namespace Svac.AimlRouter;

/// <summary>
/// The ONE egress every module reaches a model through (SLICE_S2_CONTRACT.md, 15A ruling; module
/// `Svac.AimlRouter`, contract `IAimlRouter`, impl `AimlRouterService` — verbatim names). S2 ships zero
/// consumers (§0), so nothing calls this in production yet; it is exercised by this module's own gate
/// tests (fake providers/config/quota/event-store) and evals (real local-transport provider). Whichever
/// slice gives the router a first real caller wires <c>AddAimlRouter</c> (DependencyInjection/) into that
/// caller's host — this type's own logic does not change.
/// </summary>
internal sealed class AimlRouterService(
    IEnumerable<IModelProvider> providers,
    IVendorEgressAuthorizer egressAuthorizer,
    IConfigRegistry configRegistry,
    IQuotaService quotaService,
    IEventStore eventStore) : IAimlRouter
{
    public async Task<AimlResult> InvokeAsync(AimlRequest req, RequestContext ctx, CancellationToken ct = default)
    {
        var invocationId = AimlInvocationId.New(DateTimeOffset.UtcNow, Random.Shared);
        var stopwatch = Stopwatch.StartNew();

        // 1. Vendor-egress law FIRST (§1b): the router is the only place data leaves the trust
        // boundary, and this authorizer + the allowlist ceiling below are the two INDEPENDENT locks —
        // neither is skippable by reaching the other first.
        if (!egressAuthorizer.Authorize(req.PayloadClass, req.Subject, ctx).IsAuthorized)
        {
            await AppendDecision(req, ctx, invocationId, hop: null, transport: null, decisionSource: null, policyVersion: 0, failoverFrom: null, outcome: nameof(AimlFailure.RefusedPrivacyFloor), stopwatch, tokensIn: 0, tokensOut: 0, ct);
            return AimlResult.Failed(AimlFailure.RefusedPrivacyFloor);
        }

        // 2. Budget circuit breaker (§5): the router itself as consumer of its own 10A key, at the
        // single egress, day one.
        var quotaResult = await ConsumeDailyBudget(req.Caller, ctx, ct);
        if (quotaResult is QuotaResult.Limited)
        {
            await AppendDecision(req, ctx, invocationId, hop: null, transport: null, decisionSource: null, policyVersion: 0, failoverFrom: null, outcome: nameof(AimlFailure.BudgetDenied), stopwatch, tokensIn: 0, tokensOut: 0, ct);
            return AimlResult.Failed(AimlFailure.BudgetDenied);
        }

        // 3. Resolve — pure, IO-free (Resolver): allowlist ∩ DI-registered, explicit pin honored
        // verbatim but law-bound, Automatic path chain-ordered.
        var allowlist = await configRegistry.GetValue<IReadOnlyList<ProviderAllowlistEntry>>(AimlRouterConfigKeys.ProviderAllowlist, ct);
        var registeredProviderIds = RegisteredProviderIdsFor(req.Task);
        var policyVersion = 0;


        ProviderChain chain;
        DecisionSource decisionSource;
        if (req.ExplicitPin is { } pin)
        {
            chain = Resolver.ResolveExplicitPin(pin, allowlist, registeredProviderIds, req.PayloadClass);
            decisionSource = DecisionSource.Explicit;
        }
        else
        {
            var policy = await configRegistry.GetValue<RoutingPolicy>(AimlRouterConfigKeys.RoutingPolicy, ct);
            policyVersion = policy.Version;
            chain = Resolver.Resolve(policy, allowlist, registeredProviderIds, req.Task, req.PayloadClass, ctx.Region);
            decisionSource = DecisionSource.Policy;
        }

        if (chain.IsEmpty)
        {
            // Build-phase fix (SLICE_S2_CONTRACT.md §10.3 FINDING 2, backend/e2e/aiml-router.e2e.mjs's own
            // header): a chain that resolved empty ONLY because a genuine candidate's payload class
            // exceeded the provider's ceiling is a privacy-law refusal, not a routing-configuration gap —
            // ProviderChain.AnyCeilingSkip (set by Resolver) carries exactly that distinction through this
            // otherwise-opaque IsEmpty signal. NotAllowlisted stays reserved for a pin naming a provider/
            // model/environment that was never a candidate at all; NoRouteConfigured stays reserved for
            // the Automatic path finding no candidate for reasons OTHER than the privacy floor.
            var cause = chain.AnyCeilingSkip
                ? AimlFailure.RefusedPrivacyFloor
                : req.ExplicitPin is not null ? AimlFailure.NotAllowlisted : AimlFailure.NoRouteConfigured;
            await AppendDecision(req, ctx, invocationId, hop: null, transport: null, decisionSource: decisionSource, policyVersion, failoverFrom: null, outcome: cause.ToString(), stopwatch, tokensIn: 0, tokensOut: 0, ct);
            return AimlResult.Failed(cause);
        }

        // 4. Walk the chain in order (failover, §1b): a hop that throws is a failed HOP, not a failed
        // call — try the next one; chain exhausted is the only case that becomes a Failure.
        var timeoutSeconds = await configRegistry.GetValue<int>(AimlRouterConfigKeys.InvokeTimeoutSeconds, ct);
        string? failoverFrom = null;
        for (var depth = 0; depth < chain.Hops.Count; depth++)
        {
            var hop = chain.Hops[depth];
            var provider = providers.FirstOrDefault(p => p.Descriptor.ProviderId == hop.Provider);
            if (provider is null)
            {
                continue; // resolved against a provider id with no matching DI registration at call time -> treat as a failed hop.
            }

            try
            {
                var invocation = new ProviderInvocation(hop.Model, req.Payload, TimeSpan.FromSeconds(timeoutSeconds));
                var execResult = await provider.ExecuteAsync(invocation, ct);

                var receipt = new RoutingReceipt(
                    InvocationId: invocationId,
                    Provider: hop.Provider,
                    Model: hop.Model,
                    DecisionSource: depth == 0 ? decisionSource : DecisionSource.Failover,
                    PolicyVersion: policyVersion,
                    FallbackDepth: depth,
                    LatencyMs: stopwatch.ElapsedMilliseconds,
                    InputTokens: execResult.InputTokens,
                    OutputTokens: execResult.OutputTokens,
                    FailoverFrom: failoverFrom);

                await AppendDecision(req, ctx, invocationId, hop, provider.Descriptor.Transport.ToString(), receipt.DecisionSource, policyVersion, failoverFrom, outcome: "Success", stopwatch, execResult.InputTokens, execResult.OutputTokens, ct);
                return AimlResult.Ok(execResult.Output, receipt);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failoverFrom = $"{hop.Provider}:{hop.Model}";
            }
        }

        await AppendDecision(req, ctx, invocationId, hop: null, transport: null, decisionSource: decisionSource, policyVersion, failoverFrom, outcome: nameof(AimlFailure.ChainExhausted), stopwatch, tokensIn: 0, tokensOut: 0, ct);
        return AimlResult.Failed(AimlFailure.ChainExhausted);
    }

    private async Task<QuotaResult> ConsumeDailyBudget(CallerModule caller, RequestContext ctx, CancellationToken ct)
    {
        var actor = SystemActors.ForCallerModule(caller);
        var context = new QuotaContext(
            ResetSpec: new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
            TimeZone: TimeZoneInfo.Utc,
            ConDayCutoff: TimeOnly.MinValue, // daily/UTC (§5) — con-local math over a UTC midnight cutoff IS plain daily/UTC.
            Now: DateTimeOffset.UtcNow);
        return await quotaService.Consume(actor, Svac.AimlRouter.Quota.AimlRouterQuotaKeys.CallDaily, context, ct);
    }

    /// <summary>
    /// "Effective set = allowlist (9A) ∩ DI-registered (environment truth)" (§1b): a provider only
    /// counts as DI-registered FOR THIS CALL if it also declares the requested task as a capability —
    /// an <see cref="IModelProvider"/> registered for translation-only work is not a candidate for a
    /// Generate request just because it exists in this environment's DI container.
    /// </summary>
    private HashSet<string> RegisteredProviderIdsFor(AimlTaskKind task) =>
        providers.Where(p => p.Descriptor.Capabilities.Contains(task)).Select(p => p.Descriptor.ProviderId).ToHashSet();

    private async Task AppendDecision(
        AimlRequest req, RequestContext ctx, AimlInvocationId invocationId, ResolvedHop? hop, string? transport,
        DecisionSource? decisionSource, int policyVersion, string? failoverFrom, string outcome, Stopwatch stopwatch,
        int tokensIn, int tokensOut, CancellationToken ct)
    {
        var evt = new AimlRouteDecidedEvent(
            InvocationId: invocationId.Value,
            Caller: req.Caller.ToString(),
            Task: req.Task.ToString(),
            PayloadClass: req.PayloadClass.ToString(),
            SubjectRef: req.Subject?.ToString(),
            Provider: hop?.Provider,
            Model: hop?.Model,
            Transport: transport,
            DecisionSource: decisionSource?.ToString(),
            PolicyVersion: policyVersion,
            FailoverFrom: failoverFrom,
            Outcome: outcome,
            LatencyMs: stopwatch.ElapsedMilliseconds,
            InputTokens: tokensIn,
            OutputTokens: tokensOut,
            PayloadSha256: Sha256Of(req.Payload));

        var payloadJson = JsonSerializer.Serialize(evt);
        await eventStore.Append(StreamType.Audit, streamId: invocationId.Value, eventType: "aiml.route_decided", payloadJson, ctx, ExpectedVersion.AnyVersion, ct);
    }

    private static string Sha256Of(AimlPayload payload)
    {
        var canonical = JsonSerializer.Serialize(new { payload.System, payload.Messages, payload.MaxTokens, payload.Temperature, payload.StructuredOutputSchema });
        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
