using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// The fail-closed-at-startup half of 4A (SLICE_S1_CONTRACT.md §3): the host refuses to boot if any
/// non-GET/HEAD endpoint lacks a <see cref="PolicyActionAttribute"/> mapping to a table row. B1's
/// boot-refusal proof: an arch test maps an unmapped canary POST onto a TestHost carrying this check and
/// asserts startup throws.
/// </summary>
public static class StartupPolicyCoverage
{
    private static readonly HashSet<string> MutationMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>
    /// Call once, after every endpoint is mapped and before <c>app.Run()</c>. Throws
    /// <see cref="InvalidOperationException"/> naming every offending endpoint if any exist.
    /// </summary>
    public static WebApplication RequireMutationsPolicyMapped(this WebApplication app)
    {
        var policyTable = app.Services.GetRequiredService<IPolicyTable>();

        // WebApplication's own DataSources (populated directly by MapGet/MapPost/etc., since
        // WebApplication implements IEndpointRouteBuilder) — NOT the DI-registered EndpointDataSource
        // singleton, which stays an empty CompositeEndpointDataSource until routing middleware actually
        // runs a request through the pipeline. Reading DataSources works before the app ever accepts a
        // request, which is exactly when this fail-closed check must run.
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(ds => ds.Endpoints);

        var unmapped = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var methodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
            // Auth-F2 (SECURITY_REVIEW_S1.md): a catch-all `Map(pattern, handler)` route matches EVERY
            // verb (POST/PUT/DELETE included) but carries NO HttpMethodMetadata at all — topology, not an
            // HTTP-method allowlist, is what makes it reachable by a mutation. Treating a null metadata as
            // "not a mutation" let exactly that endpoint bypass the boot-refusal fail-closed check ("is
            // this a mutation?" must never resolve to "no" by default). Null now means "no declared verb
            // restriction", i.e. mutation-capable — the only endpoints that legitimately skip this gate
            // are ones that DECLARE themselves GET/HEAD-only, which is checked below.
            var isMutation = methodMetadata is null || methodMetadata.HttpMethods.Any(m => MutationMethods.Contains(m));
            if (!isMutation)
            {
                continue;
            }

            var policyAction = endpoint.Metadata.GetMetadata<PolicyActionAttribute>();
            if (policyAction is null)
            {
                unmapped.Add(endpoint.DisplayName ?? endpoint.ToString() ?? "<unnamed endpoint>");
                continue;
            }

            if (policyTable.Find(policyAction.Action) is null)
            {
                unmapped.Add($"{endpoint.DisplayName} (action \"{policyAction.Action}\" has no PolicyTable row)");
            }
        }

        if (unmapped.Count > 0)
        {
            throw new InvalidOperationException(
                "4A boot refusal: the following mutation endpoint(s) have no [PolicyAction] mapped to a " +
                "PolicyTable row (SLICE_S1_CONTRACT.md §3 — a consumer-reachable, policy-less mutation is a " +
                $"contract violation, not a gap):\n  - {string.Join("\n  - ", unmapped)}");
        }

        return app;
    }

    /// <summary>
    /// [S3, PHASE_2A_SUBSTRATE.md §1/§3a] Fail-closed BOTH directions on the target-binding/TargetRule
    /// pairing, red-fixture-proven: (a) a row demanding SelfOnly/OwnedResource whose endpoint conveys
    /// PolicyTargetBinding.None refuses to boot; (b) a FromRoute binding naming a route parameter absent
    /// from the endpoint's own route pattern refuses to boot; (c) the reverse — an ActionScoped row given
    /// a non-None binding — also refuses. A resource-scoped action without a correctly-wired target
    /// conveyance is structurally unshippable, forever. Call AFTER <see cref="RequireMutationsPolicyMapped"/>
    /// (that check already proves every mutation endpoint has a mapped, table-registered action) — this
    /// check is a no-op for every S1/S2 endpoint (every real row today is ActionScoped + binds None).
    /// </summary>
    public static WebApplication RequireTargetBindingConsistent(this WebApplication app)
    {
        var policyTable = app.Services.GetRequiredService<IPolicyTable>();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(ds => ds.Endpoints);

        var violations = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var policyAction = endpoint.Metadata.GetMetadata<PolicyActionAttribute>();
            if (policyAction is null)
            {
                continue; // no [PolicyAction] at all — RequireMutationsPolicyMapped's job, not this one.
            }

            var entry = policyTable.Find(policyAction.Action);
            if (entry is null)
            {
                continue; // action has no table row — RequireMutationsPolicyMapped's job, not this one.
            }

            var binding = endpoint.Metadata.GetMetadata<PolicyTargetBindingMetadata>()?.Binding ?? PolicyTargetBinding.None;
            var demandsTarget = entry.TargetRule is TargetRule.SelfOnlyRule or TargetRule.OwnedResourceRule;
            var conveysTarget = binding is not PolicyTargetBinding.NoneBinding;
            var name = endpoint.DisplayName ?? endpoint.ToString() ?? "<unnamed endpoint>";

            if (demandsTarget && !conveysTarget)
            {
                violations.Add($"{name} (action \"{policyAction.Action}\" declares SelfOnly/OwnedResource but the endpoint binds PolicyTargetBinding.None)");
                continue;
            }

            if (!demandsTarget && conveysTarget)
            {
                violations.Add($"{name} (action \"{policyAction.Action}\" is ActionScoped/unset but the endpoint conveys a non-None target binding)");
                continue;
            }

            if (binding is PolicyTargetBinding.FromRouteBinding fromRoute)
            {
                var routePattern = (endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern;
                var paramExists = routePattern?.Parameters.Any(p => p.Name == fromRoute.ParamName) ?? false;
                if (!paramExists)
                {
                    violations.Add($"{name} (FromRoute names route parameter \"{fromRoute.ParamName}\", which is absent from the endpoint's own route pattern)");
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                "4A boot refusal: target-binding / TargetRule mismatch (PHASE_2A_SUBSTRATE.md §1/§3a — a " +
                "resource-scoped action without correct target conveyance is structurally unshippable):\n  - " +
                string.Join("\n  - ", violations));
        }

        return app;
    }
}
