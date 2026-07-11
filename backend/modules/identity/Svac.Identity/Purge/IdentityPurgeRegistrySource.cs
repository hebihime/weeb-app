using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Purge;

namespace Svac.Identity.Purge;

/// <summary>
/// Identity's own additive slice of the 13A registry (SLICE_S3_CONTRACT.md §6a) — every store
/// <c>IdentityDbContext</c> owns, registered against every closed purge class with a verb.
/// <c>identity.reserved_handles</c> and <c>identity.ban_evasion_refs</c> are NotApplicable across the
/// board (zero personal data; retained-by-design evidence respectively) and therefore need no <see
/// cref="Svac.DomainCore.Contracts.Purge.IPurgeStoreExecutor"/> at all — <see
/// cref="Svac.DomainCore.Purge.PurgePipeline"/> never calls a store's executor for a NotApplicable cell.
///
/// ORDER IS LOAD-BEARING (§6a's S2 scar) — TWO dependency pairs, both declared "dependency FIRST":
/// (1) <c>identity.email_challenges</c> before <c>identity.accounts</c> — <see cref="PurgePipeline.Run"/>
/// now iterates the registry's entries in first-occurrence order, so the email-keyed challenge purge
/// (which reads the account's still-live email to find signup-purpose rows that carry no account_id)
/// always runs before <c>identity.accounts</c>' Tombstone verb NULLs that same email column.
/// (2) <c>identity.refresh_tokens</c> before <c>identity.sessions</c> — refresh_tokens is keyed by
/// session_id, not account_id (its own S2-scar analogue), and <see
/// cref="Svac.Identity.Purge.RefreshTokensPurgeStoreExecutor"/> resolves its rows via a join THROUGH
/// identity.sessions (<c>Sessions.Where(AccountId==id).Select(SessionId)</c>) — that join must run before
/// identity.sessions' own Delete verb removes the very session rows it depends on (caught by
/// <c>PurgeCompletenessIdentityTests</c>'s red-fixture run before this ordering was fixed).
/// </summary>
public sealed class IdentityPurgeRegistrySource : IPurgeRegistrySource
{
    public IReadOnlyList<PurgeRegistrationEntry> Entries { get; } = BuildEntries();

    private static List<PurgeRegistrationEntry> BuildEntries()
    {
        var entries = new List<PurgeRegistrationEntry>();

        void Add(string storeKey, PurgeClass purgeClass, PurgeVerb verb, string reason) =>
            entries.Add(new PurgeRegistrationEntry(storeKey, purgeClass, verb, reason));

        // --- identity.email_challenges FIRST (see class doc — the S2-scar ordering dependency) ---
        Add("identity.email_challenges", PurgeClass.AccountDeletion, PurgeVerb.Delete, "keyed by email, not account_id (§6a S2 scar) — hard delete every row matching the account's own account_id (login/email_change) or its still-live email (signup)");
        Add("identity.email_challenges", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("identity.email_challenges", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete; the in-tx under-13 hard delete already happens PRE-pipeline at signup refusal (§1g) — this cell covers a LATER minor-purge trigger (e.g. S18) against a challenge row that survived");
        Add("identity.email_challenges", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "challenges are not consent-gated");
        Add("identity.email_challenges", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "identity.email_challenge.retention_hours sweep");
        Add("identity.email_challenges", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.accounts ---
        Add("identity.accounts", PurgeClass.AccountDeletion, PurgeVerb.Tombstone, "PII columns -> NULL/sentinel; state pinned 'deleted'; handle -> identity.retired_handles; birthdate field key crypto-shredded via field_key_refs'/data_protection_keys' existing global (purpose,subject) registration — no separate row needed here");
        Add("identity.accounts", PurgeClass.StatutoryErasure, PurgeVerb.Tombstone, "Art. 17 erasure — same tombstone mechanism as AccountDeletion");
        Add("identity.accounts", PurgeClass.MinorPurge, PurgeVerb.Delete, "confirmed-minor: enumerated purge, no tombstone identity survives (the legal-hold copy is S12's store, not ours)");
        Add("identity.accounts", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "account existence isn't consent-gated");
        Add("identity.accounts", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "n/a — accounts have no time-based expiry independent of deletion");
        Add("identity.accounts", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "n/a — not a blob store");

        // --- identity.refresh_tokens FIRST (keyed by session_id, not account_id — its own S2-scar
        // analogue): RefreshTokensPurgeStoreExecutor joins THROUGH identity.sessions
        // (db.Sessions.Where(AccountId==id).Select(SessionId)) to find its rows — that join must run
        // BEFORE identity.sessions' own Delete verb removes the session rows it depends on, exactly the
        // same ordering dependency as email_challenges-before-accounts above. ---
        Add("identity.refresh_tokens", PurgeClass.AccountDeletion, PurgeVerb.Delete, "keyed by session_id, not account_id — hard delete every row whose session belongs to this account");
        Add("identity.refresh_tokens", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("identity.refresh_tokens", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("identity.refresh_tokens", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "refresh tokens are not consent-gated");
        Add("identity.refresh_tokens", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "expired/consumed GC past 30 days");
        Add("identity.refresh_tokens", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.sessions ---
        Add("identity.sessions", PurgeClass.AccountDeletion, PurgeVerb.Delete, "hard delete");
        Add("identity.sessions", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("identity.sessions", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("identity.sessions", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "sessions are not consent-gated");
        Add("identity.sessions", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "expired/revoked GC past 30 days");
        Add("identity.sessions", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.devices (+ push tokens) ---
        Add("identity.devices", PurgeClass.AccountDeletion, PurgeVerb.Delete, "hard delete (incl. push token column)");
        Add("identity.devices", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("identity.devices", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("identity.devices", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "device registration is not consent-gated (push CATEGORY consent lives in a separate store)");
        Add("identity.devices", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "n/a — no independent retention ceiling declared for device rows");
        Add("identity.devices", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.push_category_consents (rebuildable projection over events_consent) ---
        Add("identity.push_category_consents", PurgeClass.AccountDeletion, PurgeVerb.Delete, "projection; stream keeps S1 OQ-1a posture — the evidence survives pseudonymized on events_consent, this cache row does not need to");
        Add("identity.push_category_consents", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("identity.push_category_consents", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("identity.push_category_consents", PurgeClass.ConsentRevocation, PurgeVerb.Delete, "hard delete the account's per-category rows on a broad consent-revocation purge; the rebuildable projection recomputes any still-valid grants via Replay over events_consent — no residual row survives");
        Add("identity.push_category_consents", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "n/a — no independent retention ceiling; bounded by the account's own lifetime");
        Add("identity.push_category_consents", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.consent_current (rebuildable projection over events_consent) ---
        Add("identity.consent_current", PurgeClass.AccountDeletion, PurgeVerb.Delete, "projection; evidence stays on the pseudonymized events_consent stream");
        Add("identity.consent_current", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("identity.consent_current", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("identity.consent_current", PurgeClass.ConsentRevocation, PurgeVerb.Delete, "hard delete the account's consent-kind rows on a broad consent-revocation purge; the rebuildable projection recomputes any still-valid grants via Replay over events_consent");
        Add("identity.consent_current", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "n/a — bounded by the account's own lifetime");
        Add("identity.consent_current", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.handle_history (moderation trail; OQ-2-adjacent) ---
        Add("identity.handle_history", PurgeClass.AccountDeletion, PurgeVerb.Pseudonymize, "HMAC re-key the account_id column — moderation linkage survives for key holders, the raw id is severed");
        Add("identity.handle_history", PurgeClass.StatutoryErasure, PurgeVerb.Pseudonymize, "Art. 17 erasure — same re-key mechanism");
        Add("identity.handle_history", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete (no moderation-linkage exception for a confirmed minor)");
        Add("identity.handle_history", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "handle changes are not consent-gated");
        Add("identity.handle_history", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "identity.handle_history.retention_months sweep (impersonation-defense window)");
        Add("identity.handle_history", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.export_jobs (incl. artifact bytea) ---
        Add("identity.export_jobs", PurgeClass.AccountDeletion, PurgeVerb.Delete, "hard delete incl. the artifact bytea column — the artifact contains the whole PII corpus, it dies with the account");
        Add("identity.export_jobs", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete incl. artifact");
        Add("identity.export_jobs", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete incl. artifact");
        Add("identity.export_jobs", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "export requests are not consent-gated");
        Add("identity.export_jobs", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "expired-job sweep (identity.export.link_ttl_hours — delete rows whose expires_at has passed)");
        Add("identity.export_jobs", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "no blob exists — the Postgres-bytea ruling (§12 item 5) makes this class structurally empty until S11");

        // --- identity.deletion_jobs (the receipt; S1 purge_runs precedent) ---
        Add("identity.deletion_jobs", PurgeClass.AccountDeletion, PurgeVerb.Pseudonymize, "the receipt SURVIVES — proof deletion ran; HMAC re-key the account_id column, mirrors purge_runs' own subject-pseudonymization posture");
        Add("identity.deletion_jobs", PurgeClass.StatutoryErasure, PurgeVerb.Pseudonymize, "Art. 17 erasure — same re-key mechanism, receipt survives");
        Add("identity.deletion_jobs", PurgeClass.MinorPurge, PurgeVerb.Pseudonymize, "minor-protection purge — same re-key mechanism, receipt survives");
        Add("identity.deletion_jobs", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "deletion-job rows are not consent-gated");
        Add("identity.deletion_jobs", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "the pseudonymized receipt is retained for the statutory period; no separate expiry sweep runs against it (mirrors events_consent's own RetentionExpiry posture)");
        Add("identity.deletion_jobs", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.retired_handles (OQ-2; subject-severed AT WRITE — no account linkage ever exists) ---
        Add("identity.retired_handles", PurgeClass.AccountDeletion, PurgeVerb.NotApplicable, "subject-severed at write (OQ-2, §2) — no linkage to any account_id exists for a per-subject purge call to reach");
        Add("identity.retired_handles", PurgeClass.StatutoryErasure, PurgeVerb.NotApplicable, "subject-severed at write — no linkage exists");
        Add("identity.retired_handles", PurgeClass.MinorPurge, PurgeVerb.NotApplicable, "subject-severed at write — no linkage exists");
        Add("identity.retired_handles", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "not consent-gated");
        Add("identity.retired_handles", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "the OQ-2 quarantine-release sweep (identity.handle.retirement_days) is a GLOBAL, non-subject-scoped age sweep over the whole table — it does not fit IPurgePipeline.Run's per-subject SubjectRef shape (there is no subject column to filter on by design); landing a dedicated global sweep job is out of THIS deletion+purge pass's scope. Registered honestly as NotApplicable-for-this-mechanism rather than a verb that would never actually fire correctly through a per-subject call.");
        Add("identity.retired_handles", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- identity.reserved_handles (zero personal data — a seeded desk manifest) ---
        foreach (var purgeClass in Enum.GetValues<PurgeClass>())
        {
            Add("identity.reserved_handles", purgeClass, PurgeVerb.NotApplicable, "zero personal data — a seeded desk manifest (brand/staff/impersonation terms), registered with reason, never silently exempt");
        }

        // --- identity.ban_evasion_refs (OQ-3 RATIFIED (a) — retained evidence, never purged by design) ---
        foreach (var purgeClass in Enum.GetValues<PurgeClass>())
        {
            Add("identity.ban_evasion_refs", purgeClass, PurgeVerb.NotApplicable, "OQ-3 RATIFIED (a): a banned account's deletion WRITES this store (salted-HMAC email ref + push-token hash, lawful_basis=legitimate_interest) so re-registration can be refused — it is deliberately retained evidence, never a purge target for any class reachable through this pipeline");
        }

        return entries;
    }
}
