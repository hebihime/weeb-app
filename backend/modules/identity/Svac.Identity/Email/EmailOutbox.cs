using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Email;

namespace Svac.Identity.Email;

/// <summary>
/// The real, possibly-slow outbound transport the outbox dispatcher drains into (SECURITY_REVIEW_S3.md
/// MAIL-1) — <see cref="SmtpEmailSender"/> in prod/dev, a test double in Svac.Tests.Identity. Distinct from
/// <see cref="IEmailSender"/> (which the REQUEST path resolves, via <see cref="OutboxEmailSender"/>) so DI
/// can bind the two independently: swapping the real transport for a test double must never also swap out
/// the enqueue-only request-path behavior the whole fix depends on.
/// </summary>
public interface IEmailTransport
{
    public Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default);
}

/// <summary>One enqueued outbound email, with the RequestContext its send should be audited/rendered under.</summary>
public sealed record QueuedEmail(EmailMessage Message, RequestContext Ctx);

/// <summary>
/// The in-process mail queue (SECURITY_REVIEW_S3.md MAIL-1: "an in-process System.Threading.Channels.Channel
/// producer + a BackgroundService consumer"). Unbounded — outbound mail volume is already capped by 10A's
/// per-mailbox `identity.email.send.daily` quota (§5) before anything reaches this queue, so an unbounded
/// channel cannot become an unbounded memory leak under normal operation; a construction failure of the
/// dispatcher itself (not modeled here) would need its own alarm, same as any other hosted service.
/// </summary>
public sealed class EmailOutbox
{
    private readonly Channel<QueuedEmail> _channel = Channel.CreateUnbounded<QueuedEmail>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<QueuedEmail> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(QueuedEmail item, CancellationToken ct) => _channel.Writer.WriteAsync(item, ct);
}

/// <summary>
/// The request-path-facing <see cref="IEmailSender"/> (SECURITY_REVIEW_S3.md MAIL-1): enqueues onto
/// <see cref="EmailOutbox"/> and returns immediately — NEVER awaits SMTP I/O in-band. This is the whole fix:
/// every existing call site (EmailChallengeMachine, MeEndpoints, AccountLifecycle,
/// DeletionPhysicalPurgeWorker, RefreshRotationService, ExportWorker) keeps calling
/// <c>emailSender.SendAsync(...)</c> exactly as before — only what that call now DOES changes. A live
/// account's real SMTP round-trip can no longer leak into request latency because no request ever performs
/// one; TimingFloor's floor-not-ceiling limitation (SECURITY_REVIEW_S3.md's "root cause" note) stops
/// mattering once every branch's dominant cost is DB-local.
/// </summary>
public sealed class OutboxEmailSender(EmailOutbox outbox) : IEmailSender
{
    public async Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default)
    {
        await outbox.EnqueueAsync(new QueuedEmail(msg, ctx), ct);
        return EmailResult.Sent("queued");
    }
}

/// <summary>
/// Drains <see cref="EmailOutbox"/> off the request thread (SECURITY_REVIEW_S3.md MAIL-1) and dispatches
/// each item through the real <see cref="IEmailTransport"/> (SmtpEmailSender in prod/dev). One DI scope per
/// item (IEmailTransport may be Scoped) so a slow/failing send never pins a single long-lived scope. A
/// per-item failure is logged and swallowed — dropping the request thread's dependency on delivery success
/// was the entire point of this queue; S4 owns real delivery/retry semantics (the 3A event for the
/// triggering action already fired before the caller ever reached SendAsync).
/// </summary>
public sealed partial class EmailOutboxDispatcher(
    EmailOutbox outbox,
    IServiceScopeFactory scopeFactory,
    ILogger<EmailOutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in outbox.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchOne(item, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown — ReadAllAsync surfaces cancellation as an exception; never log it as a fault.
        }
    }

    private async Task DispatchOne(QueuedEmail item, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
            await transport.SendAsync(item.Message, item.Ctx, ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never log the raw recipient (msg.To) — matches every other identity log call site's "no
            // raw email/secret in logs" discipline (SECURITY_REVIEW_S3.md's own verified-sound note).
            LogDispatchFailed(ex, item.Message.TemplateKey);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "EmailOutboxDispatcher: dispatch failed for template {TemplateKey}")]
    private partial void LogDispatchFailed(Exception ex, string templateKey);
}
