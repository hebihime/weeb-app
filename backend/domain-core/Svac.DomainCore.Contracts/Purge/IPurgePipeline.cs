using Svac.DomainCore.Contracts.Ids;

namespace Svac.DomainCore.Contracts.Purge;

/// <summary>
/// Closed purge-class taxonomy (SLICE_S1_CONTRACT.md §6). Fixed at S1 so every later store declares
/// against a fixed set — extending this enum is a versioned contract change, never an ad hoc addition.
/// </summary>
public enum PurgeClass
{
    AccountDeletion,
    StatutoryErasure,
    MinorPurge,
    ConsentRevocation,
    RetentionExpiry,
    OrphanedBlob,
}

/// <summary>
/// The verbs a store can declare per class (SLICE_S1_CONTRACT.md §6). CryptoShred is a VERB, never its
/// own class. Pseudonymize is added beyond the contract's literal four-verb prose to give the OQ-1
/// ratification (SLICE_S1_CONTRACT.md §15: "post-account-deletion, consent and audit streams RETAIN with
/// the subject pseudonymized") a real, distinct verb — pseudonymization keeps the record's shape and
/// utility (proves consent existed) rather than nulling the payload, so mapping it onto Tombstone would
/// misstate what actually happens on that path.
/// </summary>
public enum PurgeVerb
{
    Delete,
    Tombstone,
    CryptoShred,
    Pseudonymize,
    NotApplicable,
}

/// <summary>Reference to the subject a purge run targets.</summary>
public readonly record struct SubjectRef(string ResourceType, string ResourceId);

/// <summary>
/// Declares, at compile time, what verb a store performs for a given purge class (SLICE_S1_CONTRACT.md
/// §6). The arch test enumerates every EF entity type + every declared blob/cache store and fails the
/// build on any store absent from this registry.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PurgeRegistrationAttribute(string storeKey, PurgeClass purgeClass, PurgeVerb verb, string? reason = null) : Attribute
{
    public string StoreKey { get; } = storeKey;
    public PurgeClass PurgeClass { get; } = purgeClass;
    public PurgeVerb Verb { get; } = verb;

    /// <summary>Required when Verb is NotApplicable — a registered exemption, never a silent gap.</summary>
    public string? Reason { get; } = reason;
}

/// <summary>Custody-hold override: holds override erasure during open reports (SLICE_S1_CONTRACT.md §6, ER-14).</summary>
public readonly record struct CustodyHold(string HoldId, string DocumentedBasis);

/// <summary>Outcome of one purge-pipeline run over one store (SLICE_S1_CONTRACT.md §1b, §6).</summary>
public sealed record PurgeReport(string RunId, string StoreKey, PurgeClass PurgeClass, int RowsAffected, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

/// <summary>
/// The ONE executor of purge verbs (SLICE_S1_CONTRACT.md §1b, §6): iterates the compile-time registry,
/// runs the registered verb per store for the requested class, emits an Audit-stream event + a
/// purge_run row per run.
/// </summary>
public interface IPurgePipeline
{
    /// <param name="heldStoreKeys">
    /// Additive (SLICE_S3_CONTRACT.md §2 Phase P step 1, ER-14): store keys a caller's OWN custody-hold
    /// consult (e.g. identity's 13A custody-hold registry) has determined must NOT be touched by this
    /// run. Null/empty (every S1/S2 call site, byte-identical) executes every registered store exactly as
    /// before. A held store still gets a purge_run receipt row — verb recorded as "Held", zero rows
    /// affected, the documented basis in the evidence column — "held stores are skipped with a
    /// documented-basis purge_run row, rest proceeds" (§2). The orchestrator's algorithm itself never
    /// changes; this only lets a caller name stores to skip for THIS run.
    /// </param>
    public Task<IReadOnlyList<PurgeReport>> Run(PurgeClass purgeClass, SubjectRef subject, ActorRef actor, RequestContext ctx, IReadOnlySet<string>? heldStoreKeys = null, CancellationToken ct = default);
}

/// <summary>
/// A module's own pluggable purge EXECUTION for ONE store it owns (SLICE_S3_CONTRACT.md §6c: "One
/// orchestrator over S1's ONE purge pipeline — never a second pipeline; later modules join by 13A
/// registration and the orchestrator never changes") — the purge-side mirror of <see
/// cref="Svac.DomainCore.Contracts.Export.IExportContributor"/>. <see cref="PurgePipeline"/> (domain-core)
/// natively executes its own six event streams + ledger/quota/crypto-shred pairs; for every OTHER
/// registered storeKey it falls back to whichever <see cref="IPurgeStoreExecutor"/> is DI-registered for
/// that key (identity registers one per identity table) — the orchestrator's algorithm (iterate the
/// registry, run the verb, write the receipt + audit event) never changes; only WHO executes a given
/// store's verb does, by registration.
/// </summary>
public interface IPurgeStoreExecutor
{
    public string StoreKey { get; }

    /// <summary>
    /// Executes <paramref name="verb"/> for <paramref name="subject"/> against this store. Never called
    /// with <see cref="PurgeVerb.NotApplicable"/> (the pipeline short-circuits that case itself). Returns
    /// the number of rows affected — the same receipt shape every native S1 store reports.
    /// </summary>
    public Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default);
}

/// <summary>
/// The ONE keyed re-key construction every purge-registry Pseudonymize verb uses (MinorProt-F4,
/// SECURITY_REVIEW_S1.md: a KEYED re-key, never an unsalted hash — a candidate id recomputes an unsalted
/// hash with zero secrets). Shared here so <see cref="Svac.DomainCore.Purge.PurgePipeline"/>'s own
/// events_consent re-key and every module's <see cref="IPurgeStoreExecutor"/> Pseudonymize verb derive the
/// SAME pseudonym for the SAME (purgeClass, original, key) — required so cross-store correlation survives
/// for a key holder (e.g. identity.handle_history's re-keyed account ref matching events_consent's own)
/// and so re-running the SAME purge idempotently re-derives the SAME pseudonym.
/// </summary>
public static class PurgePseudonymizer
{
    public static string Pseudonymize(string original, PurgeClass purgeClass, byte[] hmacKey)
    {
        var bytes = System.Security.Cryptography.HMACSHA256.HashData(hmacKey, System.Text.Encoding.UTF8.GetBytes($"{purgeClass}:{original}"));
        var body = Svac.DomainCore.Deterministic.Ulid.Encode(0, bytes.AsSpan(0, 10));
        return Svac.DomainCore.Deterministic.Ulid.WithPrefix(IdPrefixes.Pseudonym, body);
    }
}
