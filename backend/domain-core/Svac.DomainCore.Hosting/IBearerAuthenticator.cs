using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Hosting;

/// <summary>
/// What a real bearer-token resolution produces (PHASE_2A_SUBSTRATE.md §4, SLICE_S3_CONTRACT.md §1b).
/// Region/Locale are optional overrides of the request's own resolution — null means "defer to the
/// existing IRegionResolver / Accept-Language logic", never "force unknown".
/// </summary>
public sealed record AuthenticatedActor(ActorRef Actor, string? AccountState, RegionCode? Region, string? Locale);

/// <summary>
/// The bearer-resolution Hosting seam (PHASE_2A_SUBSTRATE.md §4, SLICE_S3_CONTRACT.md §1b: "one
/// chokepoint, three credential systems by design"). <see cref="RequestContextMiddleware"/> calls this
/// FIRST; a null result means "resolve anonymous the same way S1/S2 always have" (byte-identical). A
/// non-null result folds <see cref="AuthenticatedActor.AccountState"/> + optional region/locale into the
/// built <see cref="RequestContext"/>. Identity registers the session-backed resolver (S3 build); the
/// admin host registers its cookie-auth resolver (S5 build); partner arrives later (S29). One middleware,
/// three credential systems.
/// </summary>
public interface IBearerAuthenticator
{
    public Task<AuthenticatedActor?> Authenticate(HttpContext httpContext, CancellationToken ct = default);
}

/// <summary>
/// The default registration (PHASE_2A_SUBSTRATE.md §4: "default registered in AddSvacHosting =
/// AnonymousBearerAuthenticator"). Always returns null — every request resolves anonymous exactly as
/// S1/S2 always have, until a host registers a real resolver.
/// </summary>
public sealed class AnonymousBearerAuthenticator : IBearerAuthenticator
{
    public Task<AuthenticatedActor?> Authenticate(HttpContext httpContext, CancellationToken ct = default) =>
        Task.FromResult<AuthenticatedActor?>(null);
}
