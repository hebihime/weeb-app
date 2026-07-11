namespace Svac.Identity.Email;

/// <summary>
/// Prod-without-SMTP THROWS at startup (SECURITY_REVIEW_S3.md OPS-2). Mirrors
/// <see cref="Svac.DomainCore.FieldEncryption.ProdFieldKeyVaultGuard"/> exactly: an explicit, unresolvable
/// check called once during host startup — BEFORE the first request is served — never a lazy DI factory
/// that only throws the first time something resolves <see cref="IEmailTransport"/>. The old shape
/// (<c>AddScoped&lt;IEmailSender&gt;(_ =&gt; throw ...)</c>) boots clean, passes <c>/health</c>, and only
/// fails on the first real signup/login-code/email-change/export request — this call replaces that with a
/// genuine boot-time refusal.
///
/// Trust-F1 (SECURITY_REVIEW_S1.md): allowlist the one safe environment (Development) by NAME, never
/// blocklist the one unsafe one (Production) — Staging/QA/Preview must fail closed too.
/// </summary>
public static class SmtpConfiguredGuard
{
    /// <param name="environmentName">The hosting environment's EnvironmentName (e.g. ASPNETCORE_ENVIRONMENT).</param>
    /// <param name="devSeamsEnabled">The DevSeams environment flag — informational only in the exception message; DevSeams itself is enforced by <see cref="Svac.DomainCore.FieldEncryption.ProdFieldKeyVaultGuard"/>, called earlier in Program.cs.</param>
    /// <param name="smtpConfigured">True once real <see cref="Svac.Identity.Email.SmtpTransportOptions"/> are wired (the caller's own environment/config decision, never a 9A entry).</param>
    public static void Enforce(string environmentName, bool devSeamsEnabled, bool smtpConfigured)
    {
        var isDevelopment = string.Equals(environmentName, Microsoft.Extensions.Hosting.Environments.Development, StringComparison.OrdinalIgnoreCase);

        if (!isDevelopment && !smtpConfigured)
        {
            throw new InvalidOperationException(
                $"Startup in environment \"{environmentName}\" (devSeamsEnabled={devSeamsEnabled}) with no SMTP " +
                "transport configured — IEmailTransport has no real backend for the outbox dispatcher to drain " +
                "into (SECURITY_REVIEW_S3.md OPS-2, SLICE_S3_CONTRACT.md §1b, L18 fail-closed AT STARTUP, not at " +
                "first send). Configure SVAC_SMTP_HOST/credentials before deploying to any environment other " +
                "than Development.");
        }
    }
}
