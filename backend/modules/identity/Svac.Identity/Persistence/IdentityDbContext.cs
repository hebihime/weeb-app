using Microsoft.EntityFrameworkCore;

namespace Svac.Identity.Persistence;

/// <summary>
/// The schema `identity` DbContext (SLICE_S3_CONTRACT.md §2/§8) — the SECOND module-owned schema after
/// `core`, module-owned solely by this type. Every PII row carries region + lawful_basis NOT NULL (L21).
/// This BUILD ships the FULL §2 schema (all 12 named tables + the OQ-3 ban_evasion_refs store) in ONE EF
/// migration; export_jobs/deletion_jobs exist as schema only — their pipelines are Pass 2 (§0 DO-NOT list).
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<EmailChallengeEntity> EmailChallenges => Set<EmailChallengeEntity>();
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<PushCategoryConsentEntity> PushCategoryConsents => Set<PushCategoryConsentEntity>();
    public DbSet<ConsentCurrentEntity> ConsentCurrent => Set<ConsentCurrentEntity>();
    public DbSet<HandleHistoryEntity> HandleHistory => Set<HandleHistoryEntity>();
    public DbSet<ReservedHandleEntity> ReservedHandles => Set<ReservedHandleEntity>();
    public DbSet<RetiredHandleEntity> RetiredHandles => Set<RetiredHandleEntity>();
    public DbSet<ExportJobEntity> ExportJobs => Set<ExportJobEntity>();
    public DbSet<DeletionJobEntity> DeletionJobs => Set<DeletionJobEntity>();
    public DbSet<BanEvasionRefEntity> BanEvasionRefs => Set<BanEvasionRefEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<AccountEntity>(b =>
        {
            b.ToTable("accounts", "identity", t =>
                t.HasCheckConstraint("ck_accounts_state", "account_state IN ('active','suspended','banned','deleted')"));
            b.HasKey(e => e.AccountId);
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.Handle).HasColumnName("handle");
            b.Property(e => e.Email).HasColumnName("email");
            b.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
            b.Property(e => e.BirthdateEnc).HasColumnName("birthdate_enc");
            b.Property(e => e.AttestedAdultAt).HasColumnName("attested_adult_at");
            b.Property(e => e.TermsVersion).HasColumnName("terms_version");
            b.Property(e => e.FandomTag).HasColumnName("fandom_tag");
            b.Property(e => e.AvatarRef).HasColumnName("avatar_ref");
            b.Property(e => e.Locale).HasColumnName("locale");
            b.Property(e => e.AccountState).HasColumnName("account_state").HasDefaultValue("active");
            b.Property(e => e.IrlAccessState).HasColumnName("irl_access_state").HasDefaultValue("active");
            b.Property(e => e.StateChangedAt).HasColumnName("state_changed_at");
            b.Property(e => e.DeletionRequestedAt).HasColumnName("deletion_requested_at");
            b.Property(e => e.DeletionEffectiveAt).HasColumnName("deletion_effective_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.LastActiveAt).HasColumnName("last_active_at");
            b.Property(e => e.TombstonedAt).HasColumnName("tombstoned_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.RegionSource).HasColumnName("region_source");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            // Partial unique indexes (§2). PII-3 / CONC-4 (SECURITY_REVIEW_S3.md, grace-window identity
            // takeover): gated on `tombstoned_at IS NULL`, NOT `account_state <> 'deleted'`. A Phase-L
            // grace-deleted row (account_state='deleted', tombstoned_at still NULL) must still occupy
            // its handle/email slot in this index — only the PHYSICAL purge (Phase P: tombstoned_at set,
            // handle overwritten with a retired sentinel, email nulled) may free it. The old
            // `account_state <> 'deleted'` filter excluded a grace-deleted row from the index entirely,
            // so a third party could claim the still-live handle/email DURING grace — and CancelDeletion
            // restoring account_state='active' would then collide with the squatter's row (an uncaught
            // 23505, permanently destroying the cancel right; see AccountLifecycle.CancelDeletion's
            // defensive catch). Gating on tombstoned_at makes this index and the C# availability checks
            // (SignupEndpoints.GetHandleAvailability, EmailChallengeMachine.IssueForSignup) free the slot
            // at EXACTLY the same instant, by construction.
            b.HasIndex(e => e.Handle).IsUnique().HasFilter("tombstoned_at IS NULL").HasDatabaseName("ux_accounts_handle");
            b.HasIndex(e => e.Email).IsUnique().HasFilter("tombstoned_at IS NULL AND email IS NOT NULL").HasDatabaseName("ux_accounts_email");
            b.HasIndex(e => e.DeletionEffectiveAt).HasFilter("deletion_effective_at IS NOT NULL").HasDatabaseName("ix_accounts_deletion_due");
        });

        modelBuilder.Entity<EmailChallengeEntity>(b =>
        {
            b.ToTable("email_challenges", "identity", t =>
                t.HasCheckConstraint("ck_email_challenges_purpose", "purpose IN ('signup','login','email_change')"));
            b.HasKey(e => e.ChallengeId);
            b.Property(e => e.ChallengeId).HasColumnName("challenge_id");
            b.Property(e => e.Purpose).HasColumnName("purpose");
            b.Property(e => e.EmailLower).HasColumnName("email_lower");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.CodeHash).HasColumnName("code_hash");
            b.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            b.Property(e => e.VerifiedAt).HasColumnName("verified_at");
            b.Property(e => e.VerifiedTokenHash).HasColumnName("verified_token_hash");
            b.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.Locale).HasColumnName("locale");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.EmailLower).HasDatabaseName("ix_email_challenges_email_lower");
            b.HasIndex(e => e.AccountId).HasDatabaseName("ix_email_challenges_account");
        });

        modelBuilder.Entity<SessionEntity>(b =>
        {
            b.ToTable("sessions", "identity");
            b.HasKey(e => e.SessionId);
            b.Property(e => e.SessionId).HasColumnName("session_id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.DeviceId).HasColumnName("device_id");
            b.Property(e => e.AccessTokenHash).HasColumnName("access_token_hash");
            b.Property(e => e.RefreshFamilyId).HasColumnName("refresh_family_id");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            b.Property(e => e.AccessExpiresAt).HasColumnName("access_expires_at");
            b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            b.Property(e => e.RevokeReason).HasColumnName("revoke_reason");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.AccessTokenHash).IsUnique().HasDatabaseName("ux_sessions_access_token_hash");
            b.HasIndex(e => e.AccountId).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_sessions_account");
        });

        modelBuilder.Entity<RefreshTokenEntity>(b =>
        {
            b.ToTable("refresh_tokens", "identity");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.SessionId).HasColumnName("session_id");
            b.Property(e => e.TokenHash).HasColumnName("token_hash");
            b.Property(e => e.FamilyId).HasColumnName("family_id");
            b.Property(e => e.IssuedAt).HasColumnName("issued_at");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
            b.Property(e => e.SupersededBy).HasColumnName("superseded_by");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
            b.HasIndex(e => e.FamilyId).HasDatabaseName("ix_refresh_tokens_family");
        });

        modelBuilder.Entity<DeviceEntity>(b =>
        {
            b.ToTable("devices", "identity", t =>
                t.HasCheckConstraint("ck_devices_platform", "platform IN ('ios','android','web')"));
            b.HasKey(e => e.DeviceId);
            b.Property(e => e.DeviceId).HasColumnName("device_id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.Platform).HasColumnName("platform");
            b.Property(e => e.PushToken).HasColumnName("push_token");
            b.Property(e => e.PushTokenUpdatedAt).HasColumnName("push_token_updated_at");
            b.Property(e => e.CreatedAt).HasColumnName("created_at");
            b.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            b.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.AccountId).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_devices_account");
        });

        modelBuilder.Entity<PushCategoryConsentEntity>(b =>
        {
            b.ToTable("push_category_consents", "identity", t =>
                t.HasCheckConstraint("ck_push_category_consents_category", "category BETWEEN 1 AND 9 AND category <> 8"));
            b.HasKey(e => new { e.AccountId, e.Category });
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.Category).HasColumnName("category");
            b.Property(e => e.Enabled).HasColumnName("enabled");
            b.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");
        });

        modelBuilder.Entity<ConsentCurrentEntity>(b =>
        {
            b.ToTable("consent_current", "identity", t =>
                t.HasCheckConstraint("ck_consent_current_status", "status IN ('granted','revoked')"));
            b.HasKey(e => new { e.AccountId, e.ConsentKind });
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.ConsentKind).HasColumnName("consent_kind");
            b.Property(e => e.Version).HasColumnName("version");
            b.Property(e => e.Status).HasColumnName("status");
            b.Property(e => e.Surface).HasColumnName("surface");
            b.Property(e => e.DecidedAt).HasColumnName("decided_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");
        });

        modelBuilder.Entity<HandleHistoryEntity>(b =>
        {
            b.ToTable("handle_history", "identity");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.OldHandle).HasColumnName("old_handle");
            b.Property(e => e.NewHandle).HasColumnName("new_handle");
            b.Property(e => e.ChangedAt).HasColumnName("changed_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.AccountId).HasDatabaseName("ix_handle_history_account");
        });

        modelBuilder.Entity<ReservedHandleEntity>(b =>
        {
            b.ToTable("reserved_handles", "identity");
            b.HasKey(e => e.Handle);
            b.Property(e => e.Handle).HasColumnName("handle");
            b.Property(e => e.Reason).HasColumnName("reason");
        });

        modelBuilder.Entity<RetiredHandleEntity>(b =>
        {
            b.ToTable("retired_handles", "identity");
            b.HasKey(e => e.Handle);
            b.Property(e => e.Handle).HasColumnName("handle");
            b.Property(e => e.RetiredAt).HasColumnName("retired_at");
        });

        modelBuilder.Entity<ExportJobEntity>(b =>
        {
            b.ToTable("export_jobs", "identity", t =>
                t.HasCheckConstraint("ck_export_jobs_state", "state IN ('pending','ready','delivered','expired','failed')"));
            b.HasKey(e => e.ExportId);
            b.Property(e => e.ExportId).HasColumnName("export_id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.State).HasColumnName("state");
            b.Property(e => e.Artifact).HasColumnName("artifact");
            b.Property(e => e.ManifestJson).HasColumnName("manifest").HasColumnType("jsonb");
            b.Property(e => e.RequestedAt).HasColumnName("requested_at");
            b.Property(e => e.ReadyAt).HasColumnName("ready_at");
            b.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.AccountId).IsUnique().HasFilter("state IN ('pending','ready')").HasDatabaseName("ux_export_active");
        });

        modelBuilder.Entity<DeletionJobEntity>(b =>
        {
            b.ToTable("deletion_jobs", "identity", t =>
                t.HasCheckConstraint("ck_deletion_jobs_state", "state IN ('scheduled','canceled','executing','held','complete')"));
            b.HasKey(e => e.DeletionId);
            b.Property(e => e.DeletionId).HasColumnName("deletion_id");
            b.Property(e => e.AccountId).HasColumnName("account_id");
            b.Property(e => e.State).HasColumnName("state");
            b.Property(e => e.RequestedAt).HasColumnName("requested_at");
            b.Property(e => e.ScheduledFor).HasColumnName("scheduled_for");
            b.Property(e => e.ExecutingSince).HasColumnName("executing_since");
            b.Property(e => e.ExportOffered).HasColumnName("export_offered").HasDefaultValue(true);
            b.Property(e => e.CustodyHoldsFound).HasColumnName("custody_holds_found");
            b.Property(e => e.CustodyHoldRefsJson).HasColumnName("custody_hold_refs").HasColumnType("jsonb");
            b.Property(e => e.ExecutedAt).HasColumnName("executed_at");
            b.Property(e => e.PurgeRunIdsJson).HasColumnName("purge_run_ids").HasColumnType("jsonb");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");

            b.HasIndex(e => e.AccountId).HasDatabaseName("ix_deletion_jobs_account");
        });

        modelBuilder.Entity<BanEvasionRefEntity>(b =>
        {
            b.ToTable("ban_evasion_refs", "identity");
            b.HasKey(e => e.HmacEmail);
            b.Property(e => e.HmacEmail).HasColumnName("hmac_email");
            b.Property(e => e.PushTokenHash).HasColumnName("push_token_hash");
            b.Property(e => e.BannedAt).HasColumnName("banned_at");
            b.Property(e => e.Region).HasColumnName("region");
            b.Property(e => e.LawfulBasis).HasColumnName("lawful_basis");
        });
    }
}
