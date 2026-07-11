using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Hosting;
using Svac.Identity.Auth;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Endpoints;

/// <summary>The signup/* routes (SLICE_S3_CONTRACT.md §1c). Anonymous mutations sit behind the `identity-anon` fixed-window rate limiter (IdentityRateLimiting).</summary>
public static class SignupEndpoints
{
    private static readonly TimeSpan AntiEnumerationFloor = TimeSpan.FromMilliseconds(60);

    public static void MapSignupEndpoints(this IEndpointRouteBuilder app)
    {
        // OPS-6 (SECURITY_REVIEW_S3.md, LOW→fix): unbounded handle-existence scraping + triple-AnyAsync
        // DB amplification per call — now behind the SAME anonymous per-IP limiter as every POST mutation
        // (identity-anon policy applies to any request shape, GET included; OPS-1's UseForwardedHeaders
        // fix makes the partition key meaningful behind the ACA ingress).
        app.MapGet("/v1/signup/handle-availability", GetHandleAvailability)
            .WithName("GetSignupHandleAvailability")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .Produces<HandleAvailabilityResponse>(StatusCodes.Status200OK);

        app.MapPost("/v1/signup/email-verification", PostEmailVerification)
            .WithName("PostSignupEmailVerification")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .RequirePolicyAction("identity.signup.challenge")
            .Produces<ChallengeIssued>(StatusCodes.Status202Accepted)
            .Produces<Problem>(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/v1/signup/email-verification/confirm", PostEmailVerificationConfirm)
            .WithName("PostSignupEmailVerificationConfirm")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .RequirePolicyAction("identity.signup.confirm")
            .Produces<VerifiedTokenIssued>(StatusCodes.Status200OK)
            .Produces<Problem>(StatusCodes.Status400BadRequest);

        app.MapPost("/v1/signup/complete", PostSignupComplete)
            .WithName("PostSignupComplete")
            .RequireRateLimiting(IdentityRateLimiting.AnonymousMutationPolicy)
            .RequirePolicyAction("identity.signup.complete")
            .Produces<Svac.Identity.Contracts.SessionCreated>(StatusCodes.Status201Created)
            .Produces<Problem>(StatusCodes.Status400BadRequest)
            .Produces<Problem>(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> GetHandleAvailability(
        [FromQuery] string? handle,
        [FromServices] IdentityDbContext db,
        [FromServices] Svac.DomainCore.Contracts.Config.IConfigRegistry config,
        CancellationToken ct)
    {
        // Reserved, retired, and taken all render IDENTICALLY {available:false} — no reserved-list, no
        // deleted-account oracle (SLICE_S3_CONTRACT.md §1c). A malformed handle also renders `false`
        // rather than a validation error — the availability check names no oracle for input shape either.
        if (string.IsNullOrWhiteSpace(handle))
        {
            return Results.Ok(new HandleAvailabilityResponse(false));
        }

        var validation = HandleRules.Validate(handle);
        if (!validation.IsValid)
        {
            return Results.Ok(new HandleAvailabilityResponse(false));
        }

        var canonical = validation.Canonical!;
        // OQ-2 (RATIFIED (b)): a retired handle past its retirement_days quarantine window reads as
        // available — mirrors SignupCompletionService's own enforcement so this GET never lies about
        // what POST /v1/signup/complete would actually accept.
        var retirementDays = await config.GetValue<int>(IdentityConfigKeys.HandleRetirementDays, ct);
        var retirementCutoff = DateTimeOffset.UtcNow.AddDays(-retirementDays);
        // PII-3 / CONC-4 (SECURITY_REVIEW_S3.md, grace-window identity takeover): gate on
        // `tombstoned_at IS NULL`, NOT `account_state <> 'deleted'`. During the 14-30 day grace window
        // (Phase L: account_state='deleted', tombstoned_at still NULL, handle column UNCHANGED) the old
        // `account_state <> 'deleted'` filter excluded the row here, reporting the handle available —
        // even though the row still legitimately holds it. A third party could then register it,
        // permanently destroying the rightful owner's cancel right (CancelDeletion's own uncaught 23505 —
        // see the defensive catch added there). tombstoned_at is only ever set by the PHYSICAL purge
        // (Phase P), at the exact moment this row's Handle column is overwritten with a retired sentinel
        // and the original moves to identity.retired_handles — so this predicate and the DB's own partial
        // unique index free the handle at IDENTICALLY the same instant, by construction.
        var taken = await db.Accounts.AnyAsync(a => a.Handle == canonical && a.TombstonedAt == null, ct)
            || await db.ReservedHandles.AnyAsync(h => h.Handle == canonical, ct)
            || await db.RetiredHandles.AnyAsync(h => h.Handle == canonical && h.RetiredAt > retirementCutoff, ct);

        return Results.Ok(new HandleAvailabilityResponse(!taken));
    }

    private static async Task<IResult> PostEmailVerification(
        [FromBody] EmailVerificationRequest request,
        [FromServices] EmailChallengeMachine challenges,
        [FromServices] IRequestContextAccessor requestContext,
        [FromServices] ClientConfigResponse clientConfig,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        var stopwatch = Stopwatch.StartNew();

        if (!EmailInput.TryNormalize(request.Email, out var emailLower))
        {
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status422UnprocessableEntity, ctx);
        }
        var locale = ResolveLocale(request.Locale, clientConfig.Locales, clientConfig.DefaultLocale);

        var challengeId = await challenges.IssueForSignup(emailLower, locale, ctx, ct);

        await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
        return Results.Json(new ChallengeIssued(challengeId), statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> PostEmailVerificationConfirm(
        [FromBody] EmailVerificationConfirmRequest request,
        [FromServices] EmailChallengeMachine challenges,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        // MAIL-1 (SECURITY_REVIEW_S3.md, silent-reject Finding 2): this endpoint had NO floor at all — a
        // backed challengeId (real row, HMAC compute + attempt-count write) vs an unbacked decoy id
        // (immediate rollback, no write) differ by more than the floor absorbs unless BOTH the work is
        // equalized (ConfirmSignupCode's dummy-HMAC branch, above) AND the residual is floored here.
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.Code))
        {
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return InvalidCodeProblem(ctx: requestContext.Current);
        }

        var ctx = requestContext.Current;
        var outcome = await challenges.ConfirmSignupCode(request.ChallengeId, request.Code, ctx, ct);
        await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
        return outcome switch
        {
            ChallengeConfirmResult.ConfirmedResult confirmed => Results.Ok(new VerifiedTokenIssued(confirmed.VerifiedToken)),
            _ => InvalidCodeProblem(ctx),
        };
    }

    private static async Task<IResult> PostSignupComplete(
        [FromBody] SignupCompleteRequest request,
        [FromServices] SignupCompletionService signup,
        [FromServices] IRequestContextAccessor requestContext,
        [FromServices] ClientConfigResponse clientConfig,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        var outcome = await signup.Complete(
            request.VerifiedToken ?? string.Empty,
            request.Handle,
            request.Birthdate,
            request.FandomTag,
            request.Locale,
            clientConfig.Locales,
            ctx,
            ct);

        return outcome switch
        {
            SignupCompleteOutcome.SessionResult session => Results.Json(
                new Svac.Identity.Contracts.SessionCreated(
                    session.Session.AccessToken, session.Session.AccessExpiresAt, session.Session.RefreshToken,
                    OpaqueId.Parse(session.AccountId)),
                statusCode: StatusCodes.Status201Created),
            SignupCompleteOutcome.RefusedAgeFloorResult => IdentityProblems.Of(IdentityMessageKeys.SignupRefusedAgeFloor, StatusCodes.Status422UnprocessableEntity, ctx),
            SignupCompleteOutcome.InvalidTokenResult => InvalidCodeProblem(ctx),
            // The specific HandleRules reason (invalid_length/invalid_charset/confusable_rejected) stays
            // an internal detail — the wire renders the ONE ratified key (§1d), never the granular one.
            SignupCompleteOutcome.HandleInvalidResult => IdentityProblems.Of(IdentityMessageKeys.HandleInvalid, StatusCodes.Status422UnprocessableEntity, ctx),
            SignupCompleteOutcome.HandleTakenResult => IdentityProblems.Of(IdentityMessageKeys.HandleTaken, StatusCodes.Status422UnprocessableEntity, ctx),
            SignupCompleteOutcome.ValidationErrorResult validation => IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status400BadRequest, ctx, title: validation.Field),
            _ => IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status400BadRequest, ctx),
        };
    }

    private static IResult InvalidCodeProblem(RequestContext ctx) =>
        IdentityProblems.Of(IdentityMessageKeys.AuthCodeInvalid, StatusCodes.Status400BadRequest, ctx);

    internal static string ResolveLocale(string? requested, IReadOnlyList<string> allowed, string fallback) =>
        requested is not null && allowed.Contains(requested) ? requested : fallback;
}
