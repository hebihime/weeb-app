using Svac.AdminHost;
using Svac.AdminHost.Auth;
using Svac.AdminHost.Desks;
using Svac.AdminHost.Domain.Auth;
using Svac.AdminHost.Domain.Bootstrap;
using Svac.AdminHost.Domain.DependencyInjection;
using Svac.AdminHost.Staff;
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

// --- Configuration (SLICE_S5_CONTRACT.md §1b/§8: Pass A — real staff auth. AdminActionExecutor/tile
// sources remain later passes' deliverables) ---
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

// SLICE_S5_CONTRACT.md §1b: "Authority/client-id from config; client credential from Key Vault via the
// S0-reserved path — no staff-auth secret in the repo (2A)." The S0-reserved names this slice mints
// (no admin-auth secret name existed before S5): SVAC_ENTRA_AUTHORITY / SVAC_ENTRA_CLIENT_ID /
// SVAC_ENTRA_CLIENT_SECRET — in prod the LAST one is routed from Key Vault into this exact config key by
// the deployment (Azure App Service Key Vault references / Bicep, per the module README checklist),
// never hardcoded here, exactly like SVAC_KEYVAULT_ENDPOINT above.
var entraConfig = new StaffAuthEntraConfig(
    Authority: builder.Configuration["SVAC_ENTRA_AUTHORITY"],
    ClientId: builder.Configuration["SVAC_ENTRA_CLIENT_ID"],
    ClientSecret: builder.Configuration["SVAC_ENTRA_CLIENT_SECRET"]);

// ProdStaffAuthGuard: any non-Development boot without complete Entra config throws at startup (§1b) —
// the ProdFieldKeyVaultGuard family, called directly here (never from inside a DI factory lambda, the S2
// ValidateOnBuild lesson) exactly like ProdFieldKeyVaultGuard.Enforce above.
ProdStaffAuthGuard.Enforce(
    environmentName: builder.Environment.EnvironmentName,
    entraAuthorityConfigured: entraConfig.AuthorityConfigured,
    entraClientIdConfigured: entraConfig.ClientIdConfigured,
    entraClientSecretConfigured: entraConfig.ClientSecretConfigured);

builder.Services.AddSvacHosting();
// Migrations apply via MigrationHostedService (registered inside AddDomainCore) THEN
// AdminMigrationHostedService (registered inside AddAdminHostModule, below) — both under their own
// Postgres advisory lock (mirrors Svac.PublicApi + Svac.Identity's ordering exactly); this host
// registers zero stream consumers (§0's "zero projections" ruling, §8 seam 20), so there is nothing else
// that must wait behind either.
var devKeyringDestroyedKeysPath = Path.Combine(Path.GetTempPath(), "svac-devseams", "destroyed-field-keys.txt");
builder.Services.AddDomainCore(connectionString, devSeamsEnabled, devKeyringDestroyedKeysPath);
builder.Services.AddAdminHostModule(connectionString);
// Pass A's own seam: staff sign-in transports (dev + prod), the grant-table role resolver, DataProtection
// persistence, StaffContextProvider — called AFTER AddDomainCore/AddAdminHostModule, exactly like
// AddIdentityModule's own bearer-authenticator override on Svac.PublicApi.
builder.Services.AddStaffAuth(connectionString, devSeamsEnabled, entraConfig);

// SLICE_S5_CONTRACT.md §8 seam 1: the desk-registration seam's first live registrant. Every later desk
// slice adds itself with one more line here — zero edits to AdminLayout/AdminNav.
builder.Services.AddSingleton<IDeskModule, DashboardDeskModule>();
// SLICE_S5_CONTRACT.md §0/§8 seam 1/3 (Pass B): the Staff & Roles desk — the second live registrant,
// proving the seam composes (zero edits to DashboardDeskModule or AdminLayout to add it).
builder.Services.AddSingleton<IDeskModule, Svac.AdminHost.Desks.StaffRolesDeskModule>();

builder.Services.AddRazorComponents();
builder.Services.AddAntiforgery();

var app = builder.Build();

// UseAuthentication BEFORE UseSvacRequestContext: RequestContextMiddleware's IBearerAuthenticator
// (StaffCookieBearerAuthenticator) reads httpContext.User, which the authentication middleware is what
// populates from the cookie — ordering here is load-bearing, not stylistic.
app.UseAuthentication();
app.UseSvacRequestContext();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new HealthStatus("healthy", DateTimeOffset.UtcNow)))
    .WithName("GetHealth")
    .Produces<HealthStatus>(StatusCodes.Status200OK);

// The real staff sign-in HTTP surface (§1a/§1b): DevSeams fixture issuance (dev), the Entra OIDC
// challenge redirect (prod/staging, only mapped when Entra is actually configured — absence law), and
// sign-out. All three carry "admin.host.transport" — never a second pre-auth-reachability row.
app.MapStaffAuthEndpoints(devSeamsEnabled, entraConfig.IsComplete);

// SLICE_S5_CONTRACT.md §0/§1c/§8 seam 3 (Pass B): the Staff & Roles desk's real HTTP form-post surface —
// provision/deactivate/reactivate/role_grant/role_revoke, every one routed through
// IAdminActionExecutor. Carries "admin.host.transport" exactly like MapStaffAuthEndpoints above (the
// specific admin.staff.* Authorize + audit happens INSIDE the executor, never at this transport layer).
app.MapStaffRolesEndpoints();

// SLICE_S5_CONTRACT.md §3/§8 seam 1: "admin.host.transport" maps EVERY Razor Component endpoint
// (the sign-in page + every desk page) honestly for RequireMutationsPolicyMapped — attached UNIFORMLY
// here, never per-page. Every ACTUAL staff verb is gated inside the (Pass B) executor +
// RequireAdminActionsCovered, never by this transport-level row.
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
await BootstrapFirstSuperAdmin(app);
await app.WaitForShutdownAsync();
return 0;

async Task BootstrapFirstSuperAdmin(WebApplication webApp)
{
    // SLICE_S5_CONTRACT.md §1b: "if admin.staff_accounts is empty AND SVAC_ADMIN_BOOTSTRAP_SUBJECT (+
    // email/display-name/region) is set, provision that subject + SuperAdmin grant ... one-shot." An env
    // var, never a 9A entry — bootstrap precedes the desk that edits 9A. Unset + empty in prod ⇒ nobody
    // signs in — fail-closed, safe (never a boot-time throw: an unset bootstrap subject is a legitimate,
    // expected state for every boot after the first).
    var subject = webApp.Configuration["SVAC_ADMIN_BOOTSTRAP_SUBJECT"];
    if (string.IsNullOrWhiteSpace(subject))
    {
        return;
    }

    var email = webApp.Configuration["SVAC_ADMIN_BOOTSTRAP_EMAIL"] ?? $"{subject}@svac.internal";
    var displayName = webApp.Configuration["SVAC_ADMIN_BOOTSTRAP_DISPLAY_NAME"] ?? "Founder";
    var region = webApp.Configuration["SVAC_ADMIN_BOOTSTRAP_REGION"] ?? "US";

    using var scope = webApp.Services.CreateScope();
    try
    {
        var bootstrapper = scope.ServiceProvider.GetRequiredService<StaffBootstrapper>();
        var provisioned = await bootstrapper.BootstrapIfEmpty(subject, email, displayName, region);
        if (provisioned)
        {
            Log.BootstrapProvisioned(webApp.Logger, subject);
        }
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        // Never crash the host over bootstrap — a misconfigured bootstrap subject is recoverable by
        // re-setting the env var and restarting; a crash loop over it is strictly worse (fails BOTH the
        // bootstrap AND every other staff sign-in).
        Log.BootstrapFailed(webApp.Logger, ex);
    }
}

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
