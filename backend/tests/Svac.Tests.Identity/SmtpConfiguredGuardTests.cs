using Svac.Identity.Email;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// OPS-2 (SECURITY_REVIEW_S3.md, HIGH): the old "SMTP fail-closed at startup" was a lazy DI factory throw,
/// discovered only at the first signup/login-code/email-change/export request — a prod deploy with no
/// SMTP configured booted clean, passed /health, then 500'd on first use. <see cref="SmtpConfiguredGuard.Enforce"/>
/// mirrors <c>ProdFieldKeyVaultGuard.Enforce</c> exactly: an explicit, synchronous check called once at
/// startup, before the host serves a single request.
/// </summary>
public sealed class SmtpConfiguredGuardTests
{
    [Fact]
    public void Enforce_ProductionWithNoSmtp_Throws()
    {
        var ex = Record.Exception(() => SmtpConfiguredGuard.Enforce(
            environmentName: "Production",
            devSeamsEnabled: false,
            smtpConfigured: false));

        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("QA")]
    [InlineData("Preview")]
    public void Enforce_AnyNonDevelopmentEnvironmentWithNoSmtp_Throws_Trust_F1_AllowlistNotBlocklist(string environmentName)
    {
        // Trust-F1 (SECURITY_REVIEW_S1.md): allowlist "Development" by NAME rather than blocklisting
        // "Production" — every other environment name must fail closed too, exactly like
        // ProdFieldKeyVaultGuard's own TrustBoundaryLensTests coverage.
        var ex = Record.Exception(() => SmtpConfiguredGuard.Enforce(
            environmentName: environmentName,
            devSeamsEnabled: false,
            smtpConfigured: false));

        Assert.NotNull(ex);
    }

    [Fact]
    public void Enforce_DevelopmentWithDevSeamsAndMailpitConfigured_DoesNotThrow()
    {
        // The real docker-compose.yml/Program.cs shape: ASPNETCORE_ENVIRONMENT=Development,
        // SVAC_DEVSEAMS_ENABLED=true -> smtpOptions = SmtpTransportOptions.MailpitDefault(...) -> smtpConfigured=true.
        var ex = Record.Exception(() => SmtpConfiguredGuard.Enforce(
            environmentName: "Development",
            devSeamsEnabled: true,
            smtpConfigured: true));

        Assert.Null(ex);
    }

    [Fact]
    public void Enforce_DevelopmentWithNoSmtpConfigured_DoesNotThrow()
    {
        // Development may legitimately boot without SMTP (e.g. DevSeams off, no identity-module exercise
        // intended this run) — only non-Development environments are required to have it configured.
        var ex = Record.Exception(() => SmtpConfiguredGuard.Enforce(
            environmentName: "Development",
            devSeamsEnabled: false,
            smtpConfigured: false));

        Assert.Null(ex);
    }

    [Fact]
    public void Enforce_ProductionWithSmtpConfigured_DoesNotThrow()
    {
        var ex = Record.Exception(() => SmtpConfiguredGuard.Enforce(
            environmentName: "Production",
            devSeamsEnabled: false,
            smtpConfigured: true));

        Assert.Null(ex);
    }
}
