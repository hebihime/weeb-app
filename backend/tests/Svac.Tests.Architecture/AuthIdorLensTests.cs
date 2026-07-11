using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Region;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL — auth / IDOR lens (4A refusal, topology-is-not-a-guard, encoded identity).
/// Every test here is a demonstrated break against the S1 substrate as built. These are RED on purpose:
/// each asserts the property the substrate PROMISES (SLICE_S1_CONTRACT.md) and currently VIOLATES.
/// </summary>
public sealed class AuthIdorLensTests
{
    // ---------------------------------------------------------------------------------------------
    // FINDING 1 — Encoded identity: every anonymous request is stamped with the SYSTEM (`sys_`) prefix.
    //
    // RequestContextMiddleware.cs:19,43 mints the anonymous ActorRef with IdPrefixes.System while the
    // Kind is ActorKind.Anonymous. OpaqueId.cs:8-9 and ActorRef.cs:14-16 both state the prefix is
    // "load-bearing, not decorative — 4A axis checks and ER-6 absence rules key off it". So the lowest-
    // privilege actor carries the highest-privilege id prefix. Any code that trusts the prefix (which the
    // contract explicitly sanctions) reads an unauthenticated caller as the system actor.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task AnonymousRequest_MustNotCarryTheSystemActorPrefix()
    {
        var accessor = new AmbientRequestContextAccessor();
        // Capture inside the pipeline continuation — where the ambient context is live — because the
        // middleware sets it on an AsyncLocal that is popped once InvokeAsync returns to the caller.
        ActorRef actor = default;
        var middleware = new RequestContextMiddleware(
            next: _ => { actor = accessor.Current.Actor; return Task.CompletedTask; },
            accessor: accessor,
            regionResolver: new FixedUnknownRegionResolver());

        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-abc" };
        await middleware.InvokeAsync(httpContext);

        Assert.Equal(ActorKind.Anonymous, actor.Kind);

        // The break: prefix is "sys" (system) on an anonymous, unauthenticated request.
        Assert.NotEqual(IdPrefixes.System, actor.Id.Prefix);
    }

    // ---------------------------------------------------------------------------------------------
    // FINDING 2 — Topology-is-not-a-guard: the 4A boot-refusal fails OPEN for a catch-all `Map` route.
    //
    // StartupPolicyCoverage.cs:37-45 decides "is this a mutation?" purely from HttpMethodMetadata. A route
    // mapped with IEndpointRouteBuilder.Map(pattern, handler) matches ALL verbs (POST/PUT/DELETE included)
    // yet carries NO HttpMethodMetadata, so `methodMetadata is null` → `continue` → the endpoint is
    // silently skipped. A policy-less, consumer-reachable mutation ships and the fail-closed boot check
    // never fires. §3 promises "the host refuses to boot if any non-GET endpoint lacks a [PolicyAction]".
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void CatchAllMapMutationEndpoint_MustNotBypassBootRefusal()
    {
        var app = BuildMinimalApp();

        // `Map` (all-verbs overload) accepts POST but attaches no HttpMethodMetadata. No .RequirePolicyAction.
        app.Map("/canary/all-verbs", () => Results.Ok());

        // The break: the boot check should refuse (this endpoint accepts POST with no policy), but it
        // returns cleanly because it only inspects endpoints that carry HttpMethodMetadata.
        Assert.Throws<InvalidOperationException>(() => app.RequireMutationsPolicyMapped());
    }

    // ---------------------------------------------------------------------------------------------
    // FINDING 3 — RETIRED at S3 (SLICE_S3_CONTRACT.md §3a, deferred finding Auth-F3). The original break:
    // PolicyEnforcementFilter always called Authorize with TargetRef.ForAction(action) — a synthetic
    // target whose ResourceId was ALWAYS null — so the route's real resource id never reached
    // IPolicyEngine.Authorize and object-level ownership could never be checked at the chokepoint.
    //
    // PHASE_2A_SUBSTRATE.md §1/§4 (landed ahead of S3's own build, per the ratified BUILD-ORDERING)
    // shipped the fix as a general mechanism: PolicyTargetBinding.FromRoute conveys a REAL resource id
    // read out of the endpoint's own matched route into TargetRef.ResourceId, and RequirePolicyAction's
    // new overload wires both the binding and the boot-refusal metadata that makes a resource-scoped
    // action with no target conveyance structurally unshippable (PolicyTargetBindingBootRefusalTests.cs).
    // This test now proves the fixed mechanism, non-vacuously, using S3's own real exemplar action name
    // (identity.session.revoke, §3b) — the SAME shape DELETE /v1/me/sessions/{sessionId} maps with.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task PolicyChokepoint_MustConveyTheRealTargetResourceId()
    {
        var spy = new CapturingPolicyEngine();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContextAccessor>(new StubAccessor(AnonymousContext()));
        services.AddSingleton<IPolicyEngine>(spy);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        // Simulate a route like DELETE /v1/me/sessions/{sessionId} bound to victim's id — the value an
        // IDOR attacker varies. FromRoute reads it out of HttpContext.Request.RouteValues, never out of
        // the filter-invocation ARGUMENTS array (that array holds handler parameters, not route values).
        var routeResourceId = "ses_01HZZZZZZZZZZZZZZZZZZZZZZZ";
        httpContext.Request.RouteValues = new Microsoft.AspNetCore.Routing.RouteValueDictionary
        {
            ["sessionId"] = routeResourceId,
        };
        var filterContext = EndpointFilterInvocationContext.Create(httpContext);

        var filter = new PolicyEnforcementFilter("identity.session.revoke", PolicyTargetBinding.FromRoute("sessionId", "session"));
        await filter.InvokeAsync(filterContext, _ => ValueTask.FromResult<object?>(Results.Ok()));

        Assert.NotNull(spy.LastTarget);
        // The fix: the chokepoint now conveys the real route resource id — ownership CAN be checked here.
        Assert.Equal(routeResourceId, spy.LastTarget!.Value.ResourceId);
        Assert.Equal("session", spy.LastTarget!.Value.ResourceType);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static WebApplication BuildMinimalApp()
    {
        var builder = WebApplication.CreateBuilder();
        // PHASE_2A_SUBSTRATE.md §1: IPolicyTable now resolves as the union of registered
        // IPolicyTableSource(s) — register the real core rows so this DI-constructed PolicyTable is
        // byte-identical to the pre-Phase-2a table (CatchAllMapMutationEndpoint_MustNotBypassBootRefusal
        // below relies on the real core.ledger.append row existing).
        builder.Services.AddSingleton<IPolicyTableSource, CorePolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTable, PolicyTable>();
        return builder.Build();
    }

    private static RequestContext AnonymousContext()
    {
        var actor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UnixEpoch, new Random(1)), ActorKind.Anonymous);
        return new RequestContext(actor, RegionCode.Unknown, RegionSource.EdgeInferred,
            LawfulBasisVariant.ConservativeGlobalV0, "en", "corr-1");
    }

    private sealed class FixedUnknownRegionResolver : IRegionResolver
    {
        public Task<(RegionCode Region, RegionSource Source)> Resolve(ActorRef actor, CancellationToken ct = default) =>
            Task.FromResult((RegionCode.Unknown, RegionSource.EdgeInferred));
    }

    private sealed class StubAccessor(RequestContext ctx) : IRequestContextAccessor
    {
        public RequestContext Current { get; } = ctx;
    }

    private sealed class CapturingPolicyEngine : IPolicyEngine
    {
        public TargetRef? LastTarget { get; private set; }

        public Task<PolicyDecision> Authorize(ActorRef actor, string action, TargetRef target, CancellationToken ct = default)
        {
            LastTarget = target;
            return Task.FromResult(PolicyDecision.Allowed);
        }
    }
}
