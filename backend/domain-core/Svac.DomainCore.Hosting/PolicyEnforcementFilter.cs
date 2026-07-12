using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// Endpoint metadata carrying the <see cref="PolicyTargetBinding"/> an endpoint declared (PHASE_2A_
/// SUBSTRATE.md §1/§4). A plain POCO, not an <see cref="Attribute"/> — <see cref="PolicyTargetBinding"/>
/// carries data (FromRoute's paramName/resourceType), which an Attribute constructor cannot accept
/// (attribute arguments must be compile-time constants). Minimal-API's <c>WithMetadata(object)</c> accepts
/// any object, so this rides the same endpoint-metadata collection <see cref="PolicyActionAttribute"/>
/// does. Read by <see cref="StartupPolicyCoverage"/>'s target-binding boot-refusal checks.
/// </summary>
public sealed class PolicyTargetBindingMetadata(PolicyTargetBinding binding)
{
    public PolicyTargetBinding Binding { get; } = binding;
}

/// <summary>
/// The 4A request-time chokepoint (SLICE_S1_CONTRACT.md §1b, §3; PHASE_2A_SUBSTRATE.md §4): for the
/// endpoint it is attached to, resolves the current actor + calls IPolicyEngine.Authorize for the given
/// action, and refuses closed on anything but Allow. Attach via <see
/// cref="PolicyEndpointExtensions.RequirePolicyAction"/>, never by hand — that extension also stamps the
/// PolicyActionAttribute (and, for the new overload, PolicyTargetBindingMetadata) the boot-refusal check
/// looks for.
///
/// PHASE_2A_SUBSTRATE.md §4: the target binding resolves the REAL TargetRef the row is authorized
/// against — <see cref="PolicyTargetBinding.NoneBinding"/> (the default, byte-identical) still calls
/// <c>TargetRef.ForAction(action)</c>; <see cref="PolicyTargetBinding.SelfAccountBinding"/> conveys the
/// caller's own actor id (never route/body/client input); <see
/// cref="PolicyTargetBinding.FromRouteBinding"/> conveys a real resource id read out of the matched
/// route. Ownership resolution itself happens INSIDE the engine via the registered
/// IResourceOwnershipResolver — this filter only ever resolves the TargetRef shape.
/// </summary>
public sealed class PolicyEnforcementFilter(string action, PolicyTargetBinding? binding = null) : IEndpointFilter
{
    private readonly PolicyTargetBinding _binding = binding ?? PolicyTargetBinding.None;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var requestContextAccessor = services.GetRequiredService<IRequestContextAccessor>();
        var policyEngine = services.GetRequiredService<IPolicyEngine>();

        var ctx = requestContextAccessor.Current;
        var target = ResolveTarget(action, _binding, ctx, context.HttpContext);
        var decision = await policyEngine.Authorize(ctx.Actor, action, target, context.HttpContext.RequestAborted);

        return decision switch
        {
            PolicyDecision.Allow => await next(context),
            PolicyDecision.DenyAsAbsence => PolicyResults.NotFoundAbsence(),
            PolicyDecision.DenySilentAs404 => PolicyResults.NotFoundAbsence(),
            PolicyDecision.DenyAsLimit limit => PolicyResults.LimitReached(BuildPlaceholderLimitReached(limit.QuotaKey)),
            PolicyDecision.DenyStandard standard => PolicyResults.Standard(standard.ReasonKey, ctx.CorrelationId),
            _ => throw new InvalidOperationException($"Unhandled PolicyDecision case: {decision.GetType().Name}"),
        };
    }

    /// <summary>Resolves the TargetRef a binding conveys — the endpoint-side half of PHASE_2A_SUBSTRATE.md §4's Auth-F3 redesign.</summary>
    private static TargetRef ResolveTarget(string action, PolicyTargetBinding binding, RequestContext ctx, HttpContext httpContext) => binding switch
    {
        PolicyTargetBinding.NoneBinding => TargetRef.ForAction(action),
        PolicyTargetBinding.SelfAccountBinding => new TargetRef("account", ctx.Actor.Id.ToString()),
        PolicyTargetBinding.FromRouteBinding fromRoute => new TargetRef(
            fromRoute.ResourceType,
            httpContext.Request.RouteValues.TryGetValue(fromRoute.ParamName, out var value) ? value?.ToString() : null),
        _ => throw new InvalidOperationException($"Unhandled PolicyTargetBinding case: {binding.GetType().Name}"),
    };

    // Phase-1 scaffold: the real resets_at/premium_extends come from IQuotaService (§5), which the
    // filter does not call directly (Authorize and Consume are separate verbs, §1b). A quota-gated
    // endpoint calls IQuotaService.Consume itself and renders its own LimitReached; this fallback only
    // covers a bare DenyAsLimit policy row exercised without a live quota call (e.g. a unit test).
    private static Svac.DomainCore.Contracts.Api.LimitReached BuildPlaceholderLimitReached(string quotaKey) => new(
        QuotaKey: quotaKey,
        MessageKey: Svac.DomainCore.Contracts.Api.MessageKeys.LimitReachedGeneric,
        ResetsAt: DateTimeOffset.UtcNow.AddDays(1),
        PremiumExtends: false);
}

/// <summary>Minimal-API mapping sugar that wires both the boot-refusal metadata and the request-time filter in one call.</summary>
public static class PolicyEndpointExtensions
{
    /// <summary>Existing no-arg overload = <see cref="PolicyTargetBinding.None"/> (byte-identical, SLICE_S1_CONTRACT.md §3).</summary>
    public static RouteHandlerBuilder RequirePolicyAction(this RouteHandlerBuilder builder, string action) =>
        builder.RequirePolicyAction(action, PolicyTargetBinding.None);

    /// <summary>[S3, PHASE_2A_SUBSTRATE.md §4] The target-binding overload — conveys a real resource target per <see cref="PolicyTargetBinding"/>.</summary>
    public static RouteHandlerBuilder RequirePolicyAction(this RouteHandlerBuilder builder, string action, PolicyTargetBinding binding) =>
        builder
            .WithMetadata(new PolicyActionAttribute(action))
            .WithMetadata(new PolicyTargetBindingMetadata(binding))
            .AddEndpointFilter(new PolicyEnforcementFilter(action, binding));
}
