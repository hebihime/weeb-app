using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Consent;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.Hosting;
using Svac.Identity.Auth;
using Svac.Identity.Config;
using Svac.Identity.Contracts;
using Svac.Identity.Persistence;

namespace Svac.Identity.Endpoints;

/// <summary>
/// The full `/v1/me/*` account-management surface (SLICE_S3_CONTRACT.md §1c/§1b/§3b): the authenticated
/// account-management set built on the foundation's minimal `GET /v1/me` + the Auth-F3 `IResourceOwnershipResolver`
/// exemplar (`DELETE /v1/me/sessions/{sessionId}`). Every mutation/read below carries its own §3b policy
/// row; `/v1/me/*` never names an account id in a route or body (§1c) — self-scoped actions bind
/// <see cref="PolicyTargetBinding.SelfAccount"/> (the caller's OWN id, taken from the session), and the two
/// owner-scoped resources (session, device) bind <see cref="PolicyTargetBinding.FromRoute"/> against the
/// pre-registered <see cref="Svac.DomainCore.Contracts.Policy.IResourceOwnershipResolver"/>s.
/// </summary>
public static class MeEndpoints
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>MAIL-1 (SECURITY_REVIEW_S3.md, silent-reject Finding 3): PUT /v1/me/email + its confirm had
    /// no floor at all — same 60ms floor as every other anti-enumeration endpoint (SignupEndpoints/
    /// AuthEndpoints' own AntiEnumerationFloor).</summary>
    private static readonly TimeSpan AntiEnumerationFloor = TimeSpan.FromMilliseconds(60);

    /// <summary>The mutable push-category set (SLICE_S3_CONTRACT.md §0/§1c/§12 item 10): 1-7,9. Category 8 is UNREPRESENTABLE here — not filtered out, simply never a member — so a request naming it renders IDENTICALLY to any other value outside this set: 404, absence, never a locked toggle.</summary>
    private static readonly int[] MutablePushCategories = { 1, 2, 3, 4, 5, 6, 7, 9 };

    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/me", GetMe)
            .WithName("GetMe")
            .RequirePolicyAction("identity.me.read", PolicyTargetBinding.SelfAccount)
            .Produces<AccountSelf>(StatusCodes.Status200OK);

        app.MapPatch("/v1/me", PatchMe)
            .WithName("PatchMe")
            .RequirePolicyAction("identity.settings.update", PolicyTargetBinding.SelfAccount)
            .Produces(StatusCodes.Status200OK)
            .Produces<Problem>(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/v1/me/handle", PostMeHandle)
            .WithName("PostMeHandle")
            .RequirePolicyAction("identity.handle.change", PolicyTargetBinding.SelfAccount)
            .Produces(StatusCodes.Status200OK)
            .Produces<LimitReached>(StatusCodes.Status429TooManyRequests)
            .Produces<Problem>(StatusCodes.Status422UnprocessableEntity);

        app.MapPut("/v1/me/email", PutMeEmail)
            .WithName("PutMeEmail")
            .RequirePolicyAction("identity.email.change", PolicyTargetBinding.SelfAccount)
            .Produces<ChallengeIssued>(StatusCodes.Status202Accepted)
            .Produces<Problem>(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/v1/me/email/confirm", PostMeEmailConfirm)
            .WithName("PostMeEmailConfirm")
            .RequirePolicyAction("identity.email.change_confirm", PolicyTargetBinding.SelfAccount)
            .Produces(StatusCodes.Status200OK)
            .Produces<Problem>(StatusCodes.Status400BadRequest);

        app.MapGet("/v1/me/sessions", GetMeSessions)
            .WithName("GetMeSessions")
            .RequirePolicyAction("identity.session.list", PolicyTargetBinding.SelfAccount)
            .Produces<IReadOnlyList<SessionSummary>>(StatusCodes.Status200OK);

        app.MapDelete("/v1/me/sessions/{sessionId}", DeleteMeSession)
            .WithName("DeleteMeSession")
            // THE Auth-F3 exemplar (SLICE_S3_CONTRACT.md §3): the target is a REAL resource id read out of
            // the route, resolved against the registered SessionOwnershipResolver; foreign ≡ nonexistent.
            .RequirePolicyAction("identity.session.revoke", PolicyTargetBinding.FromRoute("sessionId", "session"))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/v1/me/devices", PostMeDevices)
            .WithName("PostMeDevices")
            .RequirePolicyAction("identity.device.register", PolicyTargetBinding.SelfAccount)
            .Produces<DeviceRegistered>(StatusCodes.Status201Created)
            .Produces<LimitReached>(StatusCodes.Status429TooManyRequests)
            .Produces<Problem>(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/v1/me/devices/{deviceId}", DeleteMeDevice)
            .WithName("DeleteMeDevice")
            .RequirePolicyAction("identity.device.remove", PolicyTargetBinding.FromRoute("deviceId", "device"))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/v1/me/push-consents", GetMePushConsents)
            .WithName("GetMePushConsents")
            .RequirePolicyAction("identity.consent.read_push_categories", PolicyTargetBinding.SelfAccount)
            .Produces<IReadOnlyList<PushConsentRow>>(StatusCodes.Status200OK);

        app.MapPut("/v1/me/push-consents/{category}", PutMePushConsent)
            .WithName("PutMePushConsent")
            .RequirePolicyAction("identity.consent.set_push_category", PolicyTargetBinding.SelfAccount)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me — the full AccountSelf shape (SLICE_S3_CONTRACT.md §1c): ageYears is DERIVED via
    // AgeMath; birthdate never crosses into the response graph (arch-scanned).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> GetMe(
        [FromServices] IdentityDbContext db,
        [FromServices] IFieldEncryptor fieldEncryptor,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountId == accountId, ct);
        if (account is null)
        {
            // The policy chokepoint already proved a live session for a User actor exists — this is
            // defensive only (e.g. a tombstoned row mid-purge) and renders absence, never a leak.
            return Results.NotFound();
        }

        var birthdateText = await fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, account.BirthdateEnc, ct);
        var birthdate = DateOnly.Parse(birthdateText, CultureInfo.InvariantCulture);
        var ageYears = AgeMath.AgeYears(birthdate, DateOnly.FromDateTime(DateTime.UtcNow));

        var self = new AccountSelf(
            OpaqueId.Parse(account.AccountId),
            account.Handle,
            account.Email ?? string.Empty,
            ageYears,
            account.Locale,
            account.FandomTag,
            account.CreatedAt,
            account.DeletionEffectiveAt);

        return Results.Ok(self);
    }

    // ------------------------------------------------------------------------------------------------
    // PATCH /v1/me — {locale?} in i18n/locales.json (SLICE_S3_CONTRACT.md §1c).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> PatchMe(
        [FromBody] SettingsUpdateRequest request,
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        [FromServices] ClientConfigResponse clientConfig,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;

        if (request.Locale is not null)
        {
            if (!clientConfig.Locales.Contains(request.Locale))
            {
                return IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status422UnprocessableEntity, ctx);
            }

            var accountId = ctx.Actor.Id.ToString();
            await db.Accounts
                .Where(a => a.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Locale, request.Locale), ct);
        }

        return Results.Ok(new { });
    }

    // ------------------------------------------------------------------------------------------------
    // POST /v1/me/handle — cooldown renders THE one LimitReached shape; reserved/retired/taken render
    // ONE handle.taken (SLICE_S3_CONTRACT.md §1c).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> PostMeHandle(
        [FromBody] HandleChangeRequest request,
        [FromServices] HandleChangeService handleChange,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        var accountId = ctx.Actor.Id.ToString();

        var outcome = await handleChange.Change(accountId, request.Handle, ctx, ct);
        return outcome switch
        {
            HandleChangeOutcome.ChangedResult or HandleChangeOutcome.NoOpResult => Results.Ok(new { }),
            HandleChangeOutcome.CooldownResult cooldown => PolicyResults.LimitReached(cooldown.LimitReached),
            HandleChangeOutcome.TakenResult => IdentityProblems.Of(IdentityMessageKeys.HandleTaken, StatusCodes.Status422UnprocessableEntity, ctx),
            _ => IdentityProblems.Of(IdentityMessageKeys.HandleInvalid, StatusCodes.Status422UnprocessableEntity, ctx),
        };
    }

    // ------------------------------------------------------------------------------------------------
    // PUT /v1/me/email (+ POST /v1/me/email/confirm) — the SAME challenge machine, code to the NEW
    // address; on confirm, the OLD address gets the takeover tripwire notice (SLICE_S3_CONTRACT.md §1c/§7).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> PutMeEmail(
        [FromBody] EmailChangeRequest request,
        [FromServices] EmailChallengeMachine challenges,
        [FromServices] IRequestContextAccessor requestContext,
        [FromServices] ClientConfigResponse clientConfig,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var ctx = requestContext.Current;

        if (!EmailInput.TryNormalize(request.Email, out var emailLower))
        {
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status422UnprocessableEntity, ctx);
        }

        var accountId = ctx.Actor.Id.ToString();
        var locale = string.IsNullOrEmpty(ctx.Locale) || !clientConfig.Locales.Contains(ctx.Locale) ? clientConfig.DefaultLocale : ctx.Locale;
        var challengeId = await challenges.IssueForEmailChange(accountId, emailLower, locale, ctx, ct);

        await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
        return Results.Json(new ChallengeIssued(challengeId), statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> PostMeEmailConfirm(
        [FromBody] EmailVerificationConfirmRequest request,
        [FromServices] EmailChallengeMachine challenges,
        [FromServices] Svac.DomainCore.Contracts.Email.IEmailSender emailSender,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var ctx = requestContext.Current;
        if (string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.Code))
        {
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return InvalidCodeProblem(ctx);
        }

        var accountId = ctx.Actor.Id.ToString();
        var outcome = await challenges.ConfirmEmailChange(accountId, request.ChallengeId, request.Code, ctx, ct);

        if (outcome is EmailChangeConfirmResult.SwappedResult swapped)
        {
            // The security notice goes to the OLD address, AFTER the swap commits (§1b/§7: "the account-
            // takeover tripwire in passwordless auth") — never inside the DB transaction. Enqueue-only
            // now (MAIL-1, OutboxEmailSender) — this await returns immediately regardless.
            await emailSender.SendAsync(
                new Svac.DomainCore.Contracts.Email.EmailMessage(swapped.OldEmail, "email.email_changed_notice", ctx.Locale, EmptyModel),
                ctx, ct);
            await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
            return Results.Ok(new { });
        }

        await TimingFloor.NormalizeAsync(stopwatch, AntiEnumerationFloor, ct);
        return InvalidCodeProblem(ctx);
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me/sessions — live sessions only; `current` marks the session the presenting bearer token
    // itself belongs to (SLICE_S3_CONTRACT.md §1c: "the honest theft lever").
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> GetMeSessions(
        HttpContext httpContext,
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var currentHash = ExtractBearerHash(httpContext);

        var rows = await db.Sessions
            .Where(s => s.AccountId == accountId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new { s.SessionId, s.DeviceId, s.CreatedAt, s.LastSeenAt, s.AccessTokenHash })
            .ToListAsync(ct);

        var deviceIds = rows.Where(r => r.DeviceId != null).Select(r => r.DeviceId!).Distinct().ToList();
        var platformByDevice = deviceIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Devices.Where(d => deviceIds.Contains(d.DeviceId)).ToDictionaryAsync(d => d.DeviceId, d => d.Platform, ct);

        var summaries = rows.Select(r => new SessionSummary(
            OpaqueId.Parse(r.SessionId),
            r.DeviceId is not null && platformByDevice.TryGetValue(r.DeviceId, out var platform) ? platform : null,
            r.CreatedAt,
            r.LastSeenAt,
            currentHash is not null && r.AccessTokenHash.AsSpan().SequenceEqual(currentHash))
        ).ToList();

        return Results.Ok(summaries);
    }

    // ------------------------------------------------------------------------------------------------
    // DELETE /v1/me/sessions/{sessionId} — ownership already proven by the policy chokepoint (Auth-F3);
    // a guarded UPDATE makes the revoke idempotent under a race with itself.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> DeleteMeSession(
        [FromRoute] string sessionId,
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var now = DateTimeOffset.UtcNow;
        await db.Sessions
            .Where(s => s.SessionId == sessionId && s.AccountId == accountId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokeReason, "user_revoked"), ct);

        return Results.NoContent();
    }

    // ------------------------------------------------------------------------------------------------
    // POST /v1/me/devices — device + push-token STORE only (delivery is S4); server-minted dev_ id.
    // ------------------------------------------------------------------------------------------------
    private static readonly string[] AllowedPlatforms = { "ios", "android", "web" };

    private static async Task<IResult> PostMeDevices(
        [FromBody] DeviceRegisterRequest request,
        [FromServices] IdentityDbContext db,
        [FromServices] IQuotaService quotaService,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var ctx = requestContext.Current;
        if (request.Platform is null || !AllowedPlatforms.Contains(request.Platform))
        {
            return IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status422UnprocessableEntity, ctx);
        }

        var quotaContext = new QuotaContext(
            new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
            TimeZoneInfo.Utc,
            TimeOnly.MinValue,
            DateTimeOffset.UtcNow);
        var quotaResult = await quotaService.Consume(ctx.Actor, IdentityQuotaKeys.DeviceRegisterDaily, quotaContext, ct);
        if (quotaResult is QuotaResult.Limited limited)
        {
            return PolicyResults.LimitReached(limited.LimitReached);
        }

        var now = DateTimeOffset.UtcNow;
        var accountId = ctx.Actor.Id.ToString();
        var deviceId = OpaqueId.New(IdPrefixes.Device, now, Random.Shared).ToString();

        db.Devices.Add(new DeviceEntity
        {
            DeviceId = deviceId,
            AccountId = accountId,
            Platform = request.Platform,
            PushToken = request.PushToken,
            PushTokenUpdatedAt = request.PushToken is not null ? now : null,
            CreatedAt = now,
            LastSeenAt = now,
            Region = ctx.Region.ToString(),
            LawfulBasis = LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, "identity.devices", "device.registered", ctx.Region.ToString()),
        });
        await db.SaveChangesAsync(ct);

        return Results.Json(new DeviceRegistered(deviceId), statusCode: StatusCodes.Status201Created);
    }

    // ------------------------------------------------------------------------------------------------
    // DELETE /v1/me/devices/{deviceId} — resource-scoped like sessions (OwnedResource); push token
    // cleared on removal (data-minimization, mirrors §1c's "clear its device's push token" on logout).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> DeleteMeDevice(
        [FromRoute] string deviceId,
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var now = DateTimeOffset.UtcNow;
        await db.Devices
            .Where(d => d.DeviceId == deviceId && d.AccountId == accountId && d.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.PushToken, (string?)null), ct);

        return Results.NoContent();
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me/push-consents — rows for categories 1-7,9; a category never touched by the account
    // renders `enabled:false` (opt-in default, never a missing row on the wire).
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> GetMePushConsents(
        [FromServices] IdentityDbContext db,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        var accountId = requestContext.Current.Actor.Id.ToString();
        var existing = await db.PushCategoryConsents
            .Where(p => p.AccountId == accountId)
            .ToDictionaryAsync(p => (int)p.Category, p => p.Enabled, ct);

        var rows = MutablePushCategories
            .Select(category => new PushConsentRow(category, existing.TryGetValue(category, out var enabled) && enabled))
            .ToList();

        return Results.Ok(rows);
    }

    // ------------------------------------------------------------------------------------------------
    // PUT /v1/me/push-consents/{category} — category 8 (or any value outside 1-7,9) is 404, WIRE-
    // IDENTICAL to a nonexistent category (SLICE_S3_CONTRACT.md §0/§1c/§12 item 10: "not a 403, not a
    // locked toggle"). Writes via IConsentLedgerWriter, same-tx.
    // ------------------------------------------------------------------------------------------------
    private static async Task<IResult> PutMePushConsent(
        [FromRoute] int category,
        [FromBody] PushConsentSetRequest request,
        [FromServices] IConsentLedgerWriter consentWriter,
        [FromServices] IRequestContextAccessor requestContext,
        CancellationToken ct)
    {
        if (!MutablePushCategories.Contains(category))
        {
            return Results.NotFound();
        }

        var ctx = requestContext.Current;
        if (request.Enabled is null)
        {
            return IdentityProblems.Of(MessageKeys.ErrorGeneric, StatusCodes.Status400BadRequest, ctx);
        }

        var accountId = ctx.Actor.Id.ToString();
        var kind = ConsentKind.PushCategory((PushCategoryValue)category);
        var decision = request.Enabled.Value ? ConsentDecision.Granted : ConsentDecision.Revoked;

        await consentWriter.Record(new SubjectRef("account", accountId), kind, "v1", "push_consents", decision, ctx, ct);
        return Results.NoContent();
    }

    // --- shared helpers -------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> EmptyModel = new Dictionary<string, string>();

    private static IResult InvalidCodeProblem(RequestContext ctx) =>
        IdentityProblems.Of(IdentityMessageKeys.AuthCodeInvalid, StatusCodes.Status400BadRequest, ctx);

    private static byte[]? ExtractBearerHash(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = header[BearerPrefix.Length..].Trim();
        return token.Length == 0 ? null : SessionTokens.HashAccessToken(token);
    }
}
