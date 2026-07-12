using Microsoft.AspNetCore.Http;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Hosting;

namespace Svac.AdminHost.Auth;

/// <summary>
/// The admin host's <see cref="IBearerAuthenticator"/> registration (PHASE_2A_SUBSTRATE.md §4,
/// SLICE_S5_CONTRACT.md §1b: "one middleware, three credential systems by design" — identity registers
/// the session-backed resolver; the admin host registers THIS one; partner arrives later). Deliberately
/// thin: the cookie's own <c>OnValidatePrincipal</c> event (wired in <see
/// cref="StaffAuthServiceCollectionExtensions"/>) already re-validated stamp+status at the configured
/// revalidation interval BEFORE <c>httpContext.User</c> is populated for this request — this type only
/// translates an already-trusted <see cref="System.Security.Claims.ClaimsPrincipal"/> into the
/// <see cref="AuthenticatedActor"/> shape <c>RequestContextMiddleware</c> folds into the ambient
/// <see cref="RequestContext"/>. An unauthenticated request (no cookie, or a cookie the handler already
/// rejected) resolves null → the exact anonymous-actor path S1/S2 always took, byte-identical.
/// </summary>
public sealed class StaffCookieBearerAuthenticator : IBearerAuthenticator
{
    public Task<AuthenticatedActor?> Authenticate(HttpContext httpContext, CancellationToken ct = default)
    {
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult<AuthenticatedActor?>(null);
        }

        var staffIdClaim = user.FindFirst(StaffClaimTypes.StaffId)?.Value;
        if (staffIdClaim is null || !OpaqueId.TryParse(staffIdClaim, out var staffId) || staffId.Prefix != IdPrefixes.Staff)
        {
            return Task.FromResult<AuthenticatedActor?>(null);
        }

        var regionClaim = user.FindFirst(StaffClaimTypes.Region)?.Value;
        var region = !string.IsNullOrWhiteSpace(regionClaim) ? new RegionCode(regionClaim, null) : (RegionCode?)null;

        // AccountState mirrors identity's own convention (a plain string the policy engine's
        // AllowedAccountStates axis can compare against) — "active" is the only value that ever reaches
        // here because a deactivated/stamp-mismatched cookie was already rejected by OnValidatePrincipal.
        return Task.FromResult<AuthenticatedActor?>(new AuthenticatedActor(
            new ActorRef(staffId, ActorKind.Staff),
            AccountState: "active",
            Region: region,
            Locale: null));
    }
}
