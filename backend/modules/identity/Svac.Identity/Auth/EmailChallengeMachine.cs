using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Auth;

/// <summary>The one challenge machine (SLICE_S3_CONTRACT.md §1b): purpose signup|login|email_change. This build's real consumers are signup + login; email_change (the /v1/me/email surface) is next-pass.</summary>
public static class ChallengePurposes
{
    public const string Signup = "signup";
    public const string Login = "login";
    public const string EmailChange = "email_change";
}

/// <summary>Outcome of a guarded code confirmation (SLICE_S3_CONTRACT.md §1c: "invalid/expired/exhausted = ONE auth.code_invalid Problem").</summary>
public abstract record ChallengeConfirmResult
{
    public sealed record InvalidResult : ChallengeConfirmResult;

    public sealed record ConfirmedResult(string VerifiedToken) : ChallengeConfirmResult;

    public static readonly ChallengeConfirmResult Invalid = new InvalidResult();

    public static ChallengeConfirmResult Confirmed(string verifiedToken) => new ConfirmedResult(verifiedToken);
}

/// <summary>Outcome of POST /v1/me/email/confirm (SLICE_S3_CONTRACT.md §1c/§1b/§7): the swap + the old address the security notice must go to, or the ONE generic invalid outcome every other failure of this challenge machine renders.</summary>
public abstract record EmailChangeConfirmResult
{
    public sealed record InvalidResult : EmailChangeConfirmResult;

    public sealed record SwappedResult(string OldEmail, string NewEmail) : EmailChangeConfirmResult;

    public static readonly EmailChangeConfirmResult Invalid = new InvalidResult();
}

/// <summary>
/// Issues and confirms email challenges (SLICE_S3_CONTRACT.md §1b/§1c/§5/§8). Every issuance path — signup
/// verification and login code alike — ALWAYS consumes the `identity.email.send.daily` quota keyed on the
/// HMAC'd mailbox regardless of whether an account exists for it, so alternating between the signup and
/// login doors can never bypass the per-mailbox flood cap and can never turn "was the quota consumed" into
/// a second enumeration oracle (§5). Anti-enumeration is structural: every code path returns the SAME
/// shape (a syntactically valid challengeId) whether or not a row was actually persisted.
/// </summary>
public sealed class EmailChallengeMachine(
    IdentityDbContext db,
    IFieldKeyVault keyVault,
    IEmailSender emailSender,
    IQuotaService quotaService,
    IEventStore eventStore,
    IConfigRegistry config)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyModel = new Dictionary<string, string>();

    /// <summary>
    /// POST /v1/signup/email-verification (SLICE_S3_CONTRACT.md §1c). Never persists a row for an
    /// already-registered email — "you already have an account" is a MAIL, not a code.
    ///
    /// "Already registered" is <c>tombstoned_at IS NULL</c> — the SAME predicate as
    /// <c>ux_accounts_email</c>'s partial unique index (§2 DDL, PII-3 / CONC-4 fix, SECURITY_REVIEW_S3.md)
    /// — deliberately NOT <c>account_state &lt;&gt; 'deleted'</c> anymore. The original reasoning here
    /// (superseded) was that a Phase-L-deleted account had already freed its email for the index's
    /// purposes, so gating on account_state let a fresh signup reach <c>Complete</c> immediately during
    /// grace, keeping OQ-3's ban-evasion consult reachable without waiting out the full grace window. The
    /// adversarial review found the actual index ALSO used that predicate — meaning a THIRD PARTY, not
    /// just the rightful owner, could claim the still-live email during the grace window, and
    /// <c>AccountLifecycle.CancelDeletion</c> restoring <c>account_state='active'</c> would then collide
    /// with the squatter's row (an uncaught 23505 permanently destroying the cancel right). Both the index
    /// and this check now gate on <c>tombstoned_at IS NULL</c> instead: the email is genuinely free ONLY
    /// once the Phase P physical purge nulls it. The OQ-3 ban-evasion consequence is accepted and safe:
    /// during grace, re-registering with the SAME email now correctly renders "already registered" (this
    /// IS the desired anti-takeover behavior); once the account is truly purged its Email column is
    /// NULLed, so this check finds no existing row and the ban-evasion HMAC lookup in
    /// <see cref="Svac.Identity.Auth.SignupCompletionService.Complete"/> (a SEPARATE, already-populated
    /// store keyed on the email's HMAC, independent of this accounts-table check) still fires exactly as
    /// designed post-purge.
    /// </summary>
    public async Task<string> IssueForSignup(string emailLower, string locale, RequestContext ctx, CancellationToken ct)
    {
        var quotaAllowed = await ConsumeMailboxQuota(emailLower, ctx, ct);
        var now = DateTimeOffset.UtcNow;

        var existingAccountId = await db.Accounts
            .Where(a => a.Email != null && a.Email == emailLower && a.TombstonedAt == null)
            .Select(a => a.AccountId)
            .FirstOrDefaultAsync(ct);

        if (existingAccountId is not null)
        {
            if (quotaAllowed)
            {
                await emailSender.SendAsync(new EmailMessage(emailLower, "email.already_registered", locale, EmptyModel), ctx, ct);
            }
            else
            {
                await EmitQuotaDenied(emailLower, ctx, ct);
            }

            // No row created — there is no code to confirm for an email that already owns an account
            // (§1c: "no account row, no birthdate ... persists before ..."); the wire shape stays
            // identical by minting a syntactically valid, unbacked challengeId.
            return MintChallengeId(now);
        }

        // Issuing a new code voids prior unconsumed same-purpose rows (UPDATE, not DELETE, §2).
        await db.EmailChallenges
            .Where(c => c.Purpose == ChallengePurposes.Signup && c.EmailLower == emailLower && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        var challengeId = MintChallengeId(now);
        var code = EmailCodes.NewCode();
        var ttlMinutes = await config.GetValue<int>(IdentityConfigKeys.EmailCodeTtlMinutes, ct);

        db.EmailChallenges.Add(new EmailChallengeEntity
        {
            ChallengeId = challengeId,
            Purpose = ChallengePurposes.Signup,
            EmailLower = emailLower,
            AccountId = null,
            CodeHash = await EmailCodes.Hash(keyVault, code, ct),
            Attempts = 0,
            ExpiresAt = now.AddMinutes(ttlMinutes),
            CreatedAt = now,
            Locale = locale,
            Region = ctx.Region.ToString(),
            LawfulBasis = ResolveLawfulBasis(ctx, "identity.email_challenges", "challenge.issued"),
        });
        await db.SaveChangesAsync(ct);

        if (quotaAllowed)
        {
            await emailSender.SendAsync(
                new EmailMessage(emailLower, "email.verify_code", locale,
                    new Dictionary<string, string> { ["code"] = code, ["ttlMinutes"] = ttlMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
                ctx, ct);
        }
        else
        {
            await EmitQuotaDenied(emailLower, ctx, ct);
        }

        await EmitBehavioral("identity.signup_started", ctx, ct);
        return challengeId;
    }

    /// <summary>POST /v1/auth/email-code (SLICE_S3_CONTRACT.md §1c). Uniform 202 whether the account exists, is banned, or is absent — code mail sends ONLY for a live, non-banned account.</summary>
    public async Task IssueForLogin(string emailLower, string locale, RequestContext ctx, CancellationToken ct)
    {
        var quotaAllowed = await ConsumeMailboxQuota(emailLower, ctx, ct);
        var now = DateTimeOffset.UtcNow;

        // PII-3 / CONC-4 (SECURITY_REVIEW_S3.md): "login stays open during grace" is a RATIFIED §2
        // behavior (a deleted-in-grace owner must still be able to log in, into the rights-restricted
        // state) — so this cannot simply exclude account_state='deleted' rows outright. But an unordered
        // FirstOrDefaultAsync over `email_lower` is planner-dependent, and during grace a deleted-in-grace
        // account A and a freshly active account B can (pre-this-fix) legitimately share one email —
        // resolving to the WRONG one hands a new mailbox owner the OLD owner's rights set. Deterministic
        // resolution: an ACTIVE row always wins over a deleted-in-grace one for the SAME email (the
        // active row is the CURRENT, provable mailbox owner); only when no active row exists does the
        // legitimate grace-login candidate resolve at all. CreatedAt DESC is a stable tiebreak within a
        // state (should two rows of the same state ever collide, which the accounts table's own
        // uniqueness posture should prevent, but a deterministic order must not depend on that holding).
        var account = await db.Accounts
            .Where(a => a.Email != null && a.Email == emailLower && a.TombstonedAt == null)
            .OrderBy(a => a.AccountState == "deleted" ? 1 : 0)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new { a.AccountId, a.AccountState })
            .FirstOrDefaultAsync(ct);

        var eligible = account is not null && account.AccountState != "banned";

        if (!quotaAllowed || !eligible)
        {
            if (!quotaAllowed)
            {
                await EmitQuotaDenied(emailLower, ctx, ct);
            }
            return; // silent — wire response stays the uniform 202 regardless (caller renders it)
        }

        await db.EmailChallenges
            .Where(c => c.Purpose == ChallengePurposes.Login && c.EmailLower == emailLower && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        var code = EmailCodes.NewCode();
        var ttlMinutes = await config.GetValue<int>(IdentityConfigKeys.EmailCodeTtlMinutes, ct);

        db.EmailChallenges.Add(new EmailChallengeEntity
        {
            ChallengeId = MintChallengeId(now),
            Purpose = ChallengePurposes.Login,
            EmailLower = emailLower,
            AccountId = account!.AccountId,
            CodeHash = await EmailCodes.Hash(keyVault, code, ct),
            Attempts = 0,
            ExpiresAt = now.AddMinutes(ttlMinutes),
            CreatedAt = now,
            Locale = locale,
            Region = ctx.Region.ToString(),
            LawfulBasis = ResolveLawfulBasis(ctx, "identity.email_challenges", "challenge.issued"),
        });
        await db.SaveChangesAsync(ct);

        await emailSender.SendAsync(
            new EmailMessage(emailLower, "email.login_code", locale,
                new Dictionary<string, string> { ["code"] = code, ["ttlMinutes"] = ttlMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
            ctx, ct);
    }

    /// <summary>POST /v1/signup/email-verification/confirm (SLICE_S3_CONTRACT.md §1c). Single guarded UPDATE-shaped consumption via SELECT ... FOR UPDATE — a genuine row lock serializes concurrent confirm attempts on the SAME challengeId.</summary>
    public async Task<ChallengeConfirmResult> ConfirmSignupCode(string challengeId, string code, RequestContext ctx, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var row = await db.EmailChallenges
            .FromSqlInterpolated($"SELECT * FROM identity.email_challenges WHERE challenge_id = {challengeId} AND purpose = {ChallengePurposes.Signup} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var maxAttempts = await config.GetValue<int>(IdentityConfigKeys.EmailCodeMaxAttempts, ct);

        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now || row.Attempts >= maxAttempts)
        {
            // SREJ-2/MAIL-1 (SECURITY_REVIEW_S3.md): a genuinely absent/consumed/expired/exhausted row
            // still pays the SAME keyed-HMAC cost a real comparison would — this is the dominant timing
            // delta between "email available, real backed row" and "email already registered, unbacked
            // decoy id" (silent-reject Finding 2). Computed and discarded; never compared to anything —
            // there is no row to compare against.
            await EmailCodes.Hash(keyVault, code, ct);
            await tx.RollbackAsync(ct);
            return ChallengeConfirmResult.Invalid;
        }

        var presentedHash = await EmailCodes.Hash(keyVault, code, ct);
        var matches = EmailCodes.FixedTimeEquals(presentedHash, row.CodeHash);

        row.Attempts++;

        if (!matches)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ChallengeConfirmResult.Invalid;
        }

        var verifiedToken = SessionTokens.NewVerifiedToken();
        row.VerifiedAt = now;
        row.VerifiedTokenHash = SessionTokens.HashVerifiedToken(verifiedToken);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await EmitBehavioral("identity.signup_verified", ctx, ct);
        return ChallengeConfirmResult.Confirmed(verifiedToken);
    }

    /// <summary>POST /v1/auth/session (SLICE_S3_CONTRACT.md §1c) — single-step redeem: validates + consumes the login code directly (no separate confirm round trip). Returns the account id on success, null on ANY failure (wrong code, expired, exhausted, unknown email) — one generic Problem for every case.</summary>
    public async Task<string?> RedeemLoginCode(string emailLower, string code, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var row = await db.EmailChallenges
            .FromSqlInterpolated($"SELECT * FROM identity.email_challenges WHERE purpose = {ChallengePurposes.Login} AND email_lower = {emailLower} AND consumed_at IS NULL ORDER BY created_at DESC LIMIT 1 FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var maxAttempts = await config.GetValue<int>(IdentityConfigKeys.EmailCodeMaxAttempts, ct);

        if (row is null || row.ExpiresAt <= now || row.Attempts >= maxAttempts || row.AccountId is null)
        {
            // Same equalization as ConfirmSignupCode above (SREJ-2/MAIL-1, SECURITY_REVIEW_S3.md): pay
            // the keyed-HMAC cost even when there is no pending login challenge to compare against, so
            // "account absent/banned" and "wrong code against a real pending challenge" stop being
            // separable by the HMAC-compute delta alone.
            await EmailCodes.Hash(keyVault, code, ct);
            await tx.RollbackAsync(ct);
            return null;
        }

        var presentedHash = await EmailCodes.Hash(keyVault, code, ct);
        var matches = EmailCodes.FixedTimeEquals(presentedHash, row.CodeHash);
        row.Attempts++;

        if (!matches)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }

        row.ConsumedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return row.AccountId;
    }

    /// <summary>PUT /v1/me/email (SLICE_S3_CONTRACT.md §1c): "email change via the SAME challenge machine (code to the NEW address)". Mirrors <see cref="IssueForSignup"/>'s anti-enumeration shape: if the requested new address already belongs to a DIFFERENT live account, no code is ever sent there and no row is ever persisted — the wire renders the SAME 202 {challengeId} either way, and the actual owner of that mailbox gets the "already registered" mail instead (never told WHO tried it).</summary>
    public async Task<string> IssueForEmailChange(string accountId, string newEmailLower, string locale, RequestContext ctx, CancellationToken ct)
    {
        var quotaAllowed = await ConsumeMailboxQuota(newEmailLower, ctx, ct);
        var now = DateTimeOffset.UtcNow;

        var collidingAccountId = await db.Accounts
            .Where(a => a.Email != null && a.Email == newEmailLower && a.AccountId != accountId && a.TombstonedAt == null)
            .Select(a => a.AccountId)
            .FirstOrDefaultAsync(ct);

        if (collidingAccountId is not null)
        {
            if (quotaAllowed)
            {
                await emailSender.SendAsync(new EmailMessage(newEmailLower, "email.already_registered", locale, EmptyModel), ctx, ct);
            }
            else
            {
                await EmitQuotaDenied(newEmailLower, ctx, ct);
            }

            return MintChallengeId(now);
        }

        await db.EmailChallenges
            .Where(c => c.Purpose == ChallengePurposes.EmailChange && c.AccountId == accountId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        var challengeId = MintChallengeId(now);
        var code = EmailCodes.NewCode();
        var ttlMinutes = await config.GetValue<int>(IdentityConfigKeys.EmailCodeTtlMinutes, ct);

        db.EmailChallenges.Add(new EmailChallengeEntity
        {
            ChallengeId = challengeId,
            Purpose = ChallengePurposes.EmailChange,
            EmailLower = newEmailLower,
            AccountId = accountId,
            CodeHash = await EmailCodes.Hash(keyVault, code, ct),
            Attempts = 0,
            ExpiresAt = now.AddMinutes(ttlMinutes),
            CreatedAt = now,
            Locale = locale,
            Region = ctx.Region.ToString(),
            LawfulBasis = ResolveLawfulBasis(ctx, "identity.email_challenges", "challenge.issued"),
        });
        await db.SaveChangesAsync(ct);

        if (quotaAllowed)
        {
            await emailSender.SendAsync(
                new EmailMessage(newEmailLower, "email.verify_code", locale,
                    new Dictionary<string, string> { ["code"] = code, ["ttlMinutes"] = ttlMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
                ctx, ct);
        }
        else
        {
            await EmitQuotaDenied(newEmailLower, ctx, ct);
        }

        return challengeId;
    }

    /// <summary>
    /// POST /v1/me/email/confirm (SLICE_S3_CONTRACT.md §1c/§1b/§7). Atomic across schema `identity`
    /// (challenge consumption + account.email swap) and schema `core` (the `identity.email_changed` audit
    /// event) via <see cref="IdentityAtomicScope"/> — the SAME cross-schema-tx seam <see
    /// cref="Svac.Identity.Auth.SignupCompletionService"/> uses. The caller sends the old-address security
    /// notice AFTER this returns (outside this tx, exactly like <see cref="RefreshRotationService"/>'s
    /// post-commit mail send) — email delivery is never inside the DB transaction.
    /// </summary>
    public async Task<EmailChangeConfirmResult> ConfirmEmailChange(string accountId, string challengeId, string code, RequestContext ctx, CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("IdentityDbContext has no connection string configured.");
        await using var scope = await Svac.Identity.Persistence.IdentityAtomicScope.OpenAsync(connectionString, ct);

        var row = await scope.Db.EmailChallenges
            .FromSqlInterpolated($"SELECT * FROM identity.email_challenges WHERE challenge_id = {challengeId} AND purpose = {ChallengePurposes.EmailChange} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var maxAttempts = await config.GetValue<int>(IdentityConfigKeys.EmailCodeMaxAttempts, ct);

        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now || row.Attempts >= maxAttempts || row.AccountId != accountId)
        {
            // Same equalization as ConfirmSignupCode/RedeemLoginCode above (SREJ-3/MAIL-1,
            // SECURITY_REVIEW_S3.md).
            await EmailCodes.Hash(keyVault, code, ct);
            await scope.RollbackAsync(ct);
            return EmailChangeConfirmResult.Invalid;
        }

        var presentedHash = await EmailCodes.Hash(keyVault, code, ct);
        var matches = EmailCodes.FixedTimeEquals(presentedHash, row.CodeHash);
        row.Attempts++;

        if (!matches)
        {
            await scope.Db.SaveChangesAsync(ct);
            await scope.CommitAsync(ct);
            return EmailChangeConfirmResult.Invalid;
        }

        var account = await scope.Db.Accounts.SingleAsync(a => a.AccountId == accountId, ct);
        var oldEmail = account.Email ?? string.Empty;
        var newEmail = row.EmailLower;
        account.Email = newEmail;
        row.ConsumedAt = now;

        try
        {
            await scope.Db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation && pg.ConstraintName == "ux_accounts_email")
        {
            // Vanishingly rare race (email uniqueness is already gated at issue time by the collision
            // check above) — the SAME generic Problem every other failure of this challenge machine
            // renders, never a dedicated "email taken" surface (§1c names no such key).
            await scope.RollbackAsync(ct);
            return EmailChangeConfirmResult.Invalid;
        }

        await scope.Events.Append(StreamType.Audit, accountId, "identity.email_changed", "{}", ctx, ExpectedVersion.AnyVersion, ct);
        await scope.CommitAsync(ct);

        return new EmailChangeConfirmResult.SwappedResult(oldEmail, newEmail);
    }

    private async Task<bool> ConsumeMailboxQuota(string emailLower, RequestContext ctx, CancellationToken ct)
    {
        var actor = await EmailQuotaActor.ForMailbox(keyVault, emailLower, ct);
        var quotaContext = new QuotaContext(
            new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
            TimeZoneInfo.Utc,
            TimeOnly.MinValue,
            DateTimeOffset.UtcNow);
        var result = await quotaService.Consume(actor, IdentityQuotaKeys.EmailSendDaily, quotaContext, ct);
        return result is QuotaResult.Ok;
    }

    private async Task EmitQuotaDenied(string emailLower, RequestContext ctx, CancellationToken ct)
    {
        // §5: "the mail just doesn't send (audited on the behavioral stream)" — a quota deny on an
        // anonymous mail surface stays wire-silent; the ONLY trace is this behavioral row. Zero raw email
        // in the payload — the actor's own (already HMAC'd) id is the only mailbox-linked value.
        var actor = await EmailQuotaActor.ForMailbox(keyVault, emailLower, ct);
        await eventStore.Append(StreamType.Behavioral, actor.Id.ToString(), "identity.email_send_quota_denied", "{}", ctx, ExpectedVersion.AnyVersion, ct);
    }

    private async Task EmitBehavioral(string eventType, RequestContext ctx, CancellationToken ct) =>
        await eventStore.Append(StreamType.Behavioral, ctx.Actor.Id.ToString(), eventType, "{}", ctx, ExpectedVersion.AnyVersion, ct);

    private static string MintChallengeId(DateTimeOffset now) => OpaqueId.New(IdPrefixes.Challenge, now, Random.Shared).ToString();

    private static string ResolveLawfulBasis(RequestContext ctx, string store, string eventType) =>
        LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, store, eventType, ctx.Region.ToString());
}
