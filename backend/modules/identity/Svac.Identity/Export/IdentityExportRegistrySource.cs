using Svac.DomainCore.Contracts.Export;

namespace Svac.Identity.Export;

/// <summary>
/// Identity's own additive slice of the export registry (SLICE_S3_CONTRACT.md §6b) — the centerpiece
/// registration. Every identity table that holds the subject's data is <see
/// cref="ExportRegistryState.Contributes"/> with a real DI-registered <see
/// cref="Svac.DomainCore.Contracts.Export.IExportContributor"/> (registered in <see
/// cref="Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions"/>); every identity table
/// that is deliberately NOT exportable carries a REGISTERED disposition with a stated reason, never a
/// silent gap. PLUS the five S1 stores S3 is the first real consumer of (§6b: "S3 registers contributors
/// for every existing subject-bearing store: ... ledger_entries (+ILedger.BalanceOf), events_consent,
/// events_behavioral, events_ledger, and the subject's OWN events_audit rows") — these five entries are
/// exactly the ones that, together with <c>Svac.DomainCore.Export.CoreExportRegistrySource</c>'s nine
/// dispositions, cover EVERY store <c>purge-registry.json</c> declares (the export⋈purge cross-gate's
/// non-vacuous proof).
/// </summary>
public sealed class IdentityExportRegistrySource : IExportRegistrySource
{
    public IReadOnlyList<ExportRegistrationEntry> Entries { get; } = BuildEntries();

    private static List<ExportRegistrationEntry> BuildEntries()
    {
        var entries = new List<ExportRegistrationEntry>();

        void Contributes(string storeKey) =>
            entries.Add(new ExportRegistrationEntry(storeKey, ExportRegistryState.Contributes, "real IExportContributor registered — writes (path, schema-versioned JSON) to the export sink"));

        void NotExportable(string storeKey, string reason) =>
            entries.Add(new ExportRegistrationEntry(storeKey, ExportRegistryState.NotExportable, reason));

        // --- Identity's own tables that hold the subject's data (SLICE_S3_CONTRACT.md §6b enumeration) ---
        Contributes("identity.accounts");
        Contributes("identity.sessions");
        Contributes("identity.devices");
        Contributes("identity.push_category_consents");
        Contributes("identity.consent_current");
        Contributes("identity.handle_history");
        Contributes("identity.export_jobs");
        Contributes("identity.deletion_jobs");

        // --- Identity's own tables that are deliberately NOT exportable, with a stated reason ---
        NotExportable("identity.email_challenges",
            "transient credential artifacts (explicit example in SLICE_S3_CONTRACT.md §6b) — no birthdate, no durable subject content, single-use codes only");
        NotExportable("identity.refresh_tokens",
            "opaque, one-way-hashed rotation-tracking tokens with zero subject-readable content; the session's own identity/timestamps already export via identity.sessions");
        NotExportable("identity.reserved_handles",
            "non-personal — a seeded desk manifest (brand/staff/impersonation terms), zero subject linkage");
        NotExportable("identity.retired_handles",
            "subject-severed at write by design (OQ-2, SLICE_S3_CONTRACT.md §2) — pseudonymous by construction, no linkage exists to export against");
        NotExportable("identity.ban_evasion_refs",
            "keyed by HMAC(email), not account id — no subject-id linkage exists to read against; no writer exists before the Pass-2b deletion pipeline (OQ-3)");

        // --- The five S1 stores S3 is the FIRST real consumer of (§6b) ---
        Contributes("ledger_entries");
        Contributes("events_ledger");
        Contributes("events_consent");
        Contributes("events_behavioral");
        Contributes("events_audit");

        return entries;
    }
}
