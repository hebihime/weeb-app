namespace Svac.Identity.Config;

/// <summary>The 9A config keys this build's endpoints actually read (SLICE_S3_CONTRACT.md §4; backend/modules/identity/config/identity.config.json carries the seeded values).</summary>
public static class IdentityConfigKeys
{
    public const string SessionAccessTtlMinutes = "identity.session.access_ttl_minutes";
    public const string SessionRefreshTtlDays = "identity.session.refresh_ttl_days";
    public const string SessionMaxActivePerAccount = "identity.session.max_active_per_account";
    public const string EmailCodeTtlMinutes = "identity.email_code.ttl_minutes";
    public const string EmailCodeMaxAttempts = "identity.email_code.max_attempts";
    public const string SignupVerifiedTokenTtlMinutes = "identity.signup.verified_token_ttl_minutes";
    /// <summary>[/v1/me/handle build] Identity-local cooldown (SLICE_S3_CONTRACT.md §4/§5) — NOT a 10A key; the deny still serializes as the one LimitReached shape.</summary>
    public const string HandleCooldownDays = "identity.handle.cooldown_days";
    /// <summary>[export build] Export download-link TTL + sweep window (SLICE_S3_CONTRACT.md §4) — real consumer: ExportWorker.MarkReadyAsync's expiresAt.</summary>
    public const string ExportLinkTtlHours = "identity.export.link_ttl_hours";
    /// <summary>[export build] The Art. 12(3) statutory clock (SLICE_S3_CONTRACT.md §4) — real consumer: ExportWorker's manifest.json `statutoryDeadlineAt` annotation (the S5 desk's future render target).</summary>
    public const string ExportStatutoryDeadlineDays = "identity.export.statutory_deadline_days";
    /// <summary>[deletion build] identity.email_challenges' 13A retention_expiry sweep window (SLICE_S3_CONTRACT.md §4) — real consumer: EmailChallengesPurgeStoreExecutor's RetentionExpiry age gate.</summary>
    public const string EmailChallengeRetentionHours = "identity.email_challenge.retention_hours";
    /// <summary>[deletion build] Post-deletion identity.handle_history sweep window (SLICE_S3_CONTRACT.md §4, impersonation-defense) — real consumer: HandleHistoryPurgeStoreExecutor's RetentionExpiry age gate.</summary>
    public const string HandleHistoryRetentionMonths = "identity.handle_history.retention_months";

    // --- Deletion pipeline (SLICE_S3_CONTRACT.md §2/§4, THIS build) ---

    /// <summary>Phase L grace window: deletion_effective_at = now + grace_days, bounds [0,30] — 0 is bounds-legal so the E2E's worker executes live (§4).</summary>
    public const string DeletionGraceDays = "identity.deletion.grace_days";
    /// <summary>Deletion/expiry worker interval (SLICE_S3_CONTRACT.md §4) — real consumer: the sweep worker's polling cadence.</summary>
    public const string DeletionSweepMinutes = "identity.deletion.sweep_minutes";
    /// <summary>Phase P's cap on waiting for a pending export to finish before proceeding with the purge (SLICE_S3_CONTRACT.md §2/§4).</summary>
    public const string ExportPreDeletionWaitHours = "identity.export.pre_deletion_wait_hours";
    /// <summary>OQ-2's quarantine window before a retired handle is eligible for release (SLICE_S3_CONTRACT.md §4) — registered/consumed at the config layer even though the dedicated global release sweep is out of this pass's scope (see IdentityPurgeRegistrySource's identity.retired_handles RetentionExpiry reason).</summary>
    public const string HandleRetirementDays = "identity.handle.retirement_days";
    /// <summary>[OPS-3/OPS-5, SECURITY_REVIEW_S3.md] The desk-facing mirror of the real enforced cap (<see cref="IdentityQuotaKeys.ExportRequestDaily"/>'s backing `quota.identity.export.request.daily.cap` row, per QuotaService's fixed naming convention) — named here only so the §4 statutory floor (bounds [1,10], "no ops edit can zero a legal right") has a typed reference for OPS-3's bounds-enforcement test. Not read by QuotaService.Consume directly (OPS-5, deferred: the dual-key divergence itself).</summary>
    public const string ExportDailyCap = "identity.export.daily_cap";
}

/// <summary>The 10A quota keys this build's endpoints consume (SLICE_S3_CONTRACT.md §5).</summary>
public static class IdentityQuotaKeys
{
    public const string EmailSendDaily = "identity.email.send.daily";
    /// <summary>[/v1/me/devices build] Push-token churn brake (SLICE_S3_CONTRACT.md §5), cap `identity.device.register_daily_cap` behind the `quota.identity.device.register.daily.cap` config row.</summary>
    public const string DeviceRegisterDaily = "identity.device.register.daily";
    /// <summary>[export build] Statutory export request cap (SLICE_S3_CONTRACT.md §5), cap `identity.export.daily_cap` behind the `quota.identity.export.request.daily.cap` config row. Deny = 429 LimitReached, floor 1 (no ops edit can zero a legal right).</summary>
    public const string ExportRequestDaily = "identity.export.request.daily";
}
