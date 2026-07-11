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
}

/// <summary>The 10A quota keys this build's endpoints consume (SLICE_S3_CONTRACT.md §5).</summary>
public static class IdentityQuotaKeys
{
    public const string EmailSendDaily = "identity.email.send.daily";
}
