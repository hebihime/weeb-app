using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.DomainCore.Persistence;

/// <summary>
/// The schema `core` DbContext (SLICE_S1_CONTRACT.md §2), owned SOLELY by this type — no other DbContext
/// ever touches schema `core`. Table-per-stream: six physical tables of one generated shape (EventRow,
/// mapped as EF shared-type entities) + the ledger/config/quota/purge/key-material tables.
/// </summary>
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    /// <summary>Maps each 3A StreamType to its physical table name (SLICE_S1_CONTRACT.md §2).</summary>
    public static readonly IReadOnlyDictionary<StreamType, string> StreamTables = new Dictionary<StreamType, string>
    {
        [StreamType.Ledger] = "events_ledger",
        [StreamType.Reputation] = "events_reputation",
        [StreamType.Consent] = "events_consent",
        [StreamType.Behavioral] = "events_behavioral",
        [StreamType.Audit] = "events_audit",
        [StreamType.HeatmapProvenance] = "events_heatmap_provenance",
    };

    public DbSet<EventRow> EventsFor(StreamType stream) => Set<EventRow>(StreamTables[stream]);

    public DbSet<ProjectionCheckpointEntity> ProjectionCheckpoints => Set<ProjectionCheckpointEntity>();
    public DbSet<LedgerEntryEntity> LedgerEntries => Set<LedgerEntryEntity>();
    public DbSet<LedgerBalanceEntity> LedgerBalances => Set<LedgerBalanceEntity>();
    public DbSet<ConfigEntryEntity> ConfigEntries => Set<ConfigEntryEntity>();
    public DbSet<QuotaCounterEntity> QuotaCounters => Set<QuotaCounterEntity>();
    public DbSet<PurgeRunEntity> PurgeRuns => Set<PurgeRunEntity>();
    public DbSet<DataProtectionKeyEntity> DataProtectionKeys => Set<DataProtectionKeyEntity>();
    public DbSet<FieldKeyRefEntity> FieldKeyRefs => Set<FieldKeyRefEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("core");

        foreach (var (_, tableName) in StreamTables)
        {
            modelBuilder.SharedTypeEntity<EventRow>(tableName, b =>
            {
                b.ToTable(tableName, "core", t =>
                {
                    t.HasCheckConstraint($"ck_{tableName}_payload_null_iff_tombstoned", "NOT tombstone OR payload IS NULL");
                });
                b.HasKey(e => e.EventId);
                b.Property(e => e.EventId).HasColumnName("event_id");
                b.Property(e => e.StreamId).HasColumnName("stream_id");
                b.Property(e => e.Seq).HasColumnName("seq");
                // Concurrency-F1: table-wide monotonic identity backing Replay's cross-stream watermark
                // (distinct from Seq, which is per-stream_id). DB-generated so ordering is assigned
                // atomically at commit time, never computed client-side.
                b.Property(e => e.GlobalSeq).HasColumnName("global_seq").ValueGeneratedOnAdd();
                b.Property(e => e.EventType).HasColumnName("event_type");
                b.Property(e => e.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
                b.Property(e => e.ReversalOf).HasColumnName("reversal_of");
                b.Property(e => e.Tombstone).HasColumnName("tombstone").HasDefaultValue(false);
                b.Property(e => e.ActorRef).HasColumnName("actor_ref");
                b.Property(e => e.Region).HasColumnName("region");
                b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");
                b.Property(e => e.OccurredAt).HasColumnName("occurred_at");
                b.Property(e => e.RecordedAt).HasColumnName("recorded_at").HasDefaultValueSql("now()");
                b.HasIndex(e => new { e.StreamId, e.Seq }).IsUnique();
                // No EF-managed self-referencing FK navigation on reversal_of: EF's shared-type-entity
                // mapping (one CLR type, six tables) cannot resolve HasOne<EventRow>() unambiguously
                // across six SharedTypeEntity registrations of the same CLR type. A plain index plus the
                // migration's own `REFERENCES core.events_<stream>(event_id)` raw-SQL column definition
                // (added post-generation) gives the same DB-level integrity without the ambiguity.
                b.HasIndex(e => e.ReversalOf);
                b.HasIndex(e => e.GlobalSeq); // Concurrency-F1: Replay's cross-stream watermark filter/order key.
            });
        }

        modelBuilder.Entity<ProjectionCheckpointEntity>(b =>
        {
            b.ToTable("projection_checkpoints", "core");
            b.HasKey(e => new { e.ConsumerId, e.StreamType });
            b.Property(e => e.ConsumerId).HasColumnName("consumer_id");
            b.Property(e => e.StreamType).HasColumnName("stream_type");
            b.Property(e => e.WatermarkSeq).HasColumnName("watermark_seq");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<LedgerEntryEntity>(b =>
        {
            b.ToTable("ledger_entries", "core", t =>
            {
                t.HasCheckConstraint("ck_ledger_entries_points_nonneg", "points >= 0");
                t.HasCheckConstraint("ck_ledger_entries_xp_eq_points", "xp = points");
            });
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.UserId).HasColumnName("user_id");
            b.Property(e => e.CrewId).HasColumnName("crew_id");
            b.Property(e => e.EventType).HasColumnName("event_type");
            b.Property(e => e.Points).HasColumnName("points");
            b.Property(e => e.Xp).HasColumnName("xp");
            b.Property(e => e.Svac).HasColumnName("svac");
            b.Property(e => e.QuestId).HasColumnName("quest_id");
            b.Property(e => e.EvidenceRef).HasColumnName("evidence_ref");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.ReversalOf).HasColumnName("reversal_of");
            b.HasOne<LedgerEntryEntity>().WithMany().HasForeignKey(e => e.ReversalOf).HasPrincipalKey(e => e.Id).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LedgerBalanceEntity>(b =>
        {
            b.ToTable("ledger_balances", "core");
            b.HasKey(e => e.UserId);
            b.Property(e => e.UserId).HasColumnName("user_id");
            b.Property(e => e.Points).HasColumnName("points");
            b.Property(e => e.Xp).HasColumnName("xp");
            b.Property(e => e.Svac).HasColumnName("svac");
            b.Property(e => e.Watermark).HasColumnName("watermark");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ConfigEntryEntity>(b =>
        {
            b.ToTable("config_entries", "core", t =>
                t.HasCheckConstraint("ck_config_entries_scope", "scope IN ('founder','ops','set')"));
            b.HasKey(e => e.Key);
            b.Property(e => e.Key).HasColumnName("key");
            b.Property(e => e.Type).HasColumnName("type");
            b.Property(e => e.ValueJson).HasColumnName("value").HasColumnType("jsonb");
            b.Property(e => e.Scope).HasColumnName("scope");
            b.Property(e => e.Gate).HasColumnName("gate");
            b.Property(e => e.BoundsJson).HasColumnName("bounds").HasColumnType("jsonb");
            b.Property(e => e.RequiresReason).HasColumnName("requires_reason").HasDefaultValue(false);
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            b.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        });

        modelBuilder.Entity<QuotaCounterEntity>(b =>
        {
            b.ToTable("quota_counters", "core");
            b.HasKey(e => new { e.ActorRef, e.QuotaKey, e.WindowKey });
            b.Property(e => e.ActorRef).HasColumnName("actor_ref");
            b.Property(e => e.QuotaKey).HasColumnName("quota_key");
            b.Property(e => e.WindowKey).HasColumnName("window_key");
            b.Property(e => e.Consumed).HasColumnName("consumed").HasDefaultValue(0);
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<PurgeRunEntity>(b =>
        {
            b.ToTable("purge_runs", "core");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.PurgeClass).HasColumnName("purge_class");
            b.Property(e => e.SubjectRef).HasColumnName("subject_ref");
            b.Property(e => e.StoreKey).HasColumnName("store_key");
            b.Property(e => e.RowsAffected).HasColumnName("rows_affected");
            b.Property(e => e.StartedAt).HasColumnName("started_at");
            b.Property(e => e.CompletedAt).HasColumnName("completed_at");
            b.Property(e => e.EvidenceJson).HasColumnName("evidence").HasColumnType("jsonb");
        });

        modelBuilder.Entity<DataProtectionKeyEntity>(b =>
        {
            b.ToTable("data_protection_keys", "core");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            b.Property(e => e.FriendlyName).HasColumnName("friendly_name");
            b.Property(e => e.Xml).HasColumnName("xml_data");
        });

        modelBuilder.Entity<FieldKeyRefEntity>(b =>
        {
            b.ToTable("field_key_refs", "core");
            b.HasKey(e => e.FieldKeyId);
            b.Property(e => e.FieldKeyId).HasColumnName("field_key_id");
            b.Property(e => e.VaultKeyName).HasColumnName("vault_key_name");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.RetiredAt).HasColumnName("retired_at");
        });
    }
}
