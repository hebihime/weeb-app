using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Svac.AdminHost.Domain.Auth;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;

namespace Svac.AdminHost.Auth;

/// <summary>
/// The staff-auth HTTP surface (SLICE_S5_CONTRACT.md §1a/§1b): real endpoints, never Blazor-infra-only,
/// so RequireMutationsPolicyMapped stays meaningful here exactly as on Svac.PublicApi (§1a). All three
/// endpoints carry the SAME "admin.host.transport" row (Staff+Anonymous, DenyAsAbsence) the Razor
/// Component mapping already uses in Program.cs — pre-auth reachability is ONE row, never duplicated.
/// </summary>
public static class StaffAuthEndpointExtensions
{
    public static WebApplication MapStaffAuthEndpoints(this WebApplication app, bool devSeamsEnabled, bool entraConfigured)
    {
        if (devSeamsEnabled)
        {
            app.MapPost("/devseams/signin/{fixtureKey}", HandleDevSeamsSignIn)
                .RequirePolicyAction("admin.host.transport");
        }

        if (entraConfigured)
        {
            app.MapGet("/staffauth/challenge", HandleEntraChallenge)
                .RequirePolicyAction("admin.host.transport");
        }

        // Explicit (Delegate) cast: a handler shaped EXACTLY (HttpContext) -> Task<IResult> is also
        // structurally assignable to RequestDelegate (Task<IResult> IS-A Task) — without this cast, the
        // compiler resolves the OTHER MapPost overload (string, RequestDelegate), which silently
        // discards the IResult (ASP0016) and returns IEndpointConventionBuilder, not RouteHandlerBuilder.
        app.MapPost("/staffauth/signout", (Delegate)HandleSignOut)
            .RequirePolicyAction("admin.host.transport");

        return app;
    }

    private static async Task<IResult> HandleDevSeamsSignIn(
        string fixtureKey,
        DevSeamsStaffTransport transport,
        StaffSignInPipeline pipeline,
        IStaffContextProvider contextProvider,
        AdminDbContext adminDb,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var claims = await transport.ResolveClaims(fixtureKey, ct);
        if (claims is null)
        {
            return Results.Redirect("/?refused=unknown_fixture");
        }

        var result = await pipeline.SignIn(claims, contextProvider.ForStaffOperation(), ct);
        if (result is not StaffSignInResult.Allowed allowed)
        {
            var reason = result switch
            {
                StaffSignInResult.RefusedNoMfa => "no_mfa",
                StaffSignInResult.RefusedUnknownSubject => "unknown_subject",
                StaffSignInResult.RefusedInactiveAccount => "inactive_account",
                _ => "unknown",
            };
            return Results.Redirect($"/?refused={reason}");
        }

        var principal = await StaffSignInFlow.BuildCookiePrincipal(allowed, adminDb, StaffAuthServiceCollectionExtensions.CookieScheme, ct);
        await httpContext.SignInAsync(StaffAuthServiceCollectionExtensions.CookieScheme, principal);
        return Results.Redirect("/dashboard");
    }

    private static IResult HandleEntraChallenge() =>
        Results.Challenge(properties: new AuthenticationProperties { RedirectUri = "/dashboard" }, authenticationSchemes: new[] { StaffAuthServiceCollectionExtensions.OidcScheme });

    private static async Task<IResult> HandleSignOut(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(StaffAuthServiceCollectionExtensions.CookieScheme);
        return Results.Redirect("/");
    }
}
