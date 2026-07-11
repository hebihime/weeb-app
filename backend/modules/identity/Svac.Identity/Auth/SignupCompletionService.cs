using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Consent;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.Identity.Config;
using Svac.Identity.Consent;
using Svac.Identity.Persistence;

namespace Svac.Identity.Auth;

/// <summary>Closed outcome union for POST /v1/signup/complete (SLICE_S3_CONTRACT.md §1c).</summary>
public abstract record SignupCompleteOutcome
{
    public sealed record SessionResult(IssuedSession Session, string AccountId) : SignupCompleteOutcome;

    /// <summary>ONE wire shape for BOTH the 18 and 13 floors (§0/§1g) — the caller must never render these two cases differently.</summary>
    public sealed record RefusedAgeFloorResult : SignupCompleteOutcome;

    public sealed record InvalidTokenResult : SignupCompleteOutcome;

    public sealed record HandleInvalidResult(string ReasonKey) : SignupCompleteOutcome;

    public sealed record HandleTakenResult : SignupCompleteOutcome;

    public sealed record ValidationErrorResult(string Field) : SignupCompleteOutcome;

    public static readonly SignupCompleteOutcome RefusedAgeFloor = new RefusedAgeFloorResult();
    public static readonly SignupCompleteOutcome InvalidToken = new InvalidTokenResult();
    public static readonly SignupCompleteOutcome HandleTaken = new HandleTakenResult();
}

/// <summary>
/// THE atomic write (SLICE_S3_CONTRACT.md §1c/§1g/§2/§8): AgeMath oracle FIRST; NO PII persists before the
/// birthdate is proven adult AND the verifiedToken is proven; ONE atomic tx = account row +
/// AgeAttestation18Plus + TermsAcceptance consent events + `identity.account_created` audit event +
/// behavioral event + first session; verifiedToken single-consumption ⇒ idempotent under race (replay
/// returns the winner's session); handle uniqueness via 23505 catch + re-read winner; birthdate stored
/// field-encrypted, NEVER a plain age/year column, NEVER in any response.
/// </summary>
public sealed class SignupCompletionService(
    IdentityDbContext db,
    IFieldEncryptor fieldEncryptor,
    Svac.DomainCore.Contracts.FieldEncryption.IFieldKeyVault fieldKeyVault,
    IConfigRegistry config,
    Svac.DomainCore.Contracts.Streams.IEventStore eventStore,
    ConsentCurrentProjection consentCurrentProjection,
    PushCategoryConsentProjection pushCategoryConsentProjection)
{
    private const string TermsVersion = "v1"; // ToS version this build attests — bumping is a versioned contract change, not this build's concern.

    public async Task<SignupCompleteOutcome> Complete(
        string verifiedToken, string? handleRaw, string? birthdateRaw, string? fandomTagRaw, string? localeRaw,
        IReadOnlyList<string> allowedLocales, RequestContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verifiedToken))
        {
            return new SignupCompleteOutcome.ValidationErrorResult("verifiedToken");
        }
        if (string.IsNullOrWhiteSpace(handleRaw))
        {
            return new SignupCompleteOutcome.ValidationErrorResult("handle");
        }
        if (!DateOnly.TryParseExact(birthdateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birthdate))
        {
            return new SignupCompleteOutcome.ValidationErrorResult("birthdate");
        }
        var fandomTag = (fandomTagRaw ?? string.Empty).Trim();
        if (fandomTag.Length is < 1 or > 64 || fandomTag.Any(char.IsControl))
        {
            return new SignupCompleteOutcome.ValidationErrorResult("fandomTag");
        }
        var locale = localeRaw ?? string.Empty;
        if (!allowedLocales.Contains(locale))
        {
            return new SignupCompleteOutcome.ValidationErrorResult("locale");
        }

        bool isAdult;
        bool isAtLeast13;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            isAdult = AgeMath.IsAtLeast(birthdate, AgeMath.AdultFloorYears, today);
            isAtLeast13 = AgeMath.IsAtLeast(birthdate, AgeMath.CoppaFloorYears, today);
        }
        catch (ArgumentException)
        {
            // "future-date = invalid input never a verdict" (§8) — distinct from the age-floor refusal.
            return new SignupCompleteOutcome.ValidationErrorResult("birthdate");
        }

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("IdentityDbContext has no connection string configured.");

        await using var scope = await IdentityAtomicScope.OpenAsync(connectionString, ct);

        var tokenHash = SessionTokens.HashVerifiedToken(verifiedToken);
        var challenge = await scope.Db.EmailChallenges
            .FromSqlInterpolated($"SELECT * FROM identity.email_challenges WHERE verified_token_hash = {tokenHash} AND purpose = 'signup' FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var verifiedTokenTtlMinutes = await config.GetValue<int>(IdentityConfigKeys.SignupVerifiedTokenTtlMinutes, ct);

        if (challenge is null || challenge.VerifiedAt is null || challenge.VerifiedAt.Value.AddMinutes(verifiedTokenTtlMinutes) <= now)
        {
            await scope.RollbackAsync(ct);
            return SignupCompleteOutcome.InvalidToken;
        }

        if (challenge.ConsumedAt is not null)
        {
            // REPLAY: this exact verifiedToken already won — idempotent under race means the loser's
            // retry resolves to the WINNER's account, never a duplicate-account error.
            var existingAccountId = await scope.Db.Accounts
                .Where(a => a.Email != null && a.Email == challenge.EmailLower && a.TombstonedAt == null)
                .Select(a => a.AccountId)
                .FirstOrDefaultAsync(ct);

            if (existingAccountId is null)
            {
                await scope.RollbackAsync(ct);
                return SignupCompleteOutcome.InvalidToken;
            }

            var replaySession = await SessionIssuance.IssueAsync(
                scope.Db, existingAccountId, deviceId: null, ctx.Region.ToString(), ResolveLawfulBasis(ctx, "identity.sessions", "session.replay_mint"),
                await config.GetValue<int>(IdentityConfigKeys.SessionAccessTtlMinutes, ct),
                await config.GetValue<int>(IdentityConfigKeys.SessionRefreshTtlDays, ct),
                await config.GetValue<int>(IdentityConfigKeys.SessionMaxActivePerAccount, ct),
                ct);

            await scope.CommitAsync(ct);
            return new SignupCompleteOutcome.SessionResult(replaySession, existingAccountId);
        }

        if (!isAdult)
        {
            if (!isAtLeast13)
            {
                // Under-13: same-tx HARD DELETE of the challenge row (§1g "no table it could land in") —
                // the row provably ceases to exist, not merely marked consumed. Remove() only stages the
                // deletion; it must be flushed via SaveChangesAsync before Commit — committing the raw
                // ADO.NET transaction does NOT itself flush EF's in-memory change tracker.
                scope.Db.EmailChallenges.Remove(challenge);
                await scope.Db.SaveChangesAsync(ct);
            }
            // Under-18-but-13-plus: leave the row exactly as-is (unconsumed) so a legitimate resubmission
            // with a corrected birthdate can still complete before the verifiedToken/code TTL — zero
            // persistence either way, and the wire response is byte-identical to the under-13 branch.

            // Anonymous refusal event carrying ZERO identifiers (§0): payload is "{}", and the actor on
            // an anonymous signup request is already the request's own opaque anonymous placeholder —
            // never the submitted email/handle/birthdate.
            await scope.Events.Append(StreamType.Behavioral, ctx.Actor.Id.ToString(), "identity.signup_refused_age", "{}", ctx, ExpectedVersion.AnyVersion, ct);

            await scope.CommitAsync(ct);
            return SignupCompleteOutcome.RefusedAgeFloor;
        }

        // OQ-3 (RATIFIED (a), §11/§13): consult identity.ban_evasion_refs to refuse re-registration by a
        // previously-banned email — WIRE-UNIFORM with the age-floor refusal (the SAME outcome type, same
        // 422 + signup.refused_age_floor key, same zero-persistence shape) so the wire never distinguishes
        // "you're underage" from "this email is banned" — no oracle. A distinct BEHAVIORAL event (never
        // exposed to the client) preserves server-side operational visibility.
        var banEvasionRef = await Svac.Identity.Deletion.BanEvasionRefs.ComputeHmacRef(fieldKeyVault, challenge.EmailLower, ct);
        if (await scope.Db.BanEvasionRefs.AnyAsync(b => b.HmacEmail == banEvasionRef, ct))
        {
            await scope.Events.Append(StreamType.Behavioral, ctx.Actor.Id.ToString(), "identity.signup_refused_ban_evasion", "{}", ctx, ExpectedVersion.AnyVersion, ct);
            await scope.CommitAsync(ct);
            return SignupCompleteOutcome.RefusedAgeFloor;
        }

        var handleValidation = HandleRules.Validate(handleRaw);
        if (!handleValidation.IsValid)
        {
            await scope.RollbackAsync(ct);
            return new SignupCompleteOutcome.HandleInvalidResult(handleValidation.ReasonKey!);
        }
        var canonicalHandle = handleValidation.Canonical!;

        // OQ-2 (RATIFIED (b), §11/§13): a retired handle past its retirement_days quarantine window is
        // available again — lazy expiry (no background sweep needed), mirroring ExportEndpoints' own
        // lazy-expiry posture. Mirrored in SignupEndpoints.GetHandleAvailability so the availability
        // check and this enforcement never disagree.
        var retirementDays = await config.GetValue<int>(IdentityConfigKeys.HandleRetirementDays, ct);
        var retirementCutoff = now.AddDays(-retirementDays);
        var reservedOrRetired = await scope.Db.ReservedHandles.AnyAsync(h => h.Handle == canonicalHandle, ct)
            || await scope.Db.RetiredHandles.AnyAsync(h => h.Handle == canonicalHandle && h.RetiredAt > retirementCutoff, ct);
        if (reservedOrRetired)
        {
            await scope.RollbackAsync(ct);
            return SignupCompleteOutcome.HandleTaken;
        }

        var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();
        var region = ctx.Region.ToString();
        var birthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), birthdate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct);

        scope.Db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = canonicalHandle,
            Email = challenge.EmailLower,
            EmailVerifiedAt = challenge.VerifiedAt!.Value,
            BirthdateEnc = birthdateEnc,
            AttestedAdultAt = now,
            TermsVersion = TermsVersion,
            FandomTag = fandomTag,
            AvatarRef = null,
            Locale = locale,
            AccountState = "active",
            IrlAccessState = "active",
            StateChangedAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = region,
            RegionSource = Svac.DomainCore.Contracts.RegionSource.Signup.ToString(),
            LawfulBasis = ResolveLawfulBasis(ctx, "identity.accounts", "account.created"),
        });

        try
        {
            await scope.Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (TryGetUniqueViolationConstraint(dbEx, out var constraintName))
        {
            await scope.RollbackAsync(ct);
            return constraintName == "ux_accounts_handle"
                ? SignupCompleteOutcome.HandleTaken
                : new SignupCompleteOutcome.ValidationErrorResult("email"); // ux_accounts_email race — vanishingly rare (email uniqueness already gated by the challenge machine); surfaced honestly, never a 500.
        }

        challenge.ConsumedAt = now;
        await scope.Db.SaveChangesAsync(ct);

        var subject = new SubjectRef("account", accountId);
        await AppendConsent(scope, subject, ConsentKind.AgeAttestation18Plus, "attested_adult", ctx, ct);
        await AppendConsent(scope, subject, ConsentKind.TermsAcceptance, TermsVersion, ctx, ct);

        await scope.Events.Append(StreamType.Audit, accountId, "identity.account_created", "{}", ctx, ExpectedVersion.AnyVersion, ct);
        await scope.Events.Append(StreamType.Behavioral, accountId, "identity.signup_completed", "{}", ctx, ExpectedVersion.AnyVersion, ct);

        var session = await SessionIssuance.IssueAsync(
            scope.Db, accountId, deviceId: null, region, ResolveLawfulBasis(ctx, "identity.sessions", "session.first_mint"),
            await config.GetValue<int>(IdentityConfigKeys.SessionAccessTtlMinutes, ct),
            await config.GetValue<int>(IdentityConfigKeys.SessionRefreshTtlDays, ct),
            await config.GetValue<int>(IdentityConfigKeys.SessionMaxActivePerAccount, ct),
            ct);

        await scope.CommitAsync(ct);

        // Refresh the two identity projections NOW (post-commit, ambient transaction gone) so the E2E's
        // "read consent back" assertion never races the write (§8 clause 7's projections are still
        // independently rebuildable — this is a convenience trigger, not the only way they ever run).
        await eventStore.Replay(StreamType.Consent, consentCurrentProjection.ConsumerId, consentCurrentProjection, ct);
        await eventStore.Replay(StreamType.Consent, pushCategoryConsentProjection.ConsumerId, pushCategoryConsentProjection, ct);

        return new SignupCompleteOutcome.SessionResult(session, accountId);
    }

    private static async Task AppendConsent(IdentityAtomicScope scope, SubjectRef subject, ConsentKind kind, string version, RequestContext ctx, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            consent_key = ConsentKindKeys.KeyFor(kind),
            version,
            decision = "granted",
            surface = "signup",
        });
        await scope.Events.Append(StreamType.Consent, subject.ResourceId, "consent.recorded", payload, ctx, ExpectedVersion.AnyVersion, ct);
    }

    private static bool TryGetUniqueViolationConstraint(DbUpdateException dbEx, out string constraintName)
    {
        constraintName = string.Empty;
        if (dbEx.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            constraintName = pg.ConstraintName ?? string.Empty;
            return true;
        }
        return false;
    }

    private static string ResolveLawfulBasis(RequestContext ctx, string store, string eventType) =>
        LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, store, eventType, ctx.Region.ToString());
}
