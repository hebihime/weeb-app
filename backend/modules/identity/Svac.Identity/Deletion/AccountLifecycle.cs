using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.Identity.Config;
using Svac.Identity.Contracts;
using Svac.Identity.Persistence;

namespace Svac.Identity.Deletion;

/// <summary>
/// The real <see cref="IAccountLifecycle"/> (SLICE_S3_CONTRACT.md §1b/§2/§3), replacing the Phase-1
/// throwing stub. The CLOSED transition table: active&lt;-&gt;suspended (Suspend/Reinstate) ·
/// active|suspended-&gt;banned (Ban) · banned-&gt;active (Reinstate) · active|suspended-&gt;deleted ONLY
/// via <see cref="RequestDeletion"/> (Phase L — logical, reversible) · deleted-&gt;active ONLY via <see
/// cref="CancelDeletion"/> inside the grace window · post-purge deleted is terminal (guarded by
/// <c>tombstoned_at IS NULL</c>). Every transition appends <c>identity.account_state_changed</c> to the
/// audit stream in the SAME transaction as the state mutation (the frozen envelope, §1b).
///
/// <see cref="Suspend"/>/<see cref="Ban"/>/<see cref="Reinstate"/> have NO HTTP mapping at S3 (S12 drives
/// them) — there is no policy-middleware chokepoint upstream of these calls, so THIS method is the ONLY
/// enforcement point and must call <see cref="IPolicyEngine.Authorize"/> itself, mirroring
/// <c>PurgePipeline.Run</c>'s own self-enforcement of <c>core.purge.execute</c>. <see
/// cref="RequestDeletion"/>/<see cref="CancelDeletion"/> DO have HTTP mappings
/// (<c>identity.deletion.request</c>/<c>.cancel</c>, DeletionEndpoints) — the middleware chokepoint has
/// already authorized by the time these run, matching every other consumer-facing service in this module
/// (SignupCompletionService, HandleChangeService, ...), so they do not re-authorize.
/// </summary>
public sealed class AccountLifecycle(
    IdentityDbContext db,
    IPolicyEngine policyEngine,
    IConfigRegistry config,
    IEmailSender emailSender,
    IFieldKeyVault keyVault) : IAccountLifecycle
{
    private static readonly IReadOnlyDictionary<string, string> EmptyModel = new Dictionary<string, string>();
    private static readonly string[] AllowedFromActive = { "active" };
    private static readonly string[] AllowedFromSuspendedOrBanned = { "suspended", "banned" };

    public async Task Suspend(OpaqueId accountId, string reasonKey, RequestContext ctx, CancellationToken ct = default)
    {
        await AuthorizeInternalVerb(ctx.Actor, "identity.account.suspend", accountId, ct);
        await Transition(accountId, allowedFrom: AllowedFromActive, to: "suspended", reasonKey, ctx, ct);
    }

    public async Task Reinstate(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default)
    {
        await AuthorizeInternalVerb(ctx.Actor, "identity.account.reinstate", accountId, ct);
        await Transition(accountId, allowedFrom: AllowedFromSuspendedOrBanned, to: "active", reasonKey: null, ctx, ct);
    }

    public async Task Ban(OpaqueId accountId, string reasonKey, RequestContext ctx, CancellationToken ct = default)
    {
        await AuthorizeInternalVerb(ctx.Actor, "identity.account.ban", accountId, ct);

        var id = accountId.ToString();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var account = await db.Accounts
            .FromSqlInterpolated($"SELECT * FROM identity.accounts WHERE account_id = {id} AND account_state IN ('active','suspended') FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var fromState = account.AccountState;
        account.AccountState = "banned";
        account.StateChangedAt = now;

        // Cascade (SLICE_S3_CONTRACT.md §1c/§1b): all sessions + refresh families revoked in-tx (refresh
        // rotation's own liveSession.RevokedAt check makes the family unusable without a separate
        // refresh_tokens mutation); device push tokens cleared. Email-code issuance is ALREADY
        // silently-refused live off account_state='banned' (EmailChallengeMachine.IssueForLogin) — no
        // extra code needed for that cascade cell.
        await db.Sessions.Where(s => s.AccountId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, "state_cascade"), ct);
        await db.Devices.Where(d => d.AccountId == id)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.PushToken, (string?)null), ct);

        await db.SaveChangesAsync(ct);
        await AppendAuditSameTx(db, id, "identity.account_state_changed", BuildEnvelope(id, fromState, "banned", reasonKey, now), ctx, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Phase L (SLICE_S3_CONTRACT.md §2): logical delete + the restricted rights set at request time.
    /// account_state -&gt; 'deleted' + deletion_effective_at = now + grace_days, SAME tx as the
    /// account_state_changed audit event + the deletion_jobs row's creation. Sessions are NOT revoked
    /// (login stays open, restricted by the accountState policy axis, §3b). Idempotent under race via a
    /// row-locked guarded read: the loser of a concurrent double-request simply no-ops (0 side effects) —
    /// the caller (DeletionEndpoints) re-reads the winner's deletion_effective_at either way.
    /// </summary>
    public async Task RequestDeletion(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default)
    {
        var id = accountId.ToString();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // "banned" is allowed here too (OQ-3, §11/§13: a banned account's deletion is a real, ratified
        // GDPR path) even though the CONSUMER-facing HTTP policy row (identity.deletion.request,
        // AllowedAccountStates: ActiveOrSuspended) never lets a banned actor reach this method that way —
        // Ban() already revoked every session, so a banned consumer cannot authenticate to hit the
        // endpoint regardless. This service-level capability exists for a future trusted (staff/S12)
        // caller invoking IAccountLifecycle.RequestDeletion directly, and is exercised today by
        // Svac.Tests.Identity's OQ-3 fixture.
        var account = await db.Accounts
            .FromSqlInterpolated($"SELECT * FROM identity.accounts WHERE account_id = {id} AND account_state IN ('active','suspended','banned') FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var graceDays = await config.GetValue<int>(IdentityConfigKeys.DeletionGraceDays, ct);
        var effectiveAt = now.AddDays(graceDays);
        var fromState = account.AccountState;
        var email = account.Email;
        var locale = account.Locale;

        // OQ-3 (RATIFIED (a), §11/§13): a BANNED account's deletion writes a salted-HMAC email ref (+
        // push-token hash, null if none survives Ban's own push-token clear) to identity.ban_evasion_refs
        // NOW, while the email is still live — capturing it here rather than at the (up to 30-days-later)
        // Phase P purge is deliberate: Phase P tombstones/nulls the email, so this is the last moment the
        // evidence is cheap to gather. lawful_basis is ALWAYS legitimate_interest by the ruling.
        if (fromState == "banned" && email is not null)
        {
            var hmacEmail = await BanEvasionRefs.ComputeHmacRef(keyVault, email, ct);
            if (!await db.BanEvasionRefs.AnyAsync(b => b.HmacEmail == hmacEmail, ct))
            {
                var pushTokenHash = await db.Devices
                    .Where(d => d.AccountId == id && d.PushToken != null)
                    .Select(d => d.PushToken)
                    .FirstOrDefaultAsync(ct);
                byte[]? pushTokenHashBytes = pushTokenHash is null
                    ? null
                    : await BanEvasionRefs.ComputeHmacBytes(keyVault, pushTokenHash, ct);
                db.BanEvasionRefs.Add(new BanEvasionRefEntity
                {
                    HmacEmail = hmacEmail,
                    PushTokenHash = pushTokenHashBytes,
                    BannedAt = now,
                    Region = ctx.Region.ToString(),
                    LawfulBasis = "legitimate_interest",
                });
            }
        }

        account.AccountState = "deleted";
        account.DeletionRequestedAt = now;
        account.DeletionEffectiveAt = effectiveAt;
        account.StateChangedAt = now;

        var deletionId = OpaqueId.New(IdPrefixes.Deletion, now, Random.Shared).ToString();
        db.DeletionJobs.Add(new DeletionJobEntity
        {
            DeletionId = deletionId,
            AccountId = id,
            State = "scheduled",
            RequestedAt = now,
            ScheduledFor = effectiveAt,
            ExportOffered = true,
            Region = ctx.Region.ToString(),
            LawfulBasis = LawfulBasisResolver.Resolve(ctx.LawfulBasisVariant.Key, "identity.deletion_jobs", "deletion.requested", ctx.Region.ToString()),
        });

        await db.SaveChangesAsync(ct);
        await AppendAuditSameTx(db, id, "identity.account_state_changed", BuildEnvelope(id, fromState, "deleted", null, effectiveAt), ctx, ct);
        await AppendAuditSameTx(db, id, "identity.deletion_scheduled", "{}", ctx, ct);
        await AppendBehavioralSameTx(db, id, "identity.deletion_requested", ctx, ct);
        await tx.CommitAsync(ct);

        // The deletion-scheduled email sends NOW (§2/§7) — after commit, over the ordinary DI-scoped
        // IEmailSender, never inside the ambient transaction (mirrors MeEndpoints' email-change notice).
        if (email is not null)
        {
            var model = new Dictionary<string, string> { ["scheduledFor"] = effectiveAt.ToString("O") };
            await emailSender.SendAsync(new EmailMessage(email, "email.deletion_scheduled", locale, model), ctx, ct);
        }
    }

    /// <summary>
    /// The ONE deleted-&gt;active transition (SLICE_S3_CONTRACT.md §2), grace-only: only a currently-
    /// deleted, still-in-grace, NOT-yet-physically-purged row (tombstoned_at IS NULL) can cancel —
    /// post-purge deleted is terminal. State-guarded UPDATE, idempotent under race: a second CancelDeletion
    /// call (or one racing a Phase P purge that already tombstoned the row) affects 0 rows and simply
    /// no-ops, never an error.
    /// </summary>
    public async Task CancelDeletion(OpaqueId accountId, RequestContext ctx, CancellationToken ct = default)
    {
        var id = accountId.ToString();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var account = await db.Accounts
            .FromSqlInterpolated($"SELECT * FROM identity.accounts WHERE account_id = {id} AND account_state = 'deleted' AND tombstoned_at IS NULL AND deletion_effective_at > {now} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        account.AccountState = "active";
        account.DeletionRequestedAt = null;
        account.DeletionEffectiveAt = null;
        account.StateChangedAt = now;

        var scheduledJobs = await db.DeletionJobs.Where(d => d.AccountId == id && d.State == "scheduled").ToListAsync(ct);
        foreach (var job in scheduledJobs)
        {
            job.State = "canceled";
        }

        await db.SaveChangesAsync(ct);
        await AppendAuditSameTx(db, id, "identity.account_state_changed", BuildEnvelope(id, "deleted", "active", null, now), ctx, ct);
        await AppendAuditSameTx(db, id, "identity.deletion_canceled", "{}", ctx, ct);
        await AppendBehavioralSameTx(db, id, "identity.deletion_canceled", ctx, ct);
        await tx.CommitAsync(ct);
    }

    // --- shared machinery -------------------------------------------------------------------------

    private async Task AuthorizeInternalVerb(ActorRef actor, string action, OpaqueId accountId, CancellationToken ct)
    {
        var decision = await policyEngine.Authorize(actor, action, new TargetRef("account", accountId.ToString()), ct);
        if (!decision.IsAllowed)
        {
            throw new UnauthorizedAccessException($"4A denied \"{action}\" for actor {actor} on account {accountId}.");
        }
    }

    /// <summary>Suspend/Reinstate's shared plumbing (Ban has its own cascade-bearing body above). Locks the row unconditionally (FOR UPDATE) then checks <paramref name="allowedFrom"/> in C# — <paramref name="allowedFrom"/> is always a small fixed literal set, never worth a dynamic SQL array.</summary>
    private async Task Transition(OpaqueId accountId, string[] allowedFrom, string to, string? reasonKey, RequestContext ctx, CancellationToken ct)
    {
        var id = accountId.ToString();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var account = await db.Accounts
            .FromSqlInterpolated($"SELECT * FROM identity.accounts WHERE account_id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (account is null || !allowedFrom.Contains(account.AccountState))
        {
            await tx.RollbackAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var fromState = account.AccountState;
        account.AccountState = to;
        account.StateChangedAt = now;

        await db.SaveChangesAsync(ct);
        await AppendAuditSameTx(db, id, "identity.account_state_changed", BuildEnvelope(id, fromState, to, reasonKey, now), ctx, ct);
        await tx.CommitAsync(ct);
    }

    private static string BuildEnvelope(string accountId, string from, string to, string? reasonKey, DateTimeOffset effectiveAt) =>
        JsonSerializer.Serialize(new { account_id = accountId, from, to, reason_key = reasonKey, effective_at = effectiveAt });

    /// <summary>
    /// Appends an event over the SAME ambient transaction as whatever mutation just ran on <paramref
    /// name="db"/> — mirrors RefreshRotationService.EventStoreOverSharedContext exactly (a fresh
    /// CoreDbContext bound to the SAME connection/transaction, never IdentityAtomicScope's brand-new
    /// connection, which would not share this ambient tx).
    /// </summary>
    private static async Task AppendAuditSameTx(IdentityDbContext identityDb, string accountId, string eventType, string payloadJson, RequestContext ctx, CancellationToken ct)
    {
        await AppendSameTx(identityDb, Svac.DomainCore.Contracts.Streams.StreamType.Audit, accountId, eventType, payloadJson, ctx, ct);
    }

    private static async Task AppendBehavioralSameTx(IdentityDbContext identityDb, string accountId, string eventType, RequestContext ctx, CancellationToken ct)
    {
        await AppendSameTx(identityDb, Svac.DomainCore.Contracts.Streams.StreamType.Behavioral, accountId, eventType, "{}", ctx, ct);
    }

    private static async Task AppendSameTx(IdentityDbContext identityDb, StreamType stream, string accountId, string eventType, string payloadJson, RequestContext ctx, CancellationToken ct)
    {
        var coreDb = new Svac.DomainCore.Persistence.CoreDbContext(
            new DbContextOptionsBuilder<Svac.DomainCore.Persistence.CoreDbContext>()
                .UseNpgsql(identityDb.Database.GetDbConnection())
                .Options);
        var currentTx = identityDb.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("AppendSameTx requires an ambient transaction on identityDb.");
        await coreDb.Database.UseTransactionAsync(currentTx, ct);

        var store = new Svac.DomainCore.EventStore.PostgresEventStore(coreDb);
        await store.Append(stream, accountId, eventType, payloadJson, ctx, ExpectedVersion.AnyVersion, ct);
        await coreDb.DisposeAsync();
    }
}
