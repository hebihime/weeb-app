using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Region;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// Builds the RequestContext BEFORE any module code runs (SLICE_S1_CONTRACT.md §1b) and stamps it into
/// the ambient accessor. S1 ships zero authenticated principals (S3 is identity); every request at S1
/// resolves an anonymous ActorRef. Region is NEVER client-settable (L20) — resolved via IRegionResolver,
/// never read from a request header/body.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next, AmbientRequestContextAccessor accessor, IRegionResolver regionResolver)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        var correlationId = httpContext.TraceIdentifier;
        var anonymousActor = new ActorRef(OpaqueId.Parse(BuildAnonymousId(correlationId)), ActorKind.Anonymous);
        var (region, regionSource) = await regionResolver.Resolve(anonymousActor, httpContext.RequestAborted);

        var ctx = new RequestContext(
            anonymousActor,
            region,
            regionSource,
            LawfulBasisVariant.ConservativeGlobalV0,
            ResolveLocale(httpContext),
            correlationId);

        accessor.Set(ctx);
        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;

        await next(httpContext);
    }

    // Deterministic, request-scoped placeholder id: real per-request minting (clock + randomness) is a
    // Phase-2 concern once a real caller exists; this middleware only needs a stable, parseable shape.
    private static string BuildAnonymousId(string correlationId)
    {
        var stableSuffix = Math.Abs(correlationId.GetHashCode()).ToString("D10", System.Globalization.CultureInfo.InvariantCulture).PadLeft(26, '0')[..26];
        var crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var chars = stableSuffix.Select(c => crockford[(c - '0') % crockford.Length]);
        return $"{IdPrefixes.Anonymous}_{new string(chars.ToArray())}";
    }

    private static string ResolveLocale(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers.AcceptLanguage.ToString();
        return string.IsNullOrWhiteSpace(header) ? "en" : header.Split(',')[0].Split(';')[0].Trim();
    }
}
