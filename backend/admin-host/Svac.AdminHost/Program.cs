using Svac.AdminHost;
using Svac.AdminHost.Domain.DependencyInjection;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.DevSeams;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Hosting;

// `--emit-purge-registry [outputPath]` (SLICE_S5_CONTRACT.md §6): admin's OWN additive slice of the 13A
// registry, pure in-memory data (no DB, no host) — mirrors Svac.PublicApi's identical CLI mode. This
// process never references Svac.PublicApi/Svac.Identity (§0 law c, the admin trust-boundary rule), so
// the ONE committed backend/domain-core/purge-registry.json is assembled by build/scripts/
// emit-purge-registry.sh MERGING this fragment with Svac.PublicApi's own (core+identity) fragment.
if (args.Length > 0 && args[0] == "--emit-purge-registry")
{
    return PurgeRegistryEmitter.Run(args);
}

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (SLICE_S5_CONTRACT.md §0: SCAFFOLD — a stub sign-in page + one dashboard stub
// route; real staff auth/AdminActionExecutor/tile sources are Phase 2) ---
var connectionString = builder.Configuration.GetConnectionString("Core")
    ?? builder.Configuration["SVAC_CORE_CONNECTION_STRING"]
    ?? "Host=localhost;Port=5433;Database=svac;Username=svac;Password=svac_dev_only";

var devSeamsEnabled = DevSeamsFlag.IsEnabledFromProcessEnvironment();

// L18 fail-closed (mirrors Svac.PublicApi/Program.cs verbatim — every host mounting AddDomainCore
// enforces this identically, admin host included): a non-Development boot with DevSeams on, or with no
// real Key Vault backend and DevSeams off, throws before a single request is served.
ProdFieldKeyVaultGuard.Enforce(
    environmentName: builder.Environment.EnvironmentName,
    devSeamsEnabled: devSeamsEnabled,
    keyVaultEndpointConfigured: !string.IsNullOrWhiteSpace(builder.Configuration["SVAC_KEYVAULT_ENDPOINT"]));

builder.Services.AddSvacHosting();
// Migrations apply via MigrationHostedService (registered inside AddDomainCore) THEN
// AdminMigrationHostedService (registered inside AddAdminHostModule, below) — both under their own
// Postgres advisory lock (mirrors Svac.PublicApi + Svac.Identity's ordering exactly); this host
// registers zero stream consumers (§0's "zero projections" ruling, §8 seam 20), so there is nothing else
// that must wait behind either.
var devKeyringDestroyedKeysPath = Path.Combine(Path.GetTempPath(), "svac-devseams", "destroyed-field-keys.txt");
builder.Services.AddDomainCore(connectionString, devSeamsEnabled, devKeyringDestroyedKeysPath);
builder.Services.AddAdminHostModule(connectionString);

builder.Services.AddRazorComponents();
builder.Services.AddAntiforgery();

var app = builder.Build();

// RequestContextMiddleware FIRST — every module/endpoint downstream resolves IRequestContextAccessor,
// never HttpContext directly (SLICE_S1_CONTRACT.md §1b, arch-tested). This host has no staff-auth
// transport wired yet (Phase 2 — Entra/DevSeams), so every request resolves the anonymous actor via
// AddSvacHosting's AnonymousBearerAuthenticator default, exactly like Svac.PublicApi at S1.
app.UseSvacRequestContext();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new HealthStatus("healthy", DateTimeOffset.UtcNow)))
    .WithName("GetHealth")
    .Produces<HealthStatus>(StatusCodes.Status200OK);

// SLICE_S5_CONTRACT.md §3/§8 seam 1: "admin.host.transport" maps EVERY Razor Component endpoint
// (pre-auth sign-in page + the dashboard stub route today; every future desk's pages tomorrow) honestly
// for RequireMutationsPolicyMapped — attached UNIFORMLY here, never per-page, exactly as the policy
// row's own note says ("maps Blazor infrastructure endpoints ... honestly"). Every ACTUAL staff verb is
// gated inside the (Phase 2) executor + RequireAdminActionsCovered, never by this transport-level row.
app.MapRazorComponents<Svac.AdminHost.Components.App>()
    .WithMetadata(new PolicyActionAttribute("admin.host.transport"))
    .AddEndpointFilter(new PolicyEnforcementFilter("admin.host.transport"));

// 4A fail-closed boot refusal (S1 boot-refusal law, mirrored at the layer this host actually mutates
// through): throws if any non-GET endpoint lacks a mapped policy row, or if AdminActionKeys.All names an
// action the boot-time-unioned PolicyTable has never heard of (policy-source duplicate-key union +
// boot refusal itself lives inside AddDomainCore/AddAdminHostModule's PolicyTable construction, already
// proven at S1/S3 — this call re-proves it on THIS host's own composition).
app.RequireMutationsPolicyMapped();
app.RequireTargetBindingConsistent();
app.RequireAdminActionsCovered();

// StartAsync (not Run()) so every registered IHostedService — MigrationHostedService, then
// AdminMigrationHostedService — completes before this process does its own startup work AND before we
// consider the host "up" for compose health purposes (mirrors Svac.PublicApi/Program.cs exactly).
await app.StartAsync();
await SeedConfigOnStartup(app);
await app.WaitForShutdownAsync();
return 0;

async Task SeedConfigOnStartup(WebApplication webApp)
{
    // Additive, idempotent (ConfigSeedLoader re-running on an already-seeded key is a no-op — safe
    // regardless of boot order against Svac.PublicApi, which seeds the SAME domain-core.config.json
    // manifest against the SAME schema-`core` table). SLICE_S5_CONTRACT.md §5: this scaffold's own two
    // manifests (admin-host.config.json, v0-batch.config.json) are EMPTY-VALID skeletons — zero rows to
    // seed today; Phase 2 seeds the real batch as data through this SAME loader, never a second
    // config-loading mechanism.
    using var scope = webApp.Services.CreateScope();
    try
    {
        var loader = scope.ServiceProvider.GetRequiredService<ConfigSeedLoader>();
        var systemActor = new Svac.DomainCore.Contracts.Ids.ActorRef(
            Svac.DomainCore.Contracts.Ids.OpaqueId.New(Svac.DomainCore.Contracts.Ids.IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared),
            Svac.DomainCore.Contracts.Ids.ActorKind.System);
        var ctx = Svac.DomainCore.Contracts.RequestContext.System(systemActor, correlationId: "admin-host-startup-seed");

        var manifestPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Config", "manifests", "domain-core.config.json"),
            Path.Combine(AppContext.BaseDirectory, "config", "admin-host.config.json"),
            Path.Combine(AppContext.BaseDirectory, "config", "v0-batch.config.json"),
        };

        var totalSeeded = 0;
        foreach (var manifestPath in manifestPaths)
        {
            if (!File.Exists(manifestPath))
            {
                Log.ConfigManifestNotFound(webApp.Logger, manifestPath);
                continue;
            }
            totalSeeded += await loader.SeedFromFile(manifestPath, ctx);
        }
        Log.ConfigSeedingComplete(webApp.Logger, totalSeeded);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        // Mirrors Svac.PublicApi/Program.cs: best-effort at scaffold (zero rows to seed today, so a
        // seeding failure has nothing to make visibly wrong yet) — logged loudly, never silent, never
        // crashes the host.
        Log.ConfigSeedingFailed(webApp.Logger, ex);
    }
}
