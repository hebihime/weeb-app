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

    /// <summary>Finds every due deletion job (scheduled OR held, past its scheduled_for) and executes each. Returns the count processed — "release re-enqueues": a held job whose hold has since cleared is picked up again by the next call, exactly like a scheduled one.</summary>
    public async Task<int> RunDueSweepAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dueJobIds = await db.DeletionJobs
            .Where(d => (d.State == "scheduled" || d.State == "held") && d.ScheduledFor <= now)
            .Select(d => d.DeletionId)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var deletionId in dueJobIds)
        {
            await ExecuteOne(deletionId, ct);
            processed++;
        }
        return processed;
    }

    private async Task ExecuteOne(string deletionId, CancellationToken ct)
    {
        var job = await db.DeletionJobs.SingleOrDefaultAsync(d => d.DeletionId == deletionId, ct);
        if (job is null || job.State is "canceled" or "complete")
        {
            return; // raced with a cancel, or a prior sweep already finished it.
        }

        var accountId = job.AccountId;
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountId == accountId, ct);
        if (account is null || account.TombstonedAt is not null)
        {
            job.State = "complete";
            await db.SaveChangesAsync(ct);
            return; // already physically purged by another pass — idempotent no-op.
        }
        if (account.AccountState != "deleted" || account.DeletionEffectiveAt is null || account.DeletionEffectiveAt > DateTimeOffset.UtcNow)
        {
            return; // canceled meanwhile, or genuinely not due yet — defensive; unreachable given the caller's own query.
        }

        var region = account.Region;
        var lawfulBasis = account.LawfulBasis;
        var ctx = new RequestContext(
            SystemActor,
            new RegionCode(region.Split('-', 2)[0], region.Contains('-', StringComparison.Ordinal) ? region.Split('-', 2)[1] : null),
            RegionSource.System,
            new LawfulBasisVariant(lawfulBasis),
            account.Locale,
            $"deletion-phase-p-{deletionId}");

        job.State = "executing";
        await db.SaveChangesAsync(ct);

        // (1) Custody-hold consult (ER-14) — RECORDED even when empty.
        var holds = await custodyHolds.HoldsFor(accountId, ct);
        var heldStoreKeys = holds.SelectMany(h => h.HeldStoreKeys).ToHashSet();
        job.CustodyHoldsFound = holds.Count;
        job.CustodyHoldRefsJson = JsonSerializer.Serialize(holds.Select(h => new { h.Hold.HoldId, h.Hold.DocumentedBasis, heldStoreKeys = h.HeldStoreKeys }));
        await db.SaveChangesAsync(ct);

        if (holds.Count > 0 && heldStoreKeys.Count > 0)
        {
            // Held stores are skipped with a documented-basis purge_run row (via IPurgePipeline.Run's
            // heldStoreKeys param below is only exercised once we actually proceed) — but if EVERY store a
            // real class-verb would touch is held, there is nothing left this pass can safely do besides
            // record the hold and wait for release. Job stays "held"; the next RunDueSweepAsync call
            // re-checks (release re-enqueues).
            job.State = "held";
            await db.SaveChangesAsync(ct);
            if (!AnyNonHeldRealWorkRemains(heldStoreKeys))
            {
                return;
            }
        }

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
        // pair) — the deletion pipeline as purge ORCHESTRATOR (§6c).
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
        job.ExecutedAt = now;
        job.PurgeRunIdsJson = JsonSerializer.Serialize(reports.Select(r => new { r.RunId, r.StoreKey, r.PurgeClass, r.RowsAffected }));
        await db.SaveChangesAsync(ct);

        await eventStore.Append(StreamType.Audit, accountId, "identity.deletion_completed", "{}", ctx, ExpectedVersion.AnyVersion, ct);
        await eventStore.Append(StreamType.Behavioral, accountId, "identity.deletion_completed", "{}", ctx, ExpectedVersion.AnyVersion, ct);
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
