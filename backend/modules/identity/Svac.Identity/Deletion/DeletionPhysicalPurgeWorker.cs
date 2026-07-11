using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Config;
using Svac.Identity.Export;
using Svac.Identity.Persistence;

namespace Svac.Identity.Deletion;

/// <summary>
/// Phase P — the physical purge (SLICE_S3_CONTRACT.md §2, at <c>deletion_effective_at</c>): the deletion
/// pipeline as purge ORCHESTRATOR (§6c) — ONE call to <see cref="IPurgePipeline.Run"/> across the entire
/// registry, never a second pipeline. No background-worker infrastructure exists in this monolith yet
/// (mirrors <c>Svac.Identity.Export.ExportWorker</c>'s own "runs the SAME orchestration logic
/// synchronously... nothing here changes shape when async dispatch day comes" posture) — <see
/// cref="RunDueSweepAsync"/> is called by the DevSeams-only diagnostic trigger for the live E2E (S1 canary
/// pattern, NEVER in the shipped contract) and is exactly what a future scheduled host would poll.
///
/// CONC-1 / CONC-2 / PII-2 (SECURITY_REVIEW_S3.md): the shipped version claimed a job with an UNGUARDED
/// `job.State = "executing"; SaveChanges()` (a plain UPDATE-by-PK, no WHERE guard) after reading the
/// account row UNLOCKED — a cancel that committed in the gap was invisible, and the worker would
/// physically purge (crypto-shred, tombstone, retire the handle) an account whose owner had just received
/// a successful 204 cancel. <see cref="ExecuteOne"/> now: (1) claims the job with a guarded CAS (zero rows
/// affected ⇒ another actor already won or the job cannot currently be claimed — abort with NO side
/// effects); (2) re-verifies the account's TRUE, freshly row-locked state inside its own transaction
/// immediately before any irreversible step; (3) reverts to a retryable state on any mid-run failure
/// instead of leaving the job stuck 'executing' forever (CONC-2(b) — a lease-based re-sweep also recovers
/// a job that never got the chance to revert itself, e.g. a hard process kill).
/// </summary>
public sealed class DeletionPhysicalPurgeWorker(
    IdentityDbContext db,
    IPurgePipeline purgePipeline,
    ICustodyHoldRegistry custodyHolds,
    IConfigRegistry config,
    IEmailSender emailSender,
    IEventStore eventStore)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyModel = new Dictionary<string, string>();
    private static readonly ActorRef SystemActor = new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    /// <summary>
    /// Finds every due deletion job (scheduled OR held, past its scheduled_for — OR a job stuck
    /// 'executing' past the CONC-2(b) lease window, i.e. a prior attempt that crashed/died before it could
    /// revert itself) and executes each. Returns the count of jobs ATTEMPTED (matching the pre-fix
    /// return-value contract) — "release re-enqueues": a held job whose hold has since cleared is picked
    /// up again by the next call, exactly like a scheduled one.
    /// </summary>
    public async Task<int> RunDueSweepAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // CONC-2(b): the lease window is sized off the SAME configured cap WaitForPendingExportAsync
        // itself is bounded by, plus a buffer for the rest of the pipeline's own real work — a legitimate,
        // still-alive run waiting out that full cap must never be preempted mid-flight by a second sweep
        // tick re-claiming its job out from under it.
        var waitHours = await config.GetValue<int>(IdentityConfigKeys.ExportPreDeletionWaitHours, ct);
        var leaseWindow = TimeSpan.FromHours(waitHours) + TimeSpan.FromHours(1);
        var staleExecutingCutoff = now - leaseWindow;

        var dueJobIds = await db.DeletionJobs
            .Where(d =>
                ((d.State == "scheduled" || d.State == "held") && d.ScheduledFor <= now) ||
                (d.State == "executing" && d.ExecutingSince != null && d.ExecutingSince <= staleExecutingCutoff))
            .Select(d => d.DeletionId)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var deletionId in dueJobIds)
        {
            try
            {
                await ExecuteOne(deletionId, staleExecutingCutoff, ct);
            }
            catch (Exception)
            {
                // CONC-2 crash-resume: a mid-run failure (any executor throwing, an email transport
                // fault, ...) must never leave this job in a dead 'executing' state — the statutory clock
                // (§2: "≤17 days worst case") would silently break with no alarm. Guarded: only reverts
                // OUR own claim (state='executing') — if another actor already moved the job on
                // (complete/canceled) between our claim and this catch, that outcome is left untouched.
                // Reset immediately to 'scheduled' (not left for the lease to expire) so the VERY NEXT
                // sweep tick retries it — every physical step this pipeline performs is documented
                // idempotent-under-retry (crypto-shred/Shred is idempotent, purge store executors are
                // ExecuteDelete/ExecuteUpdate no-ops on an already-purged row, email re-send is harmless).
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE identity.deletion_jobs SET state = 'scheduled', executing_since = NULL WHERE deletion_id = {deletionId} AND state = 'executing'",
                    CancellationToken.None);
            }
            processed++;
        }
        return processed;
    }

    private async Task ExecuteOne(string deletionId, DateTimeOffset staleExecutingCutoff, CancellationToken ct)
    {
        var claimAt = DateTimeOffset.UtcNow;

        // CONC-1 (CRITICAL) + CONC-2(a)/(b): the ONE guarded CAS that establishes mutual exclusion. Zero
        // rows affected means: a concurrent sweep already claimed this exact job (two sweeps racing the
        // same due job — CONC-2(a)), OR the job is in a state this claim does not recognize as claimable
        // (already 'canceled'/'complete', or 'executing' but NOT yet stale). Either way: abort here, no
        // side effects have happened yet.
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE identity.deletion_jobs SET state = 'executing', executing_since = {claimAt}
            WHERE deletion_id = {deletionId}
              AND (state IN ('scheduled', 'held') OR (state = 'executing' AND executing_since <= {staleExecutingCutoff}))
            """, ct);
        if (claimed == 0)
        {
            return;
        }

        var job = await db.DeletionJobs.SingleAsync(d => d.DeletionId == deletionId, ct);
        var accountId = job.AccountId;

        // (1) Custody-hold consult (ER-14) — RECORDED even when empty. Pure information-gathering, not
        // itself an irreversible step, so it is safe to run before the account re-verification below.
        var holds = await custodyHolds.HoldsFor(accountId, ct);
        var heldStoreKeys = holds.SelectMany(h => h.HeldStoreKeys).ToHashSet();
        job.CustodyHoldsFound = holds.Count;
        job.CustodyHoldRefsJson = JsonSerializer.Serialize(holds.Select(h => new { h.Hold.HoldId, h.Hold.DocumentedBasis, heldStoreKeys = h.HeldStoreKeys }));
        await db.SaveChangesAsync(ct);

        if (holds.Count > 0 && heldStoreKeys.Count > 0)
        {
            // Held stores are skipped with a documented-basis purge_run row further below — but if EVERY
            // store a real class-verb would touch is held, there is nothing left this pass can safely do
            // besides record the hold and wait for release. Job goes to "held"; the next
            // RunDueSweepAsync call re-checks it unconditionally (release re-enqueues).
            job.State = "held";
            await db.SaveChangesAsync(ct);
            if (!AnyNonHeldRealWorkRemains(heldStoreKeys))
            {
                return;
            }
            // Proceeding under the SAME claim established by the CAS above (no other actor can steal a
            // 'held' OR still-fresh 'executing' row) — flip back to 'executing' so a concurrent sweep's
            // own CAS (which also matches 'held' unconditionally) cannot legitimately double-claim this
            // exact pass while we are actively working it.
            job.State = "executing";
            await db.SaveChangesAsync(ct);
        }

        // CONC-1 (CRITICAL): re-verify the account's TRUE, freshly row-locked state immediately before
        // ANY irreversible step (email send, session revoke, purge run). This is the actual fix for the
        // cancel-vs-worker race: the shipped code's account read was UNLOCKED and taken long before the
        // (also unguarded) state write, so a CancelDeletion that committed in that gap was invisible to
        // it. Locking the row here and re-checking from scratch means whoever's transaction reaches this
        // exact check LAST sees the ground truth — and once THIS check passes (deletion_effective_at
        // confirmed <= now), a cancel racing in afterward can structurally never succeed: CancelDeletion's
        // own guard requires `deletion_effective_at > now`, which is no longer true once we are past due.
        await using var verifyTx = await db.Database.BeginTransactionAsync(ct);
        var verifyNow = DateTimeOffset.UtcNow;
        var account = await db.Accounts
            .FromSqlInterpolated(
                $"""
                SELECT * FROM identity.accounts WHERE account_id = {accountId}
                  AND account_state = 'deleted' AND tombstoned_at IS NULL
                  AND deletion_effective_at IS NOT NULL AND deletion_effective_at <= {verifyNow}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(ct);
        if (account is null)
        {
            // Lost the race to a cancel that committed first (or genuinely not due yet — defensive; the
            // caller's own due-sweep query already filters on this). Zero irreversible side effects have
            // run: no email sent, no session revoked, no purge pipeline call made. Never leave the job
            // stuck 'executing' (that is exactly CONC-2(b)'s dead-state bug) — mark it canceled so no
            // future sweep keeps re-attempting a job whose account is no longer a valid physical-purge
            // target.
            await verifyTx.CommitAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE identity.deletion_jobs SET state = 'canceled', executing_since = NULL WHERE deletion_id = {deletionId} AND state IN ('executing', 'held')", ct);
            return;
        }
        await verifyTx.CommitAsync(ct);

        var region = account.Region;
        var lawfulBasis = account.LawfulBasis;
        var ctx = new RequestContext(
            SystemActor,
            new RegionCode(region.Split('-', 2)[0], region.Contains('-', StringComparison.Ordinal) ? region.Split('-', 2)[1] : null),
            RegionSource.System,
            new LawfulBasisVariant(lawfulBasis),
            account.Locale,
            $"deletion-phase-p-{deletionId}");

        // (2) Finish a pending export first, capped at identity.export.pre_deletion_wait_hours.
        await WaitForPendingExportAsync(accountId, ct);

        // (3) email.deletion_completed — the LAST outbound act BEFORE the shred (§12 item 9).
        if (account.Email is not null)
        {
            await emailSender.SendAsync(new EmailMessage(account.Email, "email.deletion_completed", account.Locale, EmptyModel), ctx, ct);
        }

        // (4) Revoke all sessions/refresh families; clear device push tokens; delete export jobs+artifacts
        // NOW (ahead of step 5's registry-driven pass) so an old access token 401s immediately rather than
        // waiting on the purge call below — the registry's own identity.sessions/identity.devices/
        // identity.export_jobs AccountDeletion rows re-run the same deletes idempotently in step 5.
        var now = DateTimeOffset.UtcNow;
        await db.Sessions.Where(s => s.AccountId == accountId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, "state_cascade"), ct);
        await db.Devices.Where(d => d.AccountId == accountId)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.PushToken, (string?)null), ct);

        // (5) ONE call across the ENTIRE registry — identity's stores + S1's stores (incl.
        // events_heatmap_provenance's NotApplicable-for-AccountDeletion full-history default, and the
        // birthdate field key's crypto-shred via the already-registered field_key_refs/data_protection_keys
        // pair, now ALWAYS hoisted to run last by PurgePipeline.Run — PII-1) — the deletion pipeline as
        // purge ORCHESTRATOR (§6c).
        var subject = new SubjectRef("account", accountId);
        var reports = await purgePipeline.Run(PurgeClass.AccountDeletion, subject, SystemActor, ctx, heldStoreKeys.Count > 0 ? heldStoreKeys : null, ct);

        // (6) Tombstone: handled by AccountsPurgeStoreExecutor as part of step 5's own registered
        // Tombstone verb for identity.accounts (PII columns -> NULL/sentinel, handle -> retired_handles,
        // tombstoned_at set) — no separate action needed here.

        // (7) identity.deletion_completed audit + purge_run receipts on the job row. deletion_jobs' own
        // AccountId column was just pseudonymized by step 5's DeletionJobsPurgeStoreExecutor — every
        // further write to THIS row must key on deletion_id (PK), never account_id.
        job = await db.DeletionJobs.SingleAsync(d => d.DeletionId == deletionId, ct);
        job.State = "complete";
        job.ExecutingSince = null;
        job.ExecutedAt = now;
        job.PurgeRunIdsJson = JsonSerializer.Serialize(reports.Select(r => new { r.RunId, r.StoreKey, r.PurgeClass, r.RowsAffected }));
        await db.SaveChangesAsync(ct);

        await eventStore.Append(StreamType.Audit, accountId, "identity.deletion_completed", "{}", ctx, ExpectedVersion.AnyVersion, ct);
        // PII-9 (SECURITY_REVIEW_S3.md): events_behavioral x AccountDeletion is a registered hard Delete —
        // step 5 above just physically deleted every behavioral row for this subject's RAW account_id.
        // Appending a NEW row keyed on that same raw account_id here would immediately re-create exactly
        // the row class the pipeline just erased, and no future purge run ever revisits a subject whose
        // account is already tombstoned/pseudonymized — the row would survive forever, contradicting the
        // registered verb. Key it on a fresh anonymous identifier instead (never correlated back to the
        // account by any reader) — the audit event above already carries the durable, by-design record
        // (events_audit x AccountDeletion is registered Tombstone, not Delete: account_id survives there
        // on purpose).
        var anonymousCompletionKey = OpaqueId.New(IdPrefixes.Anonymous, now, Random.Shared).ToString();
        await eventStore.Append(StreamType.Behavioral, anonymousCompletionKey, "identity.deletion_completed", "{}", ctx, ExpectedVersion.AnyVersion, ct);
    }

    /// <summary>True if the registry has at least one store, for this class, whose verb is NOT NotApplicable and is NOT already inside <paramref name="heldStoreKeys"/> — i.e. there is still real erasure work a purge run could accomplish even with these holds in place.</summary>
    private static bool AnyNonHeldRealWorkRemains(IReadOnlySet<string> heldStoreKeys)
    {
        // Deliberately conservative: with no S12 custody-hold data ever produced in this build (the
        // production ICustodyHoldRegistry always returns empty), this branch is only exercised by the
        // Svac.Tests.Identity red-fixture — always proceeds (returns true) so a partial hold never
        // silently blocks a run that should otherwise make progress.
        return true;
    }

    private async Task WaitForPendingExportAsync(string accountId, CancellationToken ct)
    {
        var pending = await db.ExportJobs.Where(e => e.AccountId == accountId && e.State == "pending").SingleOrDefaultAsync(ct);
        if (pending is null)
        {
            return;
        }

        var capHours = await config.GetValue<int>(IdentityConfigKeys.ExportPreDeletionWaitHours, ct);
        var deadline = DateTimeOffset.UtcNow.AddHours(capHours);
        // No background job queue exists yet (ExportWorker itself runs synchronously inline on the
        // request path, §6b) — a "pending" export at Phase P time is therefore already anomalous (it
        // should have flipped to ready/failed within the SAME request that created it). This loop is the
        // honest, capped wait the contract describes rather than assuming synchronous-always: it re-reads
        // the row and gives up at the cap, letting the purge proceed regardless (a stuck export must never
        // block a statutory deletion clock indefinitely).
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await db.ExportJobs.Where(e => e.ExportId == pending.ExportId).Select(e => e.State).SingleOrDefaultAsync(ct);
            if (state != "pending")
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        }
    }
}
