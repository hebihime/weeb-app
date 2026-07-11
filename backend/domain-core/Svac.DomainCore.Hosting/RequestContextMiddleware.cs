using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Region;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// Builds the RequestContext BEFORE any module code runs (SLICE_S1_CONTRACT.md §1b) and stamps it into
/// the ambient accessor. Region is NEVER client-settable (L20) — resolved via IRegionResolver, never read
/// from a request header/body.
///
/// PHASE_2A_SUBSTRATE.md §4: calls <see cref="IBearerAuthenticator"/> FIRST. The default registration
/// (<see cref="AnonymousBearerAuthenticator"/>) always returns null, so every S1/S2 request still takes
/// the exact anonymous-actor path below, byte-identical — BuildAnonymousId is untouched verbatim. Only a
/// real (non-null) resolution folds AccountState + optional region/locale overrides into the context.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next, AmbientRequestContextAccessor accessor, IRegionResolver regionResolver, IBearerAuthenticator? bearerAuthenticator = null)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        var correlationId = httpContext.TraceIdentifier;
        // bearerAuthenticator is optional (defaults null) so existing direct-construction call sites
        // (tests predating PHASE_2A_SUBSTRATE.md §4) stay byte-identical without edits — a null
        // authenticator behaves exactly like the registered AnonymousBearerAuthenticator default.
        var authenticated = bearerAuthenticator is null ? null : await bearerAuthenticator.Authenticate(httpContext, httpContext.RequestAborted);

        RequestContext ctx;
        if (authenticated is null)
        {
            var anonymousActor = new ActorRef(OpaqueId.Parse(BuildAnonymousId(correlationId)), ActorKind.Anonymous);
            var (region, regionSource) = await regionResolver.Resolve(anonymousActor, httpContext.RequestAborted);

            ctx = new RequestContext(
                anonymousActor,
                region,
                regionSource,
                LawfulBasisVariant.ConservativeGlobalV0,
                ResolveLocale(httpContext),
                correlationId);
        }
        else
        {
            var (region, regionSource) = authenticated.Region is not null
                ? (authenticated.Region.Value, RegionSource.Declared)
                : await regionResolver.Resolve(authenticated.Actor, httpContext.RequestAborted);

            ctx = new RequestContext(
                authenticated.Actor,
                region,
                regionSource,
                LawfulBasisVariant.ConservativeGlobalV0,
                authenticated.Locale ?? ResolveLocale(httpContext),
                correlationId,
                AccountState: authenticated.AccountState,
                Staff: null);
        }

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
