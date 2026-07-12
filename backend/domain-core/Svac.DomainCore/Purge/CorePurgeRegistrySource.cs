using Svac.DomainCore.Contracts.Purge;

namespace Svac.DomainCore.Purge;

/// <summary>
/// Domain-core's own additive slice of the 13A registry (SLICE_S1_CONTRACT.md §6): every store S1
/// creates, registered against EVERY closed purge class with a verb — NotApplicable is a registered
/// exemption with a reason, never a silent gap. This table is the "S1's own stores are the first
/// registrants" clause: the CI gate that fails an unregistered store lands with real, non-vacuous rows
/// on day one. Extracted verbatim from the pre-Phase-2a <see cref="PurgeRegistry"/> (which used to BE
/// this list directly) so <see cref="PurgeRegistry"/> can become the boot-time UNION of every registered
/// <see cref="IPurgeRegistrySource"/>, mirroring <c>Svac.DomainCore.Export.CoreExportRegistrySource</c>
/// and <c>Svac.DomainCore.Policy.CorePolicyTableSource</c> exactly — byte-identical entries, zero behavior
/// change.
/// </summary>
public sealed class CorePurgeRegistrySource : IPurgeRegistrySource
{
    public IReadOnlyList<PurgeRegistrationEntry> Entries { get; } = BuildEntries();

    private static List<PurgeRegistrationEntry> BuildEntries()
    {
        var entries = new List<PurgeRegistrationEntry>();

        void Add(string storeKey, PurgeClass purgeClass, PurgeVerb verb, string reason) =>
            entries.Add(new PurgeRegistrationEntry(storeKey, purgeClass, verb, reason));

        // events_ledger + ledger_entries + ledger_balances — one row group in the contract prose,
        // three physical stores here since each is independently purgeable.
        foreach (var store in new[] { "events_ledger", "ledger_entries", "ledger_balances" })
        {
            Add(store, PurgeClass.AccountDeletion, PurgeVerb.Tombstone, "entries survive as tombstones, user refs severed; balances rebuilt by Replay");
            Add(store, PurgeClass.StatutoryErasure, PurgeVerb.Tombstone, "Art. 17 erasure — tombstone, never a real delete (immutability posture)");
            Add(store, PurgeClass.MinorPurge, PurgeVerb.Tombstone, "minor-protection purge — tombstone, never a real delete");
            Add(store, PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "earn events are not consent-gated");
            Add(store, PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "ledger is retained indefinitely per questsystem §Day-One; no retention ceiling applies");
            Add(store, PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");
        }

        // events_reputation — empty event-type enumeration at S1 (real writer S16); verbs declared now
        // so the gate is non-vacuous the day the writer lands.
        Add("events_reputation", PurgeClass.AccountDeletion, PurgeVerb.Tombstone, "tombstone on account deletion");
        Add("events_reputation", PurgeClass.StatutoryErasure, PurgeVerb.Tombstone, "Art. 17 erasure — tombstone");
        Add("events_reputation", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("events_reputation", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "window-bounded recompute verb declared; the recompute writer arrives at S16 — no reputation events exist to recompute against at S1");
        Add("events_reputation", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "model_version-scoped retention lands with the S16 writer");
        Add("events_reputation", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // events_consent — OQ-1 resolved posture (a), interim-reversible (SLICE_S1_CONTRACT.md §15).
        Add("events_consent", PurgeClass.AccountDeletion, PurgeVerb.Pseudonymize, "OQ-1 interim posture (a): pseudonymize subject (irreversible re-key); revocation stays an EVENT on this stream, never a purge of it");
        Add("events_consent", PurgeClass.StatutoryErasure, PurgeVerb.Pseudonymize, "OQ-1 interim posture (a)");
        Add("events_consent", PurgeClass.MinorPurge, PurgeVerb.Pseudonymize, "OQ-1 interim posture (a)");
        Add("events_consent", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "revocation is an EVENT on this stream, never a purge of it");
        Add("events_consent", PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "bounded instead by the founder-scope retention ceiling on the pseudonymized record, not a separate expiry sweep");
        Add("events_consent", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // events_behavioral — the hottest-volume stream; every class is a real Delete.
        Add("events_behavioral", PurgeClass.AccountDeletion, PurgeVerb.Delete, "hard delete rows");
        Add("events_behavioral", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("events_behavioral", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("events_behavioral", PurgeClass.ConsentRevocation, PurgeVerb.Delete, "hard delete");
        Add("events_behavioral", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "retention window value set when the Metrics & Ops desk consumes it (S5) — mechanism is Delete regardless of the window's length");
        Add("events_behavioral", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // events_audit — immutability posture: "reversal entries, never data surgery"; OQ-1 (a).
        Add("events_audit", PurgeClass.AccountDeletion, PurgeVerb.Tombstone, "OQ-1 interim posture (a): tombstone actor PII in payload, record survives — the staff-action accountability trail is preserved");
        Add("events_audit", PurgeClass.StatutoryErasure, PurgeVerb.Tombstone, "OQ-1 interim posture (a)");
        Add("events_audit", PurgeClass.MinorPurge, PurgeVerb.Tombstone, "OQ-1 interim posture (a)");
        Add("events_audit", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "audit rows are not consent-gated");
        Add("events_audit", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "statutory retention period governs; hard delete once the age threshold is reached");
        Add("events_audit", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // events_heatmap_provenance — founder ruling: full-history by default; deletion machinery only,
        // no read API in the contract assembly (enforced structurally by 1A: it is not in Contracts).
        Add("events_heatmap_provenance", PurgeClass.AccountDeletion, PurgeVerb.NotApplicable, "full-history retention per profilemodel §1c founder ruling — account deletion does not trigger heatmap-provenance deletion");
        Add("events_heatmap_provenance", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure overrides the full-history default");
        Add("events_heatmap_provenance", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge overrides the full-history default");
        Add("events_heatmap_provenance", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "not consent-gated");
        Add("events_heatmap_provenance", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "12 months / R2 (core.purge.sweep — cell_history_months config)");
        Add("events_heatmap_provenance", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // quota_counters — Postgres, not Redis (§2/§12.15).
        Add("quota_counters", PurgeClass.AccountDeletion, PurgeVerb.Delete, "hard delete the subject's counters");
        Add("quota_counters", PurgeClass.StatutoryErasure, PurgeVerb.Delete, "Art. 17 erasure — hard delete");
        Add("quota_counters", PurgeClass.MinorPurge, PurgeVerb.Delete, "minor-protection purge — hard delete");
        Add("quota_counters", PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "quota counters are not consent-gated");
        Add("quota_counters", PurgeClass.RetentionExpiry, PurgeVerb.Delete, "delete expired windows (GC) — core.purge.sweep_interval_minutes");
        Add("quota_counters", PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");

        // config_entries — non-personal by construction; NotApplicable across the board, registered
        // with a reason rather than silently exempt.
        foreach (var purgeClass in Enum.GetValues<PurgeClass>())
        {
            Add("config_entries", purgeClass, PurgeVerb.NotApplicable, "zero personal data by construction (§2) — registered with reason, never silently exempt");
        }

        // data_protection_keys + field_key_refs — the crypto-shred pair (§1b IFieldEncryptor/IFieldKeyVault).
        foreach (var store in new[] { "data_protection_keys", "field_key_refs" })
        {
            Add(store, PurgeClass.AccountDeletion, PurgeVerb.CryptoShred, "Key Vault purge-protection ON means shred = key destruction by re-wrap denial");
            Add(store, PurgeClass.StatutoryErasure, PurgeVerb.CryptoShred, "Art. 17 erasure — crypto-shred");
            Add(store, PurgeClass.MinorPurge, PurgeVerb.CryptoShred, "minor-protection purge — crypto-shred");
            Add(store, PurgeClass.ConsentRevocation, PurgeVerb.NotApplicable, "key material is not consent-gated");
            Add(store, PurgeClass.RetentionExpiry, PurgeVerb.NotApplicable, "keys are retired by explicit Shred, never a time-based sweep");
            Add(store, PurgeClass.OrphanedBlob, PurgeVerb.NotApplicable, "not a blob store");
        }

        // purge_runs + projection_checkpoints — the registry eats its own dog food.
        foreach (var purgeClass in Enum.GetValues<PurgeClass>())
        {
            if (purgeClass == PurgeClass.RetentionExpiry)
            {
                Add("purge_runs", purgeClass, PurgeVerb.Delete, "purge_runs itself carries retention_expiry — the registry eats its own dog food");
            }
            else
            {
                Add("purge_runs", purgeClass, PurgeVerb.NotApplicable, "non-PII operational metadata (purge-run receipts)");
            }
            Add("projection_checkpoints", purgeClass, PurgeVerb.NotApplicable, "non-PII operational metadata (per-consumer watermarks)");
        }

        return entries;
    }
}
