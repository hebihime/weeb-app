namespace Svac.DomainCore.Deterministic;

/// <summary>
/// The (variant, stream/store, event_type, region) -&gt; resolved lawful basis code table (PII-F2,
/// SECURITY_REVIEW_S1.md; SLICE_S1_CONTRACT.md §1b/§2: "lawful_basis resolved from a code table keyed
/// (stream/store, event_type, region)"). Pure and IO-free by construction (Svac.DomainCore.Deterministic
/// — no wall-clock, no config-registry read inside; the caller resolves the variant KEY once via 9A and
/// passes it in). The variant key selects WHICH table below applies; it is never itself written as the
/// lawful_basis value — persisting the variant key verbatim was exactly PII-F2's break.
/// </summary>
public static class LawfulBasisResolver
{
    /// <summary>
    /// Resolves the Art.6-shaped basis string a row's lawful_basis column should carry.
    /// </summary>
    /// <param name="variantKey">The 9A `privacy.lawful_basis_map_variant` value (selects the table).</param>
    /// <param name="streamOrStore">The stream/store this write lands in (e.g. "Ledger", "Consent" — StreamType.ToString(), or a persistence store key for a non-event-store write like ledger_entries).</param>
    /// <param name="eventType">The event/verb being recorded — reserved for a future finer-grained variant; the v0 table resolves purely on stream/store.</param>
    /// <param name="region">The row's resolved region string (e.g. "DE", or "ZZ" for the pure-system sentinel).</param>
    public static string Resolve(string variantKey, string streamOrStore, string eventType, string region)
    {
        // §1b: "pure-system rows use Region.Unknown ('ZZ') / lawful_basis='n/a'" — a system-only write
        // (migration/seed/scheduler) with no subject has no personal-data basis to resolve at all.
        if (region == "ZZ")
        {
            return "n/a";
        }

        return variantKey switch
        {
            "conservative_global_v0" => ConservativeGlobalV0(streamOrStore),
            _ => throw new ArgumentOutOfRangeException(
                nameof(variantKey), variantKey,
                "no lawful-basis resolver code table is registered for this variant — counsel's L-1 map " +
                "lands as a NEW variant key + audited config flip (SLICE_S1_CONTRACT.md §1b), never a silent code change."),
        };
    }

    /// <summary>
    /// v0's deliberately conservative table: one Art.6 basis per stream/store, chosen to be the
    /// narrowest defensible basis for that stream's typical content until counsel's per-event-type L-1
    /// map lands as its own variant.
    /// </summary>
    private static string ConservativeGlobalV0(string streamOrStore) => streamOrStore switch
    {
        "Ledger" or "ledger_entries" or "ledger_balances" => "contract", // Art.6(b): performing the quest/reward contract with the user.
        "Consent" => "consent", // Art.6(a): the consent stream IS the consent record itself.
        "Reputation" => "legitimate_interest", // Art.6(f): trust/safety scoring.
        "Behavioral" => "legitimate_interest", // Art.6(f): product analytics/telemetry.
        "Audit" => "legal_obligation", // Art.6(c): the staff-action accountability trail.
        "HeatmapProvenance" => "legitimate_interest", // Art.6(f): heatmap provenance/anti-abuse.
        _ => "legitimate_interest", // narrowest general-purpose default for anything not yet enumerated.
    };
}
