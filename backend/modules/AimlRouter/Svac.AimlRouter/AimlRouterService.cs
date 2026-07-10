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
    /// <summary>TRUST-BREAK-7/SR-F5 (SECURITY_REVIEW_S2.md): mirrors i18n/locales.json's shipped four —
    /// kept as a pure code table (no file IO at runtime, Deterministic discipline) rather than a
    /// repo-relative read that would not survive a real publish/deploy layout. Keep in sync by hand; a
    /// change to i18n/locales.json's own set is a versioned i18n-lint event, not a router concern.</summary>
    private static readonly HashSet<string> ShippedLocales = new(StringComparer.Ordinal) { "en", "es", "pt", "zh-Hans" };

    public async Task<AimlResult> InvokeAsync(AimlRequest req, RequestContext ctx, CancellationToken ct = default)
    {
        var invocationId = AimlInvocationId.New(DateTimeOffset.UtcNow, Random.Shared);
        var stopwatch = Stopwatch.StartNew();

        // PII-S2-F3 (region/lawful-basis provenance): resolved ONCE, used by every AppendDecision call
        // below — "system-actor calls inherit the SUBJECT's region" (§1b/§8), the analog of S1's
        // PurgePipeline.ResolveSubjectRegion. A subject-less call keeps the caller's own ctx untouched.
        var auditCtx = await ResolveAuditContext(req, ctx, ct);

        // 0. TRUST-BREAK-7/SR-F5: TargetLocale must be validated BEFORE any vendor egress (§1b: "must ∈
        // i18n/locales.json ×4"; §8 i18n row). Ahead of the egress authorizer/budget check on purpose — a
        // malformed request should never spend budget or even reach the privacy-floor lock.
        if (req.Task == AimlTaskKind.Translate && !IsShippedLocale(req.TargetLocale))
        {
            await AppendDecision(req, auditCtx, invocationId, hop: null, transport: null, decisionSource: null, policyVersion: 0, failoverFrom: null, outcome: nameof(AimlFailure.InvalidRequest), stopwatch, tokensIn: 0, tokensOut: 0);
            return AimlResult.Failed(AimlFailure.InvalidRequest);
        }

        // 1. Vendor-egress law FIRST (§1b): the router is the only place data leaves the trust
        // boundary, and this authorizer + the allowlist ceiling below are the two INDEPENDENT locks —
        // neither is skippable by reaching the other first.
        if (!egressAuthorizer.Authorize(req.PayloadClass, req.Subject, ctx).IsAuthorized)
        {
            await AppendDecision(req, auditCtx, invocationId, hop: null, transport: null, decisionSource: null, policyVersion: 0, failoverFrom: null, outcome: nameof(AimlFailure.RefusedPrivacyFloor), stopwatch, tokensIn: 0, tokensOut: 0);
            return AimlResult.Failed(AimlFailure.RefusedPrivacyFloor);
        }

        // 2. Budget circuit breaker (§5): the router itself as consumer of its own 10A key, at the
        // single egress, day one.
        var quotaResult = await ConsumeDailyBudget(req.Caller, ctx, ct);
        if (quotaResult is QuotaResult.Limited)
        {
            await AppendDecision(req, auditCtx, invocationId, hop: null, transport: null, decisionSource: null, policyVersion: 0, failoverFrom: null, outcome: nameof(AimlFailure.BudgetDenied), stopwatch, tokensIn: 0, tokensOut: 0);
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
            await AppendDecision(req, auditCtx, invocationId, hop: null, transport: null, decisionSource: decisionSource, policyVersion, failoverFrom: null, outcome: cause.ToString(), stopwatch, tokensIn: 0, tokensOut: 0);
            return AimlResult.Failed(cause);
        }

        // 4. Walk the chain in order (failover, §1b): a hop that throws is a failed HOP, not a failed
        // call — try the next one. CONC-S2-1: caller CANCELLATION is never a failed hop — it propagates
        // immediately, with no further vendor-egress attempt (failover is for provider failure, never for
        // the caller giving up). TRUST-BREAK-6/CONC-S2-3/SR-F1/SR-F2: a chain with exactly one REAL
        // attempt surfaces that attempt's own failure kind (Timeout / ProviderError), never the opaque
        // ChainExhausted a genuine multi-hop exhaustion deserves. SR-F4: the full trail of every attempted
        // hop accumulates into failoverFrom, never just the last one.
        var timeoutSeconds = await configRegistry.GetValue<int>(AimlRouterConfigKeys.InvokeTimeoutSeconds, ct);
        string? failoverFrom = null;
        var attemptedHops = 0;
        Exception? lastFailure = null;

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

                await AppendDecision(req, auditCtx, invocationId, hop, provider.Descriptor.Transport.ToString(), receipt.DecisionSource, policyVersion, failoverFrom, outcome: "Success", stopwatch, execResult.InputTokens, execResult.OutputTokens);
                return AimlResult.Ok(execResult.Output, receipt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // CONC-S2-1: the CALLER gave up — audit the attempt (CONC-S2-2: on a shielded, never-
                // cancelled token, so the append itself cannot also be killed by the same cancellation),
                // then propagate. Never a failed hop, never a next-provider attempt.
                await AppendDecision(req, auditCtx, invocationId, hop, provider.Descriptor.Transport.ToString(), depth == 0 ? decisionSource : DecisionSource.Failover, policyVersion, failoverFrom, outcome: "Cancelled", stopwatch, tokensIn: 0, tokensOut: 0);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failoverFrom = failoverFrom is null ? $"{hop.Provider}:{hop.Model}" : $"{failoverFrom},{hop.Provider}:{hop.Model}";
                attemptedHops++;
                lastFailure = ex;
            }
        }

        var exhaustionCause = attemptedHops <= 1
            ? lastFailure switch
            {
                TimeoutException => AimlFailure.Timeout,
                not null => AimlFailure.ProviderError,
                null => AimlFailure.ChainExhausted, // zero real attempts (every hop skipped: no DI match at call time).
            }
            : AimlFailure.ChainExhausted;

        await AppendDecision(req, auditCtx, invocationId, hop: null, transport: null, decisionSource: decisionSource, policyVersion, failoverFrom, outcome: exhaustionCause.ToString(), stopwatch, tokensIn: 0, tokensOut: 0);
        return AimlResult.Failed(exhaustionCause);
    }

    private static bool IsShippedLocale(string? locale) => locale is not null && ShippedLocales.Contains(locale);

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

    /// <summary>
    /// PII-S2-F3 (SECURITY_REVIEW_S2.md): the analog of S1's PurgePipeline.ResolveSubjectRegion — a
    /// system-actor call ABOUT a subject inherits that subject's own recorded region (from whatever
    /// stream already has a row for them), never the caller's own ZZ/n-a pure-system sentinel. Best
    /// effort: a lookup failure (or a subject with no rows anywhere) falls back to the caller's own ctx
    /// unchanged, matching PurgePipeline's own "no rows anywhere -> caller's region" fallback shape — a
    /// provenance lookup must never itself block routing.
    /// </summary>
    private async Task<RequestContext> ResolveAuditContext(AimlRequest req, RequestContext ctx, CancellationToken ct)
    {
        if (req.Subject is not { } subject)
        {
            return ctx;
        }

        try
        {
            var subjectStreamId = subject.Id.ToString();
            foreach (var stream in Enum.GetValues<StreamType>())
            {
                await foreach (var evt in eventStore.ReadStream(stream, subjectStreamId, fromSeq: 0, ct))
                {
                    if (evt.Region != RegionCode.Unknown.ToString())
                    {
                        var parts = evt.Region.Split('-', 2);
                        return ctx with { Region = new RegionCode(parts[0], parts.Length > 1 ? parts[1] : null) };
                    }
                    break; // only the earliest row on this stream matters; move on to the next stream type.
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // best-effort provenance lookup — never block routing itself over it.
        }

        return ctx;
    }

    private async Task AppendDecision(
        AimlRequest req, RequestContext auditCtx, AimlInvocationId invocationId, ResolvedHop? hop, string? transport,
        DecisionSource? decisionSource, int policyVersion, string? failoverFrom, string outcome, Stopwatch stopwatch,
        int tokensIn, int tokensOut)
    {
        try
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

            // F-S2P1/F-S2P2/MINOR-S2-F1(+F1b) (SECURITY_REVIEW_S2.md): a subject-bearing decision is keyed
            // by the SUBJECT's own stream id — the one selector PurgePipeline.ExecuteOnEventStream reads
            // (stream_id == subject.ResourceId) — so MinorPurge/AccountDeletion/StatutoryErasure/
            // RetentionExpiry can reach it. A subject-less system call keeps the invocation id as its
            // stream; the invocation id is never lost either way — it rides AimlRouteDecidedEvent.
            // InvocationId inside the payload on every path.
            var streamId = req.Subject?.Id.ToString() ?? invocationId.Value;

            // CONC-S2-2: always CancellationToken.None here — every attempted egress leaves exactly one
            // aiml.route_decided event even when the CALLER's own token is already cancelled by the time
            // this terminal/failure append runs; that is the only way to keep the audit law true under
            // cancellation.
            await eventStore.Append(StreamType.Audit, streamId, "aiml.route_decided", payloadJson, auditCtx, ExpectedVersion.AnyVersion, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // CONC-S2-4b: an event-store throw must never escape InvokeAsync — the closed Success|Failure
            // union (§1b) stays closed on every path. CONC-S2-4a (deferred) names the real fix: quota
            // Consume and this append are two unrelated autocommitted transactions; losing the decision
            // record here — while the caller's own outcome is still returned honestly — is the documented
            // cost until a shared-transaction/outbox fix lands.
        }
    }

    private static string Sha256Of(AimlPayload payload)
    {
        var canonical = JsonSerializer.Serialize(new { payload.System, payload.Messages, payload.MaxTokens, payload.Temperature, payload.StructuredOutputSchema });
        var bytes = Encoding.UTF8.GetBytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
