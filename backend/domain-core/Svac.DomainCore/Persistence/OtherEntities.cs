namespace Svac.DomainCore.Persistence;

/// <summary>core.projection_checkpoints (SLICE_S1_CONTRACT.md §2). Per-consumer watermark, one row per (consumer, stream).</summary>
public sealed class ProjectionCheckpointEntity
{
    public required string ConsumerId { get; set; }
    public required string StreamType { get; set; }
    public long WatermarkSeq { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>core.ledger_entries (SLICE_S1_CONTRACT.md §2). questsystem §Day-One verbatim + region/lawful_basis.</summary>
public sealed class LedgerEntryEntity
{
    public required string Id { get; set; } // led_ ULID; 1:1 with an events_ledger row
    public required string UserId { get; set; }
    public string? CrewId { get; set; }
    public required string EventType { get; set; }
    public int Points { get; set; } // CHECK points >= 0
    public int Xp { get; set; } // CHECK xp = points
    public long Svac { get; set; } // negative allowed: sink_purchase only
    public string? QuestId { get; set; }
    public string? EvidenceRef { get; set; }
    public required string Region { get; set; }
    public required string LawfulBasis { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ReversalOf { get; set; } // FK -> this table's id
}

/// <summary>core.ledger_balances (SLICE_S1_CONTRACT.md §2). Projection, rebuildable; summation is the truth.</summary>
public sealed class LedgerBalanceEntity
{
    public required string UserId { get; set; }
    public long Points { get; set; }
    public long Xp { get; set; }
    public long Svac { get; set; }
    public long Watermark { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>core.config_entries (SLICE_S1_CONTRACT.md §2, §4). Every Set emits an events_audit row in-tx.</summary>
public sealed class ConfigEntryEntity
{
    public required string Key { get; set; }
    public required string Type { get; set; }
    public required string ValueJson { get; set; }
    public required string Scope { get; set; } // CHECK scope IN ('founder','ops','set')
    public string? Gate { get; set; }
    public string? BoundsJson { get; set; }
    public bool RequiresReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}

/// <summary>core.quota_counters (SLICE_S1_CONTRACT.md §2, §5). Consume = single atomic UPSERT ... WHERE consumed &lt; cap.</summary>
public sealed class QuotaCounterEntity
{
    public required string ActorRef { get; set; }
    public required string QuotaKey { get; set; }
    public required string WindowKey { get; set; }
    public int Consumed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>core.purge_runs (SLICE_S1_CONTRACT.md §2, §6). One row per purge-pipeline run per store.</summary>
public sealed class PurgeRunEntity
{
    public required string Id { get; set; }
    public required string PurgeClass { get; set; }
    public required string SubjectRef { get; set; }
    public required string StoreKey { get; set; }
    public int RowsAffected { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? EvidenceJson { get; set; }
}

/// <summary>
/// core.data_protection_keys (SLICE_S1_CONTRACT.md §2). Minimal shape mirroring the standard .NET Data
/// Protection EF key-ring store (FriendlyName/Xml), owned directly rather than pulled in via the
/// Microsoft.AspNetCore.DataProtection.EntityFrameworkCore package so Svac.DomainCore.csproj's
/// dependency surface stays exactly what §1a lists. 13A-registered with CryptoShred.
/// </summary>
public sealed class DataProtectionKeyEntity
{
    public int Id { get; set; }
    public string? FriendlyName { get; set; }
    public string? Xml { get; set; }
}

/// <summary>core.field_key_refs (SLICE_S1_CONTRACT.md §2). Seeded with field-enc-special-category-v1; no key material ever in Postgres.</summary>
public sealed class FieldKeyRefEntity
{
    public required string FieldKeyId { get; set; }
    public required string VaultKeyName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}
