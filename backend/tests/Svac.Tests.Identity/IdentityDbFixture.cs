using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Email;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.Persistence;
using Svac.Identity.Auth;
using Svac.Identity.Consent;
using Svac.Identity.DependencyInjection;
using Svac.Identity.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>Records every call an endpoint/service made to <see cref="IEmailSender"/> — the DB-level forced-race/enumeration/minor-drill suite asserts on THIS instead of requiring a real Mailpit (that's the live E2E's job, backend/e2e/identity.e2e.mjs).</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentBag<EmailMessage> Sent { get; } = new();

    public Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct = default)
    {
        Sent.Add(msg);
        return Task.FromResult(EmailResult.Sent("fake"));
    }
}

/// <summary>
/// ONE shared Postgres+PostGIS Testcontainer + DI provider for the whole Svac.Tests.Identity DB-bound
/// suite (mirrors Svac.Tests.Architecture's per-test-class PostgreSqlContainer pattern, shared across
/// classes here via <see cref="IdentityDbCollectionDefinition"/> since these tests do not adversarially contend
/// with each other — each test mints its own unique email/handle, so one container serves the whole
/// collection without cross-test interference).
/// </summary>
public sealed class IdentityDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private ServiceProvider _provider = null!;

    public string ConnectionString => _postgres.GetConnectionString();

    public FakeEmailSender Emails { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var coreDb = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);
        await coreDb.Database.MigrateAsync();
        using var identityDb = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(ConnectionString).Options);
        await identityDb.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddDomainCore(ConnectionString, devSeamsEnabled: true);
        services.AddIdentityModule(ConnectionString, smtpOptions: null); // real IEmailSender overridden below — never resolves the fail-closed throw path.
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(Emails);
        _provider = services.BuildServiceProvider();

        using var seedScope = _provider.CreateScope();
        var seedLoader = seedScope.ServiceProvider.GetRequiredService<ConfigSeedLoader>();
        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var seedCtx = RequestContext.System(systemActor, correlationId: "test-seed");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Config", "identity.config.json");
        await seedLoader.SeedFromFile(manifestPath, seedCtx);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public IServiceScope NewScope() => _provider.CreateScope();

    /// <summary>A fresh anonymous RequestContext — every DB-level test builds its own rather than resolving one via HTTP middleware (these tests exercise the SERVICE layer directly, not the endpoint/policy pipeline).</summary>
    public static RequestContext AnonymousContext(string correlationId) => new(
        new ActorRef(OpaqueId.New(IdPrefixes.Anonymous, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Anonymous),
        new RegionCode("US", null),
        RegionSource.EdgeInferred,
        LawfulBasisVariant.ConservativeGlobalV0,
        "en",
        correlationId);
}

[CollectionDefinition(Name)]
public sealed class IdentityDbCollectionDefinition : ICollectionFixture<IdentityDbFixture>
{
    public const string Name = "IdentityDb";
}
