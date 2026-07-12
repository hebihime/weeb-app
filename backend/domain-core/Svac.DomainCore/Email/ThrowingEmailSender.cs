using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Email;

namespace Svac.DomainCore.Email;

/// <summary>
/// The fail-closed default <see cref="IEmailSender"/> (PHASE_2A_SUBSTRATE.md §2, the <c>ThrowingPaymentService</c>
/// family). The real SMTP transport (<c>SmtpEmailSender</c>) is DEFERRED to the S3 build. NOT DI-registered
/// by this surgery (neither S1 nor S2 has a consumer) — this class exists so S3 has a concrete fail-closed
/// default to fall back to the moment it registers a real transport conditionally.
/// </summary>
public sealed class ThrowingEmailSender : IEmailSender
{
    public Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "IEmailSender has no real transport configured (PHASE_2A_SUBSTRATE.md §2) — the SMTP transport " +
            "is S3 build scope. This throw is deliberate fail-closed behavior, not a bug.");
}
