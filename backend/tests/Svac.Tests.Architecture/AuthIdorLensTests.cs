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
    // FINDING 3 — IDOR is structurally unreachable through the 4A chokepoint.
    //
    // PolicyEnforcementFilter.cs:23 always calls Authorize with TargetRef.ForAction(action) — a synthetic
    // target whose ResourceId is ALWAYS null (ActorRef.cs:29). The route's real resource id (e.g. the {id}
    // of /profiles/{id}) never reaches IPolicyEngine.Authorize, whose signature takes a TargetRef exactly
    // so it can enforce "actor owns THIS object". Result: any endpoint mounting RequirePolicyAction gets
    // action-level authorization only, never object-level — the textbook IDOR gap, baked into the one
    // chokepoint every future mutation is required to use.
    // ---------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S1.md Auth-F3 — chokepoint target-binding API lands with the first consumer resource endpoint (S2); S1 ships zero consumer resource endpoints so nothing is exploitable today.")]
    public async Task PolicyChokepoint_MustConveyTheRealTargetResourceId()
    {
        var spy = new CapturingPolicyEngine();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContextAccessor>(new StubAccessor(AnonymousContext()));
        services.AddSingleton<IPolicyEngine>(spy);
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        // Simulate a route like /profiles/{id} bound to victim's id "usr_..." — the value an IDOR attacker varies.
        var routeResourceId = "usr_01HZZZZZZZZZZZZZZZZZZZZZZZ";
        var filterContext = EndpointFilterInvocationContext.Create(httpContext, routeResourceId);

        var filter = new PolicyEnforcementFilter("core.profile.update");
        await filter.InvokeAsync(filterContext, _ => ValueTask.FromResult<object?>(Results.Ok()));

        Assert.NotNull(spy.LastTarget);
        // The break: the chokepoint discards the resource id, so ownership can never be checked here.
        Assert.Equal(routeResourceId, spy.LastTarget!.Value.ResourceId);
    }

    // --- helpers -------------------------------------------------------------------------------

    private static WebApplication BuildMinimalApp()
    {
        var builder = WebApplication.CreateBuilder();
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

        public PolicyDecision Authorize(ActorRef actor, string action, TargetRef target)
        {
            LastTarget = target;
            return PolicyDecision.Allowed;
        }
    }
}
