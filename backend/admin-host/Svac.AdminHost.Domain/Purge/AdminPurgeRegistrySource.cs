using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Purge;

namespace Svac.AdminHost.Domain.Purge;

/// <summary>
/// The admin host's own additive slice of the 13A registry (SLICE_S5_CONTRACT.md §6) — every store
/// AdminDbContext owns, registered against every closed purge class with a verb. Mirrors
/// Svac.Identity.Purge.IdentityPurgeRegistrySource's shape exactly: a module ADDS its own store keys, it
/// never edits another source's rows; <see cref="PurgeRegistry"/>'s constructor boot-refuses on a
/// storeKey claimed by more than one source.
/// </summary>
public sealed class AdminPurgeRegistrySource : IPurgeRegistrySource
{
    public IReadOnlyList<PurgeRegistrationEntry> Entries { get; } = BuildEntries();

    private static List<PurgeRegistrationEntry> BuildEntries()
    {
        var entries = new List<PurgeRegistrationEntry>();

        void Add(string storeKey, PurgeClass purgeClass, PurgeVerb verb, string reason) =>
            entries.Add(new PurgeRegistrationEntry(storeKey, purgeClass, verb, reason));

        // --- admin.staff_accounts ---
        Add("admin.staff_accounts", PurgeClass.AccountDeletion, PurgeVerb.NotApplicable, "staff are not consumer accounts; the consumer deletion pipeline never reaches them");
        Add("admin.staff_accounts", PurgeClass.StatutoryErasure, PurgeVerb.Pseudonymize, "email/display_name/external_subject re-keyed (irreversible); stf_ id + status survive so every audit chain still resolves — the S1 OQ-1a posture applied to staff");
        Add("admin.staff_accounts", PurgeClass.MinorPurge, PurgeVerb.NotApplicable, "18+ employment/engagement by construction — no minor staff exists");
        Add("admin.staff_accounts", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "no consent-gated staff field");
        Add("admin.staff_accounts", PurgeClass.RetentionExpiry, PurgeVerb.Pseudonymize, "admin.staff_pii_retention_years post-deactivation — same re-key mechanism as StatutoryErasure");
        Add("admin.staff_accounts", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // --- admin.staff_role_grants ---
        Add("admin.staff_role_grants", PurgeClass.AccountDeletion, PurgeVerb.NotApplicable, "staff are not consumer accounts; the consumer deletion pipeline never reaches them");
        Add("admin.staff_role_grants", PurgeClass.StatutoryErasure, PurgeVerb.Tombstone, "reason texts + grantor/revoker PII tombstoned where the erased subject appears; grant/revoke structure survives (accountability)");
        Add("admin.staff_role_grants", PurgeClass.MinorPurge, PurgeVerb.NotApplicable, "18+ employment/engagement by construction — no minor staff exists");
        Add("admin.staff_role_grants", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "not consent-gated");
        Add("admin.staff_role_grants", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "rides admin.staff_accounts' own retention sweep — no separate expiry sweep runs against this store");
        Add("admin.staff_role_grants", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        return entries;
    }
}
