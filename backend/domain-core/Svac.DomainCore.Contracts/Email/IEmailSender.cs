namespace Svac.DomainCore.Contracts.Email;

/// <summary>One outbound email — keyed ×4 locales, NEVER prose (PHASE_2A_SUBSTRATE.md §2, SLICE_S3_CONTRACT.md §1b).</summary>
public sealed record EmailMessage(string To, string TemplateKey, string Locale, IReadOnlyDictionary<string, string> Model);

/// <summary>Closed outcome union for a send attempt (PHASE_2A_SUBSTRATE.md §2).</summary>
public abstract record EmailResult
{
    public sealed record SentResult(string ProviderReference) : EmailResult;

    public sealed record FailedResult(string ReasonKey) : EmailResult;

    public static EmailResult Sent(string providerReference) => new SentResult(providerReference);
    public static EmailResult Failed(string reasonKey) => new FailedResult(reasonKey);
}

/// <summary>
/// The email door (PHASE_2A_SUBSTRATE.md §2, SLICE_S3_CONTRACT.md §1b — the <c>IPaymentService</c>
/// buy-side-seam precedent: this interface lives in domain-core, never in a pre-created future module).
/// The fail-closed default <c>ThrowingEmailSender</c> lives in <c>Svac.DomainCore.Email</c>. The real
/// SMTP transport (<c>SmtpEmailSender</c>) is DEFERRED to the S3 build — neither is DI-registered by
/// S1/S2 hosts, so there is no consumer and no boot throw.
/// </summary>
public interface IEmailSender
{
    public Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default);
}
