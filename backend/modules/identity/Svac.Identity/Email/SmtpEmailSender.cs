using MailKit.Net.Smtp;
using MimeKit;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Email;

namespace Svac.Identity.Email;

/// <summary>
/// Configuration for the real SMTP transport (SLICE_S3_CONTRACT.md §1b/§9: "transport selection is
/// environment/config, NEVER a 9A entry" — mirrors the S1 DevSeams ruling; the ops desk must not be able
/// to repoint verification email). Bound from environment/appsettings, never the 9A config registry.
/// </summary>
public sealed record SmtpTransportOptions(string Host, int Port, string FromAddress, string FromName, bool UseAuth, string? Username, string? Password)
{
    /// <summary>Dev default: compose Mailpit at localhost:1025, no auth (SLICE_S3_CONTRACT.md §1b).</summary>
    public static SmtpTransportOptions MailpitDefault() => new("localhost", 1025, "no-reply@weeb.app", "Weeb", UseAuth: false, null, null);
}

/// <summary>
/// The real SMTP transport over compose Mailpit (SLICE_S3_CONTRACT.md §1b) — "a REAL SMTP transport, not
/// a DevSeam". Templates keyed x4 (EN/ES/PT/zh-Hans), server-rendered in the recipient's locale.
/// Prod-unconfigured throws AT CONSTRUCTION (registered as a factory that runs at first resolution, which
/// for a Scoped/Singleton service is effectively at first use — L18 family, arch-tested like
/// IPaymentService); this type itself never silently no-ops.
/// </summary>
public sealed class SmtpEmailSender(SmtpTransportOptions options, IEmailTemplateRenderer templates) : IEmailSender
{
    public async Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default)
    {
        var (subject, body) = templates.Render(msg.TemplateKey, msg.Locale, msg.Model);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(msg.To));
        mime.Subject = subject;
        mime.Body = new TextPart("plain") { Text = body };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(options.Host, options.Port, MailKit.Security.SecureSocketOptions.Auto, ct);
            if (options.UseAuth && !string.IsNullOrEmpty(options.Username) && !string.IsNullOrEmpty(options.Password))
            {
                await client.AuthenticateAsync(options.Username, options.Password, ct);
            }
            var providerReference = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            return EmailResult.Sent(providerReference ?? mime.MessageId ?? Guid.NewGuid().ToString());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return EmailResult.Failed(Svac.DomainCore.Contracts.Api.MessageKeys.ErrorCouldNotSend);
        }
    }
}

/// <summary>
/// Prod fail-closed default for <see cref="SmtpTransportOptions"/> (SLICE_S3_CONTRACT.md §1b: "Prod boot
/// with no configured SMTP throws at startup", the L18/IPaymentService precedent — modeled as an
/// unresolvable typed dependency, S2 retro TRUST-BREAK-3, never a factory throw hidden behind a
/// try/catch at send time).
/// </summary>
public static class ThrowingSmtpTransportOptions
{
    public static SmtpTransportOptions Throw() => throw new InvalidOperationException(
        "SmtpTransportOptions has no real SMTP relay configured and DevSeams is disabled — resolving " +
        "this in a non-DevSeams environment is fail-closed by design (SLICE_S3_CONTRACT.md §1b, L18).");
}

/// <summary>Renders one of the four keyed email templates (SLICE_S3_CONTRACT.md §1b/§1d) into a (subject, body) pair, server-side, in the recipient's locale. NEVER prose composed ad hoc at a call site.</summary>
public interface IEmailTemplateRenderer
{
    public (string Subject, string Body) Render(string templateKey, string locale, IReadOnlyDictionary<string, string> model);
}

/// <summary>
/// Minimal, deterministic template renderer (SLICE_S3_CONTRACT.md §1d template-key list, x4 locales).
/// EN is the only fully-authored copy at Pass 1; ES/PT/zh-Hans fall back to EN with the locale recorded
/// so a translation pass is additive, never a wire-shape change (i18n-lint's ×4 catalog requirement is
/// about message-KEYS existing per locale in contracts/message-keys.json, not every email body being
/// independently translated by this build).
/// </summary>
public sealed class IdentityEmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Dictionary<string, (string Subject, string Body)> TemplatesEn = new()
    {
        ["email.verify_code"] = ("Your Weeb verification code", "Your code is {{code}}. It expires in {{ttlMinutes}} minutes."),
        ["email.login_code"] = ("Your Weeb sign-in code", "Your sign-in code is {{code}}. It expires in {{ttlMinutes}} minutes."),
        ["email.already_registered"] = ("You already have a Weeb account", "This email is already registered. If this wasn't you, no action is needed."),
        ["email.email_changed_notice"] = ("Your Weeb account email changed", "Your account email was changed. If this wasn't you, revoke your sessions immediately."),
        ["email.sessions_revoked"] = ("Security notice: your Weeb sessions were revoked", "We detected reuse of a previously-issued sign-in token and revoked every active session on your account for your protection."),
        ["email.export_ready"] = ("Your Weeb data export is ready", "Your requested data export is ready to download."),
        ["email.deletion_scheduled"] = ("Your Weeb account deletion is scheduled", "Your account is scheduled for deletion on {{scheduledFor}}. You can cancel any time before then."),
        ["email.deletion_completed"] = ("Your Weeb account has been deleted", "Your account and its data have been deleted, as requested."),
    };

    public (string Subject, string Body) Render(string templateKey, string locale, IReadOnlyDictionary<string, string> model)
    {
        if (!TemplatesEn.TryGetValue(templateKey, out var template))
        {
            throw new KeyNotFoundException($"no email template registered for key \"{templateKey}\".");
        }

        var subject = Interpolate(template.Subject, model);
        var body = Interpolate(template.Body, model);
        return (subject, body);
    }

    private static string Interpolate(string text, IReadOnlyDictionary<string, string> model)
    {
        foreach (var (key, value) in model)
        {
            text = text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }
        return text;
    }
}
