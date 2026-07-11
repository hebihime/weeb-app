using Svac.DomainCore.Contracts.Export;

namespace Svac.DomainCore.Export;

/// <summary>
/// Domain-core's own additive slice of the export registry (SLICE_S3_CONTRACT.md §6b) — the DISPOSITIONS
/// for the S1 stores S3 is NOT the real consumer of: derived/operational/key-material stores with zero
/// per-subject export content, each registered with a stated reason rather than silently omitted. The
/// five S1 stores S3 IS the first real consumer of (events_ledger, ledger_entries, events_consent,
/// events_behavioral, events_audit) are registered as <see cref="ExportRegistryState.Contributes"/> by
/// <c>Svac.Identity.Export.IdentityExportRegistrySource</c> instead (§6b: "S3 registers contributors for
/// every existing subject-bearing store") — domain-core does not pre-empt that registration with its own
/// conflicting entry for the same nine keys below being the ones domain-core keeps for itself.
/// </summary>
public sealed class CoreExportRegistrySource : IExportRegistrySource
{
    public IReadOnlyList<ExportRegistrationEntry> Entries { get; } = BuildEntries();

    private static List<ExportRegistrationEntry> BuildEntries()
    {
        var entries = new List<ExportRegistrationEntry>();

        void Add(string storeKey, ExportRegistryState state, string reason) =>
            entries.Add(new ExportRegistrationEntry(storeKey, state, reason));

        Add("ledger_balances", ExportRegistryState.NotExportable,
            "rebuildable cache of ledger_entries via summation (\"summation is the truth\", §2 OtherEntities.cs) — ILedger.BalanceOf is the read door and its result is embedded directly in the ledger_entries export contribution; this physical cache is not separately exported");
        Add("events_reputation", ExportRegistryState.NotExportable,
            "zero writers exist yet (real writer lands S16); zero rows possible today — registered as a disposition, not silently omitted, so S16 must flip this to Contributes when it lands a subject-facing writer");
        Add("events_heatmap_provenance", ExportRegistryState.NotExportable,
            "full-history heatmap provenance is deletion-machinery-only per its founder ruling (profilemodel §1c) — \"no read API in the contract assembly (enforced structurally by 1A: it is not in Contracts)\"; S3 does not add one");
        Add("quota_counters", ExportRegistryState.NotExportable,
            "derived operational counters, not the subject's own personal-data content (explicit example in SLICE_S3_CONTRACT.md §6b)");
        Add("config_entries", ExportRegistryState.NotExportable,
            "zero personal data by construction — same registered exemption as its 13A purge registration");
        Add("data_protection_keys", ExportRegistryState.NotExportable,
            "key material — never exported (explicit example in SLICE_S3_CONTRACT.md §6b)");
        Add("field_key_refs", ExportRegistryState.NotExportable,
            "key material — never exported (explicit example in SLICE_S3_CONTRACT.md §6b)");
        Add("purge_runs", ExportRegistryState.NotExportable,
            "operational purge-run receipts (provenance that a purge ran), not the subject's own personal-data content");
        Add("projection_checkpoints", ExportRegistryState.NotExportable,
            "non-PII operational per-consumer watermark metadata");

        return entries;
    }
}
