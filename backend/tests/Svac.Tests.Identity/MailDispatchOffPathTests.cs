using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.Hosting;
using Svac.Identity.DependencyInjection;
using Svac.Identity.Email;
using Svac.Identity.Endpoints;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// MAIL-1 (SECURITY_REVIEW_S3.md, HIGH — account-existence timing oracle): outbound mail used to be
/// <c>await</c>ed IN-BAND inside the challenge-issuance call, so a live account's real SMTP round-trip
/// (100ms-1s against a real relay) leaked straight into request latency, while the absent/banned branch
/// (no mail, floored to 60ms) stayed fast. The fix dispatches mail OFF the request path (an in-process
/// <c>Channel</c> producer/<c>BackgroundService</c> consumer outbox) — the request enqueues and returns
/// before the SMTP call ever happens.
///
/// This is the STRONGEST possible proof, not a wall-clock race: <see cref="GatedEmailTransport"/> blocks
/// on a manual gate this test controls. If the request path awaited the transport in-band, the HTTP call
/// would hang until <see cref="GatedEmailTransport.Release"/> is called — proven here by racing the HTTP
/// response against a bounded timeout while the gate is still closed.
///
/// Deliberately its OWN host/container (not IdentityHttpFixture's shared collection): that fixture
/// overrides <see cref="IEmailSender"/> directly with a synchronous fake, which bypasses the outbox
/// entirely — the exact seam this suite needs to exercise is <see cref="IEmailTransport"/>, the outbox
/// dispatcher's OWN downstream, with <see cref="IEmailSender"/> left wired to the real
/// <see cref="OutboxEmailSender"/>.
/// </summary>
public sealed class MailDispatchOffPathTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private static readonly string[] SupportedLocales = { "en", "es", "pt", "zh-Hans" };

    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private GatedEmailTransport _transport = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        using (var coreDb = new Svac.DomainCore.Persistence.CoreDbContext(new DbContextOptionsBuilder<Svac.DomainCore.Persistence.CoreDbContext>().UseNpgsql(connectionString).Options))
        {
            await coreDb.Database.MigrateAsync();
        }
        using (var identityDb = new Svac.Identity.Persistence.IdentityDbContext(new DbContextOptionsBuilder<Svac.Identity.Persistence.IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            await identityDb.Database.MigrateAsync();
        }

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddSvacHosting();
        builder.Services.AddDomainCore(connectionString, devSeamsEnabled: true);
        // A non-null smtpOptions so AddIdentityModule wires a real IEmailTransport binding — then swapped
        // below for the gated test double. IEmailSender is left EXACTLY as AddIdentityModule wires it
        // (OutboxEmailSender) — the whole point of this test.
        builder.Services.AddIdentityModule(connectionString, Svac.Identity.Email.SmtpTransportOptions.MailpitDefault());
        _transport = new GatedEmailTransport();
        builder.Services.RemoveAll<IEmailTransport>();
        builder.Services.AddSingleton<IEmailTransport>(_transport);
        builder.Services.AddSingleton(new ClientConfigResponse(ApiVersion: "v0", Locales: SupportedLocales, DefaultLocale: "en"));

        _app = builder.Build();
        _app.UseSvacRequestContext();
        _app.UseRateLimiter();
        _app.MapIdentityEndpoints();
        _app.RequireMutationsPolicyMapped();
        _app.RequireTargetBindingConsistent();

        await _app.StartAsync();

        using var seedScope = _app.Services.CreateScope();
        var seedLoader = seedScope.ServiceProvider.GetRequiredService<ConfigSeedLoader>();
        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var seedCtx = RequestContext.System(systemActor, correlationId: "mail-offpath-test-seed");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Config", "identity.config.json");
        await seedLoader.SeedFromFile(manifestPath, seedCtx);

        var addressFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("no server address feature available.");
        Client = new HttpClient { BaseAddress = new Uri(addressFeature.Addresses.First()) };
    }

    public async Task DisposeAsync()
    {
        _transport.Release(); // never leave the dispatcher's background loop permanently blocked on shutdown.
        Client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
        }
        await _postgres.DisposeAsync();
    }

    /// <summary>Blocks every <see cref="SendAsync"/> call until <see cref="Release"/> is called — the strongest possible proof that a caller did NOT await this call in-band.</summary>
    private sealed class GatedEmailTransport : IEmailTransport
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<EmailMessage> _delivered = new();
        private readonly Lock _lock = new();

        public IReadOnlyList<EmailMessage> Delivered
        {
            get
            {
                lock (_lock)
                {
                    return _delivered.ToList();
                }
            }
        }

        public void Release() => _gate.TrySetResult();

        public async Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default)
        {
            await _gate.Task.WaitAsync(ct);
            lock (_lock)
            {
                _delivered.Add(msg);
            }
            return EmailResult.Sent("gated-test");
        }
    }

    [Fact]
    public async Task PostSignupEmailVerification_ReturnsBeforeTheGatedTransportEverCompletes_ThenDeliversOnceReleased()
    {
        var email = $"offpath-{Guid.NewGuid():N}@example.com";

        var responseTask = Client.PostAsJsonAsync("/v1/signup/email-verification", new { email });
        var raced = await Task.WhenAny(responseTask, Task.Delay(TimeSpan.FromSeconds(3)));

        // THE proof: the HTTP response won the race against a 3s timeout while the transport's gate is
        // STILL CLOSED — if SendAsync were awaited in-band, this would hang until Release() below, and
        // the timeout would win instead.
        Assert.Same(responseTask, raced);

        var response = await responseTask;
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(_transport.Delivered); // nothing dispatched yet — the gate is still closed.

        _transport.Release();

        var delivered = await WaitUntilAsync(() => _transport.Delivered.Any(m => m.To == email && m.TemplateKey == "email.verify_code"), TimeSpan.FromSeconds(5));
        Assert.True(delivered, "expected the outbox dispatcher to drain the queued email once the gate opened.");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
        return condition();
    }
}
