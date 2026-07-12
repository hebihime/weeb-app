using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.Identity.Auth;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Endpoints;

/// <summary>The auth/* routes (SLICE_S3_CONTRACT.md §1c) + `POST /v1/auth/logout`.</summary>
public static class AuthEndpoints
{
    private static readonly TimeSpan AntiEnumerationFloor = TimeSpan.FromMilliseconds(60);
    private const string BearerPrefix = "Bearer ";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/auth/email-code", PostAuthEmailCode)
            .WithName("PostAuthEmailCode")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .RequirePolicyAction("identity.auth.request_code")
            .Produces(StatusCodes.Status202Accepted);

        app.MapPost("/v1/auth/session", PostAuthSession)
            .WithName("PostAuthSession")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .RequirePolicyAction("identity.auth.create_session")
            .Produces<Svac.Identity.Contracts.SessionCreated>(StatusCodes.Status200OK)
            .Produces<Problem>(StatusCodes.Status400BadRequest);

        app.MapPost("/v1/auth/refresh", PostAuthRefresh)
            .WithName("PostAuthRefresh")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .RequirePolicyAction("identity.auth.refresh")
            .Produces<Svac.Identity.Contracts.SessionCreated>(StatusCodes.Status200OK)
            .Produces<Problem>(StatusCodes.Status400BadRequest);

        app.MapPost("/v1/auth/logout", PostAuthLogout)
            .WithName("PostAuthLogout")
            .RequirePolicyAction("identity.auth.logout", PolicyTargetBinding.SelfAccount)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> PostAuthEmailCode(
        [FromBody] AuthEmailCodeRequest request,
        [FromServices] EmailChallengeMachine challenges,
        [FromServices] IRequestContextAccessor requestContext,
        [FromServices] ClientConfigResponse clientConfig,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        if (EmailInput.TryNormalize(request.Email, out var emailLower))
        {
            await challenges.IssueForLogin(emailLower, clientConfig.DefaultLocale, requestContext.Current, ct);
        }
        // Uniform 202 {} whether the account exists, is banned, is absent, or the email shape was
        // malformed (§1c) — the input-shape branch above is the ONLY early-return-free path: reaching
        // this line always renders the identical body.
        await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
        return Results.Json(new { }, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> PostAuthSession(
        [FromBody] AuthSessionRequest request,
        [FromServices] EmailChallengeMachine challenges,
        [FromServices] IdentityDbContext db,
        [FromServices] IConfigRegistry config,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        // MAIL-1 (SECURITY_REVIEW_S3.md, AUTH lens Finding 2): login step 2 had no floor at all — a
        // pending login challenge only exists for a live, non-banned account that just requested a code,
        // so the HMAC-compute-and-attempt-write delta on the "challenge pending" branch vs the immediate
        // "no challenge" rollback (RedeemLoginCode's own dummy-HMAC equalization, above) is floored here
        // exactly like PostAuthEmailCode already floors step 1.
        var stopwatch = Stopwatch.StartNew();
        var ctx = requestContext.Current;
        if (!EmailInput.TryNormalize(request.Email, out var emailLower) || string.IsNullOrWhiteSpace(request.Code))
        {
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return InvalidCodeProblem(ctx);
        }

        var accountId = await challenges.RedeemLoginCode(emailLower, request.Code, ct);
        if (accountId is null)
        {
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return InvalidCodeProblem(ctx);
        }

        var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId, ct);
        var session = await SessionIssuance.IssueAsync(
            db, accountId, deviceId: null, account.Region, account.LawfulBasis,
            await config.GetValue<int>(IdentityConfigKeys.SessionAccessTtlMinutes, ct),
            await config.GetValue<int>(IdentityConfigKeys.SessionRefreshTtlDays, ct),
            await config.GetValue<int>(IdentityConfigKeys.SessionMaxActivePerAccount, ct),
            ct);

        await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
        return Results.Ok(new Svac.Identity.Contracts.SessionCreated(session.AccessToken, session.AccessExpiresAt, session.RefreshToken, OpaqueId.Parse(accountId)));
    }

    private static async Task<IResult> PostAuthRefresh(
        [FromBody] AuthRefreshRequest request,
        [FromServices] RefreshRotationService rotation,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return GenericAuthProblem(ctx);
        }

        var outcome = await rotation.Rotate(request.RefreshToken, ctx, ct);
        return outcome switch
        {
            RefreshOutcome.RotatedResult rotated => Results.Ok(new Svac.Identity.Contracts.SessionCreated(
                rotated.Session.AccessToken, rotated.Session.AccessExpiresAt, rotated.Session.RefreshToken, OpaqueId.Parse(rotated.AccountId))),
            _ => GenericAuthProblem(ctx),
        };
    }

    private static async Task<IResult> PostAuthLogout(
        HttpContext httpContext,
        [FromServices] IdentityDbContext db,
        CancellationToken ct)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(header) && header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            var token = header[BearerPrefix.Length..].Trim();
            if (token.Length > 0)
            {
                var hash = SessionTokens.HashAccessToken(token);
                var session = await db.Sessions.SingleOrDefaultAsync(s => s.AccessTokenHash == hash && s.RevokedAt == null, ct);
                if (session is not null)
                {
                    session.RevokedAt = DateTimeOffset.UtcNow;
                    session.RevokeReason = "logout";
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        return Results.NoContent();
    }

    private static IResult InvalidCodeProblem(RequestContext ctx) =>
        IdentityProblems.Of(IdentityMessageKeys.AuthCodeInvalid, StatusCodes.Status400BadRequest, ctx);

    /// <summary>Refresh-reuse renders the SAME generic shape as an unknown/expired token (§1b: "a thief learns nothing").</summary>
    private static IResult GenericAuthProblem(RequestContext ctx) =>
        IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status400BadRequest, ctx);
}
