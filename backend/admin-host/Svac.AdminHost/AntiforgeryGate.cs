using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Svac.AdminHost;

/// <summary>
/// SECURITY_REVIEW_S5.md S5-11 fix — the one place every staff mutation POST validates its antiforgery
/// token for real. Before this fix, <see cref="Svac.AdminHost.ConfigRegistry.ConfigRegistryEndpointExtensions"/>,
/// <see cref="Svac.AdminHost.Staff.StaffRolesEndpointExtensions"/>, and
/// <see cref="Svac.AdminHost.Auth.StaffAuthEndpointExtensions"/>'s DevSeams sign-in handler all read the
/// <c>__RequestVerificationToken</c> field off <c>Request.Form</c> by hand and never called
/// <see cref="IAntiforgery.ValidateRequestAsync(HttpContext)"/> — <c>app.UseAntiforgery()</c> alone never
/// retroactively validates a hand-read form (the middleware only auto-validates endpoints whose metadata
/// marks them as requiring it, which minimal-API handlers that bind <c>HttpContext</c> directly instead of
/// a <c>[FromForm]</c> parameter never get). Mitigated only by <c>SameSite=Lax</c> until now.
///
/// One shared helper so all eight mutation handlers (2 config + 5 staff + 1 sign-in) call the SAME real
/// validation the SAME way, rather than eight independent copies drifting out of sync.
/// </summary>
internal static class AntiforgeryGate
{
    /// <summary>
    /// Returns <c>true</c> iff the request carries a valid antiforgery token pair (cookie + form/header
    /// field) for THIS session. Swallows only <see cref="AntiforgeryValidationException"/> — any other
    /// exception (a genuine bug) still propagates, exactly as it would from any other awaited call in
    /// these handlers.
    /// </summary>
    public static async Task<bool> IsValid(IAntiforgery antiforgery, HttpContext httpContext)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
