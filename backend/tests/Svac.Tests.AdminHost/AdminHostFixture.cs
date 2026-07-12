using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost;
using Svac.AdminHost.Domain.DependencyInjection;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// Boots a REAL, in-process Kestrel host wired EXACTLY like Svac.AdminHost's own Program.cs
/// (AddSvacHosting + AddDomainCore + AddAdminHostModule + MapRazorComponents + the THREE boot-refusal
/// checks: RequireMutationsPolicyMapped, RequireTargetBindingConsistent, RequireAdminActionsCovered)
/// against its OWN Testcontainer Postgres — mirrors Svac.Tests.Identity.IdentityHttpFixture exactly, so
/// the boot-refusal proofs exercise the actual chokepoint the real host boots through, never a
/// DB-service-layer stand-in.
/// </summary>
public sealed class AdminHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private Microsoft.AspNetCore.Builder.WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        using (var coreDb = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(connectionString).Options))
        {
            await coreDb.Database.MigrateAsync();
        }
        using (var adminDb = new AdminDbContext(new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(connectionString).Options))
        {
            await adminDb.Database.MigrateAsync();
        }

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddSvacHosting();
        builder.Services.AddDomainCore(connectionString, devSeamsEnabled: true);
        builder.Services.AddAdminHostModule(connectionString);
        builder.Services.AddRazorComponents();
        builder.Services.AddAntiforgery();

        _app = builder.Build();
        _app.UseSvacRequestContext();
        _app.UseAntiforgery();
        _app.MapGet("/health", () => Microsoft.AspNetCore.Http.Results.Ok(new Svac.DomainCore.Contracts.Api.HealthStatus("healthy", DateTimeOffset.UtcNow)));
        _app.MapRazorComponents<Svac.AdminHost.Components.App>()
            .WithMetadata(new Svac.DomainCore.Contracts.Policy.PolicyActionAttribute("admin.host.transport"))
            .AddEndpointFilter(new PolicyEnforcementFilter("admin.host.transport"));

        _app.RequireMutationsPolicyMapped();
        _app.RequireTargetBindingConsistent();
        _app.RequireAdminActionsCovered();

        await _app.StartAsync();

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

    public IServiceScope NewScope() => _app!.Services.CreateScope();
}

[CollectionDefinition("AdminHostHttp")]
public sealed class AdminHostHttpTestGroup : ICollectionFixture<AdminHostFixture>;
