using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Purge;
using Svac.Identity.Config;
using Svac.Identity.Persistence;

namespace Svac.Identity.Purge;

// Ten IPurgeStoreExecutor implementations (SLICE_S3_CONTRACT.md §6a/§6c): every identity table whose
// registry cell is ever something other than NotApplicable. identity.reserved_handles,
// identity.retired_handles, and identity.ban_evasion_refs need none — every one of their registered
// cells is NotApplicable, and PurgePipeline never calls a store's executor for a NotApplicable verb.

/// <summary>
/// identity.email_challenges (SLICE_S3_CONTRACT.md §6a) — THE S2-scar exemplar: keyed by email, not
/// account_id. Matches BOTH login/email_change rows (which carry account_id) AND signup rows (which
/// never do, by design, §1g) via the account's own CURRENT email — which is why
/// <see cref="IdentityPurgeRegistrySource"/> deliberately orders this store's registry rows BEFORE
/// identity.accounts' own (PurgePipeline.Run now walks the registry in first-occurrence order): this
/// executor always runs while the account's email column is still live.
/// </summary>
public sealed class EmailChallengesPurgeStoreExecutor(IdentityDbContext db, IConfigRegistry config) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.email_challenges";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        var email = await db.Accounts.Where(a => a.AccountId == accountId).Select(a => a.Email).SingleOrDefaultAsync(ct);

        IQueryable<EmailChallengeEntity> query = db.EmailChallenges
            .Where(c => c.AccountId == accountId || (email != null && c.EmailLower == email));

        if (purgeClass == PurgeClass.RetentionExpiry)
        {
            var retentionHours = await config.GetValue<int>(IdentityConfigKeys.EmailChallengeRetentionHours, ct);
            var cutoff = DateTimeOffset.UtcNow.AddHours(-retentionHours);
            query = query.Where(c => c.CreatedAt <= cutoff);
        }

        return await query.ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// identity.accounts (SLICE_S3_CONTRACT.md §6a/§2): Tombstone for AccountDeletion/StatutoryErasure (PII
/// columns -> NULL/sentinel, state pinned 'deleted', handle moved to identity.retired_handles);
/// hard Delete for MinorPurge (no tombstone identity survives). The birthdate field key itself is
/// crypto-shredded by the ALREADY-registered global field_key_refs/data_protection_keys CryptoShred cells
/// (they iterate every FieldEncryptionPurpose for subject.ResourceId — no separate registration needed
/// here, since accounts.AccountId IS the same subject id those cells key on).
/// </summary>
public sealed class AccountsPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.accounts";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        var accountId = subject.ResourceId;

        if (verb == PurgeVerb.Delete)
        {
            return await db.Accounts.Where(a => a.AccountId == accountId).ExecuteDeleteAsync(ct);
        }

        if (verb != PurgeVerb.Tombstone)
        {
            return 0;
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.AccountId == accountId, ct);
        if (account is null)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var originalHandle = account.Handle;

        if (!await db.RetiredHandles.AnyAsync(h => h.Handle == originalHandle, ct))
        {
            db.RetiredHandles.Add(new RetiredHandleEntity { Handle = originalHandle, RetiredAt = now });
        }

        account.Email = null;
        account.FandomTag = string.Empty;
        account.AvatarRef = null;
        // Handle is NOT NULL at the schema level (§2) — a stable, non-PII sentinel replaces the human-
        // chosen value; ux_accounts_handle's partial filter (WHERE account_state <> 'deleted') already
        // frees the ORIGINAL handle for re-registration once state='deleted', independent of this column.
        account.Handle = $"deleted_{accountId}";
        account.AccountState = "deleted";
        account.TombstonedAt = now;
        account.StateChangedAt = now;

        await db.SaveChangesAsync(ct);
        return 1;
    }
}

/// <summary>identity.sessions (SLICE_S3_CONTRACT.md §6a) — hard delete for the three erasure classes; age-gated GC for RetentionExpiry ("expired/revoked GC past 30 days").</summary>
public sealed class SessionsPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    private static readonly TimeSpan RetentionFloor = TimeSpan.FromDays(30);

    public string StoreKey => "identity.sessions";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        IQueryable<SessionEntity> query = db.Sessions.Where(s => s.AccountId == accountId);

        if (purgeClass == PurgeClass.RetentionExpiry)
        {
            var cutoff = DateTimeOffset.UtcNow - RetentionFloor;
            query = query.Where(s => (s.RevokedAt != null && s.RevokedAt <= cutoff) || s.AccessExpiresAt <= cutoff);
        }

        return await query.ExecuteDeleteAsync(ct);
    }
}

/// <summary>identity.refresh_tokens (SLICE_S3_CONTRACT.md §6a) — a SECOND S2-scar analogue: keyed by session_id, not account_id. Resolved via a join through identity.sessions.</summary>
public sealed class RefreshTokensPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    private static readonly TimeSpan RetentionFloor = TimeSpan.FromDays(30);

    public string StoreKey => "identity.refresh_tokens";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        var sessionIds = db.Sessions.Where(s => s.AccountId == accountId).Select(s => s.SessionId);

        IQueryable<RefreshTokenEntity> query = db.RefreshTokens.Where(r => sessionIds.Contains(r.SessionId));

        if (purgeClass == PurgeClass.RetentionExpiry)
        {
            var cutoff = DateTimeOffset.UtcNow - RetentionFloor;
            query = query.Where(r => r.ExpiresAt <= cutoff);
        }

        return await query.ExecuteDeleteAsync(ct);
    }
}

/// <summary>identity.devices (+ push tokens) (SLICE_S3_CONTRACT.md §6a) — hard delete for the three erasure classes; no independent RetentionExpiry ceiling declared.</summary>
public sealed class DevicesPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.devices";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        return await db.Devices.Where(d => d.AccountId == accountId).ExecuteDeleteAsync(ct);
    }
}

/// <summary>identity.push_category_consents (SLICE_S3_CONTRACT.md §6a) — a rebuildable projection; Delete for every reachable class (AccountDeletion/StatutoryErasure/MinorPurge/ConsentRevocation all resolve to the same hard delete — a Replay over events_consent recomputes any still-valid grants).</summary>
public sealed class PushCategoryConsentsPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.push_category_consents";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        return await db.PushCategoryConsents.Where(p => p.AccountId == accountId).ExecuteDeleteAsync(ct);
    }
}

/// <summary>identity.consent_current (SLICE_S3_CONTRACT.md §6a) — same rebuildable-projection shape as push_category_consents.</summary>
public sealed class ConsentCurrentPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.consent_current";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        return await db.ConsentCurrent.Where(c => c.AccountId == accountId).ExecuteDeleteAsync(ct);
    }
}

/// <summary>identity.handle_history (SLICE_S3_CONTRACT.md §6a) — Pseudonymize (HMAC re-key account_id) for AccountDeletion/StatutoryErasure; hard Delete for MinorPurge; age-gated sweep for RetentionExpiry via identity.handle_history.retention_months.</summary>
public sealed class HandleHistoryPurgeStoreExecutor(IdentityDbContext db, IConfigRegistry config) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.handle_history";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        var accountId = subject.ResourceId;

        switch (verb)
        {
            case PurgeVerb.Delete:
            {
                if (purgeClass == PurgeClass.RetentionExpiry)
                {
                    var retentionMonths = await config.GetValue<int>(IdentityConfigKeys.HandleHistoryRetentionMonths, ct);
                    var cutoff = DateTimeOffset.UtcNow.AddMonths(-retentionMonths);
                    return await db.HandleHistory.Where(h => h.ChangedAt <= cutoff).ExecuteDeleteAsync(ct);
                }
                return await db.HandleHistory.Where(h => h.AccountId == accountId).ExecuteDeleteAsync(ct);
            }
            case PurgeVerb.Pseudonymize:
            {
                var rows = await db.HandleHistory.Where(h => h.AccountId == accountId).ToListAsync(ct);
                if (rows.Count == 0)
                {
                    return 0;
                }
                var pseudonym = PurgePseudonymizer.Pseudonymize(accountId, purgeClass, pseudonymizeHmacKey);
                foreach (var row in rows)
                {
                    row.AccountId = pseudonym;
                }
                await db.SaveChangesAsync(ct);
                return rows.Count;
            }
            default:
                return 0;
        }
    }
}

/// <summary>identity.export_jobs (incl. artifact bytea) (SLICE_S3_CONTRACT.md §6a) — hard delete incl. artifact for the three erasure classes (the artifact contains the whole PII corpus); age-gated sweep for RetentionExpiry (identity.export.link_ttl_hours — deletes rows past their own expires_at).</summary>
public sealed class ExportJobsPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.export_jobs";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Delete)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        IQueryable<ExportJobEntity> query = db.ExportJobs.Where(e => e.AccountId == accountId);

        if (purgeClass == PurgeClass.RetentionExpiry)
        {
            var now = DateTimeOffset.UtcNow;
            query = query.Where(e => e.ExpiresAt != null && e.ExpiresAt <= now);
        }

        return await query.ExecuteDeleteAsync(ct);
    }
}

/// <summary>identity.deletion_jobs (SLICE_S3_CONTRACT.md §6a) — the receipt SURVIVES every erasure class: Pseudonymize (HMAC re-key account_id) for AccountDeletion/StatutoryErasure/MinorPurge, mirroring purge_runs' own subject-pseudonymization posture. Matched by PK (deletion_id), never by account_id, at every OTHER call site once this executor has run — see DeletionPhysicalPurgeWorker.</summary>
public sealed class DeletionJobsPurgeStoreExecutor(IdentityDbContext db) : IPurgeStoreExecutor
{
    public string StoreKey => "identity.deletion_jobs";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Pseudonymize)
        {
            return 0;
        }

        var accountId = subject.ResourceId;
        var rows = await db.DeletionJobs.Where(d => d.AccountId == accountId).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        var pseudonym = PurgePseudonymizer.Pseudonymize(accountId, purgeClass, pseudonymizeHmacKey);
        foreach (var row in rows)
        {
            row.AccountId = pseudonym;
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
