using System.Net;
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
using Svac.Identity.Endpoints;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// OPS-6 (SECURITY_REVIEW_S3.md, LOW→fix): <c>GET /v1/signup/handle-availability</c> had no rate limiter
/// or quota — unbounded handle-existence scraping, and each call is a triple-<c>AnyAsync</c> DB hit (an
/// amplification lever). Now behind the SAME <c>identity-anon</c> per-IP fixed-window policy every anon
/// POST mutation already carries.
///
/// Its own host (not the shared IdentityHttpFixture/MeEndpointsHttpTests collection): this test
/// deliberately EXHAUSTS the 100/min-per-IP window, which would otherwise poison every other HTTP test
/// class sharing the same in-process limiter state.
/// </summary>
public sealed class HandleAvailabilityRateLimitTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private static readonly string[] SupportedLocales = { "en", "es", "pt", "zh-Hans" };

    private Microsoft.AspNetCore.Builder.WebApplication? _app;

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
        builder.Services.AddIdentityModule(connectionString, smtpOptions: null);
        builder.Services.RemoveAll<IEmailSender>();
        builder.Services.AddSingleton<IEmailSender>(new FakeEmailSender());
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
        var seedCtx = RequestContext.System(systemActor, correlationId: "handle-availability-rate-limit-test-seed");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Config", "identity.config.json");
        await seedLoader.SeedFromFile(manifestPath, seedCtx);

        var addressFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("no server address feature available.");
        Client = new HttpClient { BaseAddress = new Uri(addressFeature.Addresses.First()) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
        }
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task GetHandleAvailability_HammeredPastTheAnonymousLimiterWindow_EventuallyGetsLimited()
    {
        // IdentityRateLimiting.AnonymousMutationPolicy: PermitLimit 100 / 1-minute fixed window, per IP.
        // All requests here share one loopback IP -> one partition. RejectionStatusCode is never set on
        // the policy (IdentityRateLimiting.cs), so ASP.NET Core's own default (503 ServiceUnavailable, NOT
        // 429) is what every anonymous-mutation endpoint already renders on a rejection — this route now
        // matches that SAME behavior, not a bespoke one.
        HttpStatusCode? limited = null;
        for (var i = 0; i < 105 && limited is null; i++)
        {
            var response = await Client.GetAsync($"/v1/signup/handle-availability?handle=probe_{i}");
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                limited = response.StatusCode;
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, limited); // before OPS-6: never hit — the route carried no limiter at all.
    }
}
