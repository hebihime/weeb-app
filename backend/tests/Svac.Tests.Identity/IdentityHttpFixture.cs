using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Svac.Identity.Auth;
using Svac.Identity.DependencyInjection;
using Svac.Identity.Endpoints;
using Svac.Identity.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Boots a REAL, in-process Kestrel host wired EXACTLY like Svac.PublicApi's real Program.cs
/// (AddDomainCore + AddIdentityModule + MapIdentityEndpoints + UseSvacRequestContext + the two boot-
/// refusal checks) against its OWN Testcontainer Postgres — so the `/v1/me/*` HTTP-level proofs (IDOR,
/// category-8 absence, the real 4A chokepoint) exercise the actual policy engine + ownership resolvers +
/// SessionBearerAuthenticator end to end, not a DB-service-layer stand-in. Kept SEPARATE from
/// <see cref="IdentityDbFixture"/>'s shared collection so each gets its own container (the HTTP suite
/// mints real sessions via direct DB inserts below, which would collide awkwardly with sharing one
/// container's config-seed timing) — still one container for the whole HTTP-level suite, one boot.
/// </summary>
public sealed class IdentityHttpFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private static readonly string[] SupportedLocales = { "en", "es", "pt", "zh-Hans" };

    private Microsoft.AspNetCore.Builder.WebApplication? _app;

    public FakeEmailSender Emails { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        using (var coreDb = new Svac.DomainCore.Persistence.CoreDbContext(new DbContextOptionsBuilder<Svac.DomainCore.Persistence.CoreDbContext>().UseNpgsql(connectionString).Options))
        {
            await coreDb.Database.MigrateAsync();
        }
        using (var identityDb = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            await identityDb.Database.MigrateAsync();
        }

        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddSvacHosting();
        builder.Services.AddDomainCore(connectionString, devSeamsEnabled: true);
        builder.Services.AddIdentityModule(connectionString, smtpOptions: null);
        builder.Services.RemoveAll<IEmailSender>();
        builder.Services.AddSingleton<IEmailSender>(Emails);
        builder.Services.AddSingleton(new ClientConfigResponse(ApiVersion: "v0", Locales: SupportedLocales, DefaultLocale: "en"));

        _app = builder.Build();
        _app.UseSvacRequestContext();
        _app.MapIdentityEndpoints();
        _app.RequireMutationsPolicyMapped();
        _app.RequireTargetBindingConsistent();

        await _app.StartAsync();

        using var seedScope = _app.Services.CreateScope();
        var seedLoader = seedScope.ServiceProvider.GetRequiredService<ConfigSeedLoader>();
        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var seedCtx = RequestContext.System(systemActor, correlationId: "http-test-seed");
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

    public IServiceScope NewScope() => _app!.Services.CreateScope();

    /// <summary>Mints a real, live account + session directly against the DB (never through the HTTP signup journey — that is SignupCompleteForcedRaceTests/the live E2E's job) so HTTP-level tests can present a real bearer token. `birthdate` is field-encrypted through the REAL <see cref="Svac.DomainCore.Contracts.FieldEncryption.IFieldEncryptor"/> — GET /v1/me's handler Unprotect()s this exact column.</summary>
    public async Task<(string AccountId, string AccessToken)> SeedActiveAccountWithSession(string? accountState = null, DateOnly? birthdate = null)
    {
        using var scope = NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var fieldEncryptor = scope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.FieldEncryption.IFieldEncryptor>();

        var now = DateTimeOffset.UtcNow;
        var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();
        var birthdateValue = birthdate ?? new DateOnly(2000, 1, 1);
        var birthdateEnc = await fieldEncryptor.Protect(
            Svac.DomainCore.Contracts.FieldEncryption.FieldEncryptionPurpose.Birthdate,
            new Svac.DomainCore.Contracts.FieldEncryption.SubjectScope(accountId),
            birthdateValue.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            CancellationToken.None);
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = $"httpuser_{Guid.NewGuid():N}"[..20],
            Email = $"http-{Guid.NewGuid():N}@example.com",
            EmailVerifiedAt = now,
            BirthdateEnc = birthdateEnc,
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "shonen",
            Locale = "en",
            AccountState = accountState ?? "active",
            StateChangedAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "legitimate_interest",
        });

        var accessToken = SessionTokens.NewAccessToken();
        db.Sessions.Add(new SessionEntity
        {
            SessionId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString(),
            AccountId = accountId,
            AccessTokenHash = SessionTokens.HashAccessToken(accessToken),
            RefreshFamilyId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString(),
            CreatedAt = now,
            LastSeenAt = now,
            AccessExpiresAt = now.AddHours(1),
            Region = "US",
            LawfulBasis = "legitimate_interest",
        });

        await db.SaveChangesAsync();
        return (accountId, accessToken);
    }

    public static HttpRequestMessage Authed(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityHttpCollectionDefinition : ICollectionFixture<IdentityHttpFixture>
{
    public const string Name = "IdentityHttp";
}
