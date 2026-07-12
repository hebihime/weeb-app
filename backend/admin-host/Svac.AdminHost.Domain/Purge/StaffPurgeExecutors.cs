using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts.Purge;

namespace Svac.AdminHost.Domain.Purge;

// Two IPurgeStoreExecutor implementations (SLICE_S5_CONTRACT.md §6, StaffPurgeTests.cs's own
// INTERFACE-SKETCH: "which is Pass B/D's deliverable") — AdminPurgeRegistrySource already registers both
// storeKeys with their declared verbs; these are what make those declared verbs real.

/// <summary>
/// admin.staff_accounts (SLICE_S5_CONTRACT.md §6) — StatutoryErasure AND RetentionExpiry both declare
/// Pseudonymize: one executor, both classes land here (PurgePipeline never distinguishes purgeClass for a
/// Pseudonymize verb beyond feeding it into the keyed re-key). email/external_subject/display_name are
/// re-keyed via the SAME <see cref="PurgePseudonymizer.Pseudonymize"/> every other module's Pseudonymize
/// verb uses (cross-store-correlatable for a key holder, irreversible for everyone else); the stf_ id and
/// status are NEVER touched — "so every audit chain still resolves" (§6) means the id an
/// admin.action.executed/config.set event's actor_ref names must still resolve to a real row.
/// </summary>
public sealed class StaffAccountsPurgeStoreExecutor(AdminDbContext adminDb) : IPurgeStoreExecutor
{
    public string StoreKey => "admin.staff_accounts";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Pseudonymize)
        {
            return 0;
        }

        var staffId = subject.ResourceId;
        var row = await adminDb.StaffAccounts.SingleOrDefaultAsync(s => s.Id == staffId, ct);
        if (row is null)
        {
            return 0;
        }

        // Each field re-keys against its OWN original value, so the three pseudonyms differ from one
        // another (distinct HMAC messages) — never a single shared token that would collapse three
        // distinct PII columns onto one value.
        row.Email = PurgePseudonymizer.Pseudonymize(row.Email, purgeClass, pseudonymizeHmacKey);
        row.ExternalSubject = PurgePseudonymizer.Pseudonymize(row.ExternalSubject, purgeClass, pseudonymizeHmacKey);
        row.DisplayName = PurgePseudonymizer.Pseudonymize(row.DisplayName, purgeClass, pseudonymizeHmacKey);
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await adminDb.SaveChangesAsync(ct);
        return 1;
    }
}

/// <summary>
/// admin.staff_role_grants (SLICE_S5_CONTRACT.md §6) — StatutoryErasure declares Tombstone: reason texts
/// tombstoned on every row the erased subject appears on (as staff_id, granted_by, OR revoked_by), and the
/// grantor/revoker REF itself tombstoned wherever it names the erased subject. The grant/revoke STRUCTURE
/// (role, granted_at/revoked_at, staff_id itself) survives for accountability, per §6's own text.
/// </summary>
public sealed class StaffRoleGrantsPurgeStoreExecutor(AdminDbContext adminDb) : IPurgeStoreExecutor
{
    private const string Tombstoned = "[tombstoned]";

    public string StoreKey => "admin.staff_role_grants";

    public async Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default)
    {
        if (verb != PurgeVerb.Tombstone)
        {
            return 0;
        }

        var subjectId = subject.ResourceId;
        var rows = await adminDb.StaffRoleGrants
            .Where(g => g.StaffId == subjectId || g.GrantedBy == subjectId || g.RevokedBy == subjectId)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var row in rows)
        {
            // Free-text reasons about this subject's OWN grant/revoke — tombstoned regardless of which
            // ref column matched, since the reason text is where a subject's PII is most likely to
            // actually appear as prose (§6: "reason texts ... tombstoned WHERE THE ERASED SUBJECT
            // APPEARS").
            row.GrantReason = Tombstoned;
            if (row.RevokeReason is not null)
            {
                row.RevokeReason = Tombstoned;
            }

            // The grantor/revoker REF itself, tombstoned only where it actually names the erased subject
            // — a grant this subject GAVE to (or revoked from) someone ELSE still names that OTHER
            // person's staff_id untouched; only the erased subject's own ref is ever replaced.
            if (row.GrantedBy == subjectId)
            {
                row.GrantedBy = Tombstoned;
            }
            if (row.RevokedBy == subjectId)
            {
                row.RevokedBy = Tombstoned;
            }
        }

        await adminDb.SaveChangesAsync(ct);
        return rows.Count;
    }
}
