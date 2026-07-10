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
    public Task<IReadOnlyList<PurgeReport>> Run(PurgeClass purgeClass, SubjectRef subject, ActorRef actor, RequestContext ctx, CancellationToken ct = default);
}
