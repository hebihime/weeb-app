using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// The 4A request-time chokepoint (SLICE_S1_CONTRACT.md §1b, §3): for the endpoint it is attached to,
/// resolves the current actor + calls IPolicyEngine.Authorize for the given action, and refuses closed
/// on anything but Allow. Attach via <see cref="PolicyEndpointExtensions.RequirePolicyAction"/>, never
/// by hand — that extension also stamps the PolicyActionAttribute the boot-refusal check looks for.
/// </summary>
public sealed class PolicyEnforcementFilter(string action) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var requestContextAccessor = services.GetRequiredService<IRequestContextAccessor>();
        var policyEngine = services.GetRequiredService<IPolicyEngine>();

        var ctx = requestContextAccessor.Current;
        var decision = policyEngine.Authorize(ctx.Actor, action, TargetRef.ForAction(action));

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
    public static RouteHandlerBuilder RequirePolicyAction(this RouteHandlerBuilder builder, string action) =>
        builder.WithMetadata(new PolicyActionAttribute(action)).AddEndpointFilter(new PolicyEnforcementFilter(action));
}
