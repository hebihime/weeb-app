using System.Security.Claims;
using Svac.AdminHost.Domain.Auth;
using Svac.AdminHost.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Svac.AdminHost.Auth;

/// <summary>
/// The ONE post-pipeline step both transports share (SLICE_S5_CONTRACT.md §1b): once
/// <see cref="StaffSignInPipeline"/> returns <see cref="StaffSignInResult.Allowed"/>, build the cookie's
/// <see cref="ClaimsPrincipal"/> from the SAME claim contract (<see cref="StaffClaimTypes"/>) regardless
/// of whether the caller was <c>DevSeamsStaffTransport</c> or the Entra OIDC callback.
/// </summary>
public static class StaffSignInFlow
{
    public static async Task<ClaimsPrincipal> BuildCookiePrincipal(StaffSignInResult.Allowed allowed, AdminDbContext adminDb, string authenticationScheme, CancellationToken ct = default)
    {
        var staffId = allowed.Staff.Id.ToString();
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId, ct);

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(StaffClaimTypes.StaffId, row.Id),
                new Claim(StaffClaimTypes.SecurityStamp, row.SecurityStamp),
                new Claim(StaffClaimTypes.Region, row.Region),
                new Claim(ClaimTypes.Name, row.DisplayName),
            },
            authenticationScheme);

        return new ClaimsPrincipal(identity);
    }
}
