using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace Svac.Identity.Endpoints;

/// <summary>
/// Builds the host's <c>UseForwardedHeaders</c> trust configuration (SECURITY_REVIEW_S3.md OPS-1).
///
/// Behind Azure Container Apps' Envoy ingress, every request's transport-level source address is the
/// ingress proxy, not the real caller — so <see cref="IdentityRateLimiting"/>'s per-IP partition key
/// (<c>httpContext.Connection.RemoteIpAddress</c>) collapses the whole anonymous funnel into ONE bucket
/// unless forwarded-headers processing rewrites that address first. But the naive fix
/// (<c>app.UseForwardedHeaders()</c> with its ASP.NET defaults) is worse: it is documented to trust
/// <c>X-Forwarded-For</c> from any peer that presents it, which lets any client hand the limiter (and
/// anything else that reads <c>RemoteIpAddress</c>) whatever partition key it likes — a spoofable bypass.
///
/// The correct trust set is EXACTLY the ACA ingress's own address(es)/subnet — nothing wider. That subnet
/// is infra config (CLAUDE.md 2A), not a compile-time constant, so it is supplied via
/// <c>SVAC_ACA_INGRESS_CIDRS</c> (comma-separated CIDR list) at host startup.
///
/// <b>ASP.NET Core gotcha, discovered by this fix's own test (ForwardedHeadersTrustTests):</b> merely
/// clearing <see cref="ForwardedHeadersOptions.KnownIPNetworks"/>/<c>KnownProxies</c> to empty does NOT
/// mean "trust nobody" — <c>ForwardedHeadersMiddleware</c> treats BOTH lists being empty as "no
/// restriction configured," and honors <c>X-Forwarded-For</c> from ANY peer. An empty allow-list is
/// exactly the naive-trust-everyone bug this fix exists to avoid, just reached by a different path. So
/// with NOTHING configured, <see cref="Build"/> instead sets <see cref="ForwardedHeadersOptions.ForwardedHeaders"/>
/// to <see cref="ForwardedHeaders.None"/> — the middleware then does not process <c>X-Forwarded-For</c> at
/// all, regardless of who presents it. That is inert (Finding 1's original bug: RemoteIpAddress stays the
/// ingress proxy's own address) rather than spoofable, which is the safer of the two failure modes for a
/// not-yet-configured deploy.
///
/// <b>Before shipping to any real ACA environment, <c>SVAC_ACA_INGRESS_CIDRS</c> MUST be set to the
/// ingress's actual subnet(s)</b> — this code cannot know that value, and ships fail-closed (inert) until
/// it is supplied.
/// </summary>
public static class ForwardedHeadersConfiguration
{
    public static ForwardedHeadersOptions Build(string? trustedProxyCidrs)
    {
        var networks = ParseCidrs(trustedProxyCidrs).ToList();

        var options = new ForwardedHeadersOptions
        {
            // Nothing configured -> None, never XForwardedFor-with-an-empty-allow-list (see the gotcha
            // documented above) — the middleware then leaves RemoteIpAddress untouched no matter what any
            // peer sends.
            ForwardedHeaders = networks.Count > 0 ? ForwardedHeaders.XForwardedFor : ForwardedHeaders.None,
            // Exactly one hop trusted (the ingress itself) — never chain-trust past the immediate proxy,
            // so a caller cannot prepend extra XFF entries to walk the trust boundary further than the one
            // real hop ACA inserts.
            ForwardLimit = 1,
        };

        // Clear the framework defaults (KnownProxies seeds loopback) rather than layering onto them — the
        // trust set is EXACTLY what SVAC_ACA_INGRESS_CIDRS names, never "that, plus whatever ASP.NET
        // trusted by default." Harmless to populate even when ForwardedHeaders is None above.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }

        return options;
    }

    internal static IEnumerable<IPNetwork> ParseCidrs(string? trustedProxyCidrs)
    {
        if (string.IsNullOrWhiteSpace(trustedProxyCidrs))
        {
            yield break;
        }

        foreach (var raw in trustedProxyCidrs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return IPNetwork.Parse(raw);
        }
    }
}
