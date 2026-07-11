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
