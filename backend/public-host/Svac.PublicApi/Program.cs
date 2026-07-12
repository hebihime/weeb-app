using Microsoft.AspNetCore.Mvc;
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

// `--emit-export-registry [outputPath]` (SLICE_S3_CONTRACT.md §6b): the export registry is pure
// in-memory data too (no DB, no host) — same emit-then-diff drift-gate shape as
// --emit-openapi/--emit-purge-registry; see build/scripts/emit-export-registry.sh.
if (args.Length > 0 && args[0] == "--emit-export-registry")
{
    return ExportRegistryEmitter.Run(args);
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
// PII-8 (SECURITY_REVIEW_S3.md): a real, stable path (independent of SVAC_DEVSEAMS_DESTROYED_KEYS_PATH
// being explicitly set) so this host's own dev/compose restarts can never resurrect a crypto-shredded
// field key — DevKeyringFieldKeyVault reloads this file at construction. No-op when devSeamsEnabled is
// false (the fail-closed ThrowingFieldKeyVault branch never constructs this type at all).
var devKeyringDestroyedKeysPath = Path.Combine(Path.GetTempPath(), "svac-devseams", "destroyed-field-keys.txt");
builder.Services.AddDomainCore(connectionString, devSeamsEnabled, devKeyringDestroyedKeysPath);
// SLICE_S3_CONTRACT.md Pass 1 (BUILD phase): the identity module's real registration — IdentityDbContext
// (schema `identity`) + its own migration hosted service, the identity policy source + ownership
// resolvers, the session-backed IBearerAuthenticator (overrides AddSvacHosting's anonymous default —
// this call runs AFTER AddSvacHosting above), the real SmtpEmailSender (Mailpit under DevSeams; prod
// with DevSeams off gets no SmtpTransportOptions and fails closed at IEmailSender resolution, L18), and
// the consent ledger writer + its two rebuildable projections.
var smtpOptions = devSeamsEnabled
    ? Svac.Identity.Email.SmtpTransportOptions.MailpitDefault(builder.Configuration["SVAC_SMTP_HOST"] ?? "localhost")
    : null;
// OPS-2 (SECURITY_REVIEW_S3.md): explicit startup refusal — mirrors ProdFieldKeyVaultGuard.Enforce above.
// A non-Development boot with no SMTP configured throws HERE, before app.StartAsync()/traffic, instead of
// booting clean and 500ing at the first signup/login-code/email-change/export request.
Svac.Identity.Email.SmtpConfiguredGuard.Enforce(
    environmentName: builder.Environment.EnvironmentName,
    devSeamsEnabled: devSeamsEnabled,
    smtpConfigured: smtpOptions is not null);
builder.Services.AddIdentityModule(connectionString, smtpOptions);
builder.Services.AddOpenApi("v0", OpenApiSetup.Configure);

var localesPath = builder.Configuration["SVAC_I18N_LOCALES_PATH"] ?? ClientConfigLoader.ResolveDefaultLocalesPath(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(ClientConfigLoader.Load(localesPath));

var app = builder.Build();

// OPS-1 (SECURITY_REVIEW_S3.md): resolve the REAL client IP before anything downstream reads
// Connection.RemoteIpAddress — behind Azure Container Apps' ingress that address is always the proxy's
// unless forwarded-headers processing rewrites it first, and IdentityRateLimiting's anonymous per-IP
// limiter partitions on exactly that field. Must run before UseSvacRequestContext/UseRateLimiter.
// SVAC_ACA_INGRESS_CIDRS (comma-separated CIDR list) MUST be set to the ACA ingress's real subnet before
// any non-Development deploy — see ForwardedHeadersConfiguration's own doc comment. Unset today (the
// exact ACA ingress CIDR is not yet known); Build() fails closed (empty trust set, XFF inert) rather than
// trusting every peer, so this is inert-not-spoofable until that config lands.
app.UseForwardedHeaders(Svac.Identity.Endpoints.ForwardedHeadersConfiguration.Build(builder.Configuration["SVAC_ACA_INGRESS_CIDRS"]));
// RequestContextMiddleware FIRST — every module/endpoint downstream resolves IRequestContextAccessor,
// never HttpContext directly (SLICE_S1_CONTRACT.md §1b, arch-tested).
app.UseSvacRequestContext();
// SLICE_S3_CONTRACT.md §1c: host-level per-IP rate limiting for identity's anonymous mutation endpoints
// (IdentityRateLimiting, registered by AddIdentityModule above) — transport abuse control, NOT 10A.
app.UseRateLimiter();

app.MapOpenApi("/openapi/{documentName}.json");
Endpoints.MapAll(app);

// SLICE_S3_CONTRACT.md §3 (Phase P): a DevSeams-gated diagnostic trigger to run the deletion sweep on
// demand — the S1 canary pattern, NEVER in the shipped contract (excluded from Endpoints.MapAll, which
// the --emit-openapi emitter also calls, so this route can never leak into contracts/openapi.v0.json;
// gated a SECOND time by devSeamsEnabled itself, so a prod boot never maps it even if that ever changed).
if (devSeamsEnabled)
{
    app.MapPost("/internal/devseams/deletion-sweep", async (
            [FromServices] Svac.Identity.Deletion.DeletionPhysicalPurgeWorker worker,
            CancellationToken ct) =>
        {
            var processed = await worker.RunDueSweepAsync(ct);
            return Results.Ok(new { processed });
        })
        .WithName("DevSeamsDeletionSweep")
        .ExcludeFromDescription()
        // Still a real HTTP-mapped mutation endpoint — StartupPolicyCoverage.RequireMutationsPolicyMapped
        // below refuses to boot without this (identical law to every other mutation route; see
        // IdentityPolicyTableSource's matching "DevSeams-only diagnostic triggers" rows for why Anonymous).
        .RequirePolicyAction("identity.devseams.deletion_sweep_trigger");

    // SLICE_S3_CONTRACT.md §4/§10.3: a SECOND DevSeams-gated diagnostic, same shape/gating as the sweep
    // trigger above — sets identity.deletion.grace_days (bounds-legal [0,30]) on demand. identity.e2e.mjs
    // needs BOTH a real (nonzero) grace window — so its cancel drill has a genuine future effective_at to
    // cancel against — AND grace_days=0 for the LATER request it means to have the sweep physically
    // purge; those two needs cannot share one boot-time value. ConfigSeedLoader's seed pass is
    // deliberately idempotent (§4: never clobbers an already-seeded row) and there is no S5 ops desk yet,
    // so this is the only lever that exists before the desk ships. Routes through the real
    // IConfigRegistry.SetValue (ConfigBounds' [0,30] check still runs — an out-of-range request 400s
    // exactly like a real desk edit would), never a second config-loading mechanism.
    app.MapPost("/internal/devseams/deletion-grace-days", async (
            [FromBody] DevSeamsGraceDaysRequest body,
            [FromServices] Svac.DomainCore.Contracts.Config.IConfigRegistry config,
            CancellationToken ct) =>
        {
            // identity.deletion.grace_days' [0,30] bound (SLICE_S3_CONTRACT.md §4) is now ALSO enforced
            // generically inside ConfigRegistry.SetValue itself (OPS-3, SECURITY_REVIEW_S3.md: BoundsJson,
            // seeded from the manifest's own "bounds" field, is checked for every key on the real write
            // path — no longer a hardcoded 3-key switch). This local check stays as a friendlier 400 with
            // a specific message; SetValue's own ArgumentException is the structural backstop either way.
            if (body.Days < 0 || body.Days > 30)
            {
                return Results.BadRequest(new { error = $"identity.deletion.grace_days bounds are [0,30] (SLICE_S3_CONTRACT.md §4); got {body.Days}." });
            }

            var systemActor = new Svac.DomainCore.Contracts.Ids.ActorRef(
                Svac.DomainCore.Contracts.Ids.OpaqueId.New(Svac.DomainCore.Contracts.Ids.IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared),
                Svac.DomainCore.Contracts.Ids.ActorKind.System);
            var ctx = Svac.DomainCore.Contracts.RequestContext.System(systemActor, correlationId: "devseams-grace-days-override");
            await config.SetValue(
                Svac.Identity.Config.IdentityConfigKeys.DeletionGraceDays,
                body.Days,
                "DevSeams E2E override (SLICE_S3_CONTRACT.md §10.3)",
                systemActor,
                ctx,
                ct);
            return Results.Ok(new { graceDays = body.Days });
        })
        .WithName("DevSeamsDeletionGraceDays")
        .ExcludeFromDescription()
        .RequirePolicyAction("identity.devseams.grace_days_override");
}

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
        var systemActor = new Svac.DomainCore.Contracts.Ids.ActorRef(
            Svac.DomainCore.Contracts.Ids.OpaqueId.New(Svac.DomainCore.Contracts.Ids.IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared),
            Svac.DomainCore.Contracts.Ids.ActorKind.System);
        var ctx = Svac.DomainCore.Contracts.RequestContext.System(systemActor, correlationId: "startup-seed");

        // Every module's additive config manifest (SLICE_S1_CONTRACT.md §4 union-merge) — domain-core's
        // own substrate keys, then SLICE_S3_CONTRACT.md's identity.*/quota.identity.*.cap keys. Each is
        // independently idempotent (ConfigSeedLoader re-running on an already-seeded key is a no-op), so
        // seeding order between manifests never matters.
        var manifestPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Config", "manifests", "domain-core.config.json"),
            Path.Combine(AppContext.BaseDirectory, "Config", "identity.config.json"),
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
        // Best-effort at S1: no request path yet reads these keys (zero live quota keys, §5), so a
        // seeding failure must not crash the host — but it IS logged loudly, never silent.
        Log.ConfigSeedingFailed(webApp.Logger, ex);
    }
}

/// <summary>Request body for <c>POST /internal/devseams/deletion-grace-days</c> (DevSeams-gated, never in the shipped contract).</summary>
internal sealed record DevSeamsGraceDaysRequest(int Days);
