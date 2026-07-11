using Svac.DomainCore.Config;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.DevSeams;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Hosting;
using Svac.Identity.DependencyInjection;
using Svac.PublicApi;

// `--emit-openapi [outputPath]`: contract emission mode (SLICE_S1_CONTRACT.md §1c). Boots a minimal,
// DB-free instance of this host on an ephemeral port, fetches the generated OpenAPI document over real
// HTTP, writes it to disk, exits. Kept as an early-exit branch so the real host below never pays for
// this path and vice versa — see build/scripts/emit-openapi-contract.sh for the CI/gate wrapper.
if (args.Length > 0 && args[0] == "--emit-openapi")
{
    return await OpenApiContractEmitter.Run(args);
}

// `--emit-purge-registry [outputPath]` (SLICE_S1_CONTRACT.md §6): the 13A registry is pure in-memory
// data (no DB, no host) — emitted so "the CI gate diffs EF surface vs registrations" has a committed
// artifact to diff against, same drift-gate shape as --emit-openapi.
if (args.Length > 0 && args[0] == "--emit-purge-registry")
{
    return PurgeRegistryEmitter.Run(args);
}

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (SLICE_S1_CONTRACT.md §0: zero business logic; health, one bootstrap GET, the 4A
// middleware, OpenAPI emit) ---
var connectionString = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration["SVAC_CORE_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5433;Database=svac;Username=svac;Password=svac_dev_only";

var devSeamsEnabled = DevSeamsFlag.IsEnabledFromProcessEnvironment();

// L18 fail-closed (§1b): a non-Development boot with DevSeams on, or with no real Key Vault backend and
// DevSeams off, throws before a single request is served. Trust-F1 (SECURITY_REVIEW_S1.md): allowlist
// Development by NAME rather than blocklisting IsProduction() — Staging/QA/Preview must fail closed too.
ProdFieldKeyVaultGuard.Enforce(
    environmentName: builder.Environment.EnvironmentName,
    devSeamsEnabled: devSeamsEnabled,
    keyVaultEndpointConfigured: !string.IsNullOrWhiteSpace(builder.Configuration["SVAC_KEYVAULT_ENDPOINT"]));

builder.Services.AddSvacHosting();
// Migrations apply via MigrationHostedService (registered inside AddDomainCore) under the Postgres
// advisory lock; the seeding pass below runs AFTER app.StartAsync() completes, so it always sees an
// already-migrated schema — stream consumers (none exist at S1; the first lands with a feature module)
// must register after this same hosted service for the identical reason.
builder.Services.AddDomainCore(connectionString, devSeamsEnabled);
// SLICE_S3_CONTRACT.md Pass 1 (BUILD phase): the identity module's real registration — IdentityDbContext
// (schema `identity`) + its own migration hosted service, the identity policy source + ownership
// resolvers, the session-backed IBearerAuthenticator (overrides AddSvacHosting's anonymous default —
// this call runs AFTER AddSvacHosting above), the real SmtpEmailSender (Mailpit under DevSeams; prod
// with DevSeams off gets no SmtpTransportOptions and fails closed at IEmailSender resolution, L18), and
// the consent ledger writer + its two rebuildable projections.
var smtpOptions = devSeamsEnabled
    ? Svac.Identity.Email.SmtpTransportOptions.MailpitDefault()
    : null;
builder.Services.AddIdentityModule(connectionString, smtpOptions);
builder.Services.AddOpenApi("v0", OpenApiSetup.Configure);

var localesPath = builder.Configuration["SVAC_I18N_LOCALES_PATH"] ?? ClientConfigLoader.ResolveDefaultLocalesPath(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(ClientConfigLoader.Load(localesPath));

var app = builder.Build();

// RequestContextMiddleware FIRST — every module/endpoint downstream resolves IRequestContextAccessor,
// never HttpContext directly (SLICE_S1_CONTRACT.md §1b, arch-tested).
app.UseSvacRequestContext();

app.MapOpenApi("/openapi/{documentName}.json");
Endpoints.MapAll(app);

// 4A fail-closed boot refusal (§3): throws if any non-GET endpoint lacks a mapped policy row. S1 ships
// zero consumer mutation endpoints, so this call is expected to pass vacuously today and refuse to boot
// the moment a future module maps a policy-less mutation endpoint onto this host. Pure endpoint-metadata
// inspection — no DB dependency, so it runs before StartAsync.
app.RequireMutationsPolicyMapped();
// PHASE_2A_SUBSTRATE.md §1/§3a: fail-closed both directions on target-binding/TargetRule pairing. A
// no-op today (S1/S2 ships zero SelfOnly/OwnedResource rows and zero non-None bindings) — wired in now
// so the check is proven end-to-end on the real host, not just in TestHost-based arch tests.
app.RequireTargetBindingConsistent();

// StartAsync (not Run()) so every registered IHostedService — MigrationHostedService first, applying
// the schema under the advisory lock — completes before this process does its own startup work AND
// before we consider the host "up" for compose health purposes.
await app.StartAsync();
await SeedConfigOnStartup(app);
await app.WaitForShutdownAsync();
return 0;

async Task SeedConfigOnStartup(WebApplication webApp)
{
    // Additive, idempotent (SLICE_S1_CONTRACT.md §4: ConfigSeedLoader re-running on an already-seeded
    // key is a no-op). Schema is guaranteed to exist here: app.StartAsync() above has already run
    // MigrationHostedService.StartAsync to completion.
    using var scope = webApp.Services.CreateScope();
    try
    {
        var loader = scope.ServiceProvider.GetRequiredService<ConfigSeedLoader>();
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "Config", "manifests", "domain-core.config.json");
        if (!File.Exists(manifestPath))
        {
            Log.ConfigManifestNotFound(webApp.Logger, manifestPath);
            return;
        }

        var systemActor = new Svac.DomainCore.Contracts.Ids.ActorRef(
            Svac.DomainCore.Contracts.Ids.OpaqueId.New(Svac.DomainCore.Contracts.Ids.IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared),
            Svac.DomainCore.Contracts.Ids.ActorKind.System);
        var ctx = Svac.DomainCore.Contracts.RequestContext.System(systemActor, correlationId: "startup-seed");
        var seeded = await loader.SeedFromFile(manifestPath, ctx);
        Log.ConfigSeedingComplete(webApp.Logger, seeded);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        // Best-effort at S1: no request path yet reads these keys (zero live quota keys, §5), so a
        // seeding failure must not crash the host — but it IS logged loudly, never silent.
        Log.ConfigSeedingFailed(webApp.Logger, ex);
    }
}
