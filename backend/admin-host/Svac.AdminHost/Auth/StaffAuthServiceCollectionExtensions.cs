using System.Globalization;
using Azure.Extensions.AspNetCore.DataProtection.Keys;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Svac.AdminHost.Domain.Auth;
using Svac.AdminHost.Domain.Bootstrap;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;

namespace Svac.AdminHost.Auth;

/// <summary>
/// The staff-auth transport seam (SLICE_S5_CONTRACT.md §1b, §8 seam 4): "one claim contract + one MFA
/// policy + one directory mapping behind dev/prod transports." Both transports funnel into the SAME
/// <see cref="StaffSignInPipeline"/>/<see cref="StaffSignInFlow"/> pair — the pipeline after the
/// transport is IDENTICAL in dev and prod (§1b). Call AFTER <c>AddSvacHosting</c>/<c>AddDomainCore</c>/
/// <c>AddAdminHostModule</c> in Program.cs, exactly like <c>AddIdentityModule</c>'s own bearer-authenticator
/// override.
/// </summary>
public static class StaffAuthServiceCollectionExtensions
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string OidcScheme = "EntraOidc";

    /// <summary>SLICE_S5_CONTRACT.md §1b / backend/e2e/admin-host.e2e.mjs's wire contract: the staff auth
    /// cookie's EXACT name — a shared constant so no second copy of this literal can drift from the
    /// <see cref="CookieAuthenticationOptions.Cookie"/> configuration below (<see
    /// cref="Svac.AdminHost.Components.Layout.AdminLayout"/> reads it to distinguish a REJECTED cookie
    /// from a request that never carried one at all).</summary>
    public const string CookieName = ".Svac.AdminAuth";

    public static IServiceCollection AddStaffAuth(
        this IServiceCollection services,
        string postgresConnectionString,
        bool devSeamsEnabled,
        StaffAuthEntraConfig entraConfig,
        Uri? dataProtectionKeyVaultKeyIdentifier = null)
    {
        services.AddSingleton(new DevSeamsEnabledFlag(devSeamsEnabled));

        // Principal -> actor mapping, fail-closed both hops (§1b/§0 law): the grant-table resolver
        // overrides AddDomainCore's DenyAllStaffRoleResolver default — registered HERE (never inside
        // AddAdminHostModule; DependencyInjectionTests.cs pins that a bare AddAdminHostModule composition
        // must still resolve DenyAllStaffRoleResolver). Roles are read fresh from the grants table on
        // every call, NEVER from Entra claims.
        services.AddScoped<IStaffRoleResolver, GrantTableStaffRoleResolver>();

        // One middleware, three credential systems (PHASE_2A_SUBSTRATE.md §4) — this host's own leg.
        services.AddScoped<IBearerAuthenticator, StaffCookieBearerAuthenticator>();

        services.AddScoped<StaffSignInPipeline>();
        services.AddScoped<IStaffSessionRevalidator, GrantTableStaffSessionRevalidator>();
        services.AddScoped<StaffBootstrapper>();
        services.AddScoped<IStaffContextProvider, StaffContextProvider>();

        // [DevSeamsOnly] arch-tested never-in-prod-DI (§1b) — registered ONLY under devSeamsEnabled,
        // exactly like AddDomainCore's own IPaymentService/IRegionResolver family. See
        // DevSeamsStaffTransportArchTests.cs for the red-fixture proof both directions.
        if (devSeamsEnabled)
        {
            services.AddScoped<DevSeamsStaffTransport>();
        }

        // PERSIST DataProtection keys to the existing core.data_protection_keys table (Pass A
        // deliverable: fix the scaffold's "not persisted" warning) so cookies/antiforgery survive
        // restart + multiple instances. A hand-rolled IXmlRepository, never the EF-Core-package's own
        // entity (CoreDbXmlRepository's own doc comment explains why) — zero new package dependency.
        //
        // SECURITY_REVIEW_S5.md S5-04: that XML payload carries the RAW signing/encryption key material
        // for every staff cookie + antiforgery token — persisted to core.data_protection_keys in
        // PLAINTEXT before this fix, readable from any DB access (a backup, a replica, a compromised
        // read-only credential) with zero further exploitation needed to forge a founder cookie. The
        // plaintext XmlRepository stays wired ONLY behind DevSeams (guaranteed Development-only —
        // ProdFieldKeyVaultGuard.Enforce already throws at boot if devSeamsEnabled is true anywhere else,
        // called earlier in Program.cs than this method ever runs); every other boot chains
        // .ProtectKeysWithAzureKeyVault so the stored XML is encrypted-at-rest under a real Key Vault RSA
        // key — a DB dump alone no longer yields a usable key. Fail-closed, not fail-open: a prod/staging
        // boot with no Key Vault key identifier configured throws HERE rather than silently falling back
        // to plaintext (mirrors ProdFieldKeyVaultGuard/ProdStaffAuthGuard's own allowlist-Development
        // shape) — defensive, since ProdFieldKeyVaultGuard.Enforce (called earlier in Program.cs) already
        // refuses boot in this exact situation via SVAC_KEYVAULT_ENDPOINT.
        var dataProtectionBuilder = services.AddDataProtection()
            .SetApplicationName("svac-admin-host")
            .AddKeyManagementOptions(o => o.XmlRepository = new CoreDbXmlRepository(postgresConnectionString));

        if (!devSeamsEnabled)
        {
            if (dataProtectionKeyVaultKeyIdentifier is null)
            {
                throw new InvalidOperationException(
                    "AddStaffAuth: DevSeams is disabled (a non-Development boot) but no Azure Key Vault " +
                    "key identifier was supplied for DataProtection key-ring encryption (SECURITY_REVIEW_S5.md " +
                    "S5-04) — the plaintext core.data_protection_keys path is DevSeams-only. Configure " +
                    "SVAC_KEYVAULT_ENDPOINT before deploying to any environment other than Development.");
            }

            // Azure.Identity pinned to 1.21.0 (Directory.Packages.props' own comment): below that
            // version, Azure.Core >= 1.53's own DefaultAzureCredential export collides with Azure.
            // Identity's now-duplicate copy (CS0433) — 1.21.0 is the version that drops Identity's copy
            // in favor of Core's, so this plain, unqualified reference resolves to exactly one type.
            dataProtectionBuilder.ProtectKeysWithAzureKeyVault(dataProtectionKeyVaultKeyIdentifier, new DefaultAzureCredential());
        }

        var authBuilder = services.AddAuthentication(CookieScheme);

        if (entraConfig.IsComplete)
        {
            // Prod/Staging: OIDC to Entra ID via Microsoft.Identity.Web (§1b: "Layer-1 standard; no
            // hand-rolled OIDC"). ProdStaffAuthGuard.Enforce (called explicitly in Program.cs, mirroring
            // ProdFieldKeyVaultGuard) already refused to boot in any non-Development environment reaching
            // this branch incomplete — by the time this runs, entraConfig.IsComplete is trustworthy.
            authBuilder.AddMicrosoftIdentityWebApp(
                configureMicrosoftIdentityOptions: options =>
                {
                    options.Instance = entraConfig.Authority!;
                    options.ClientId = entraConfig.ClientId!;
                    options.ClientSecret = entraConfig.ClientSecret!;
                    options.CallbackPath = "/staffauth/signin-oidc";
                    options.SignedOutCallbackPath = "/staffauth/signout-callback-oidc";
                },
                configureCookieAuthenticationOptions: null,
                openIdConnectScheme: OidcScheme,
                cookieScheme: CookieScheme);

            services.Configure<OpenIdConnectOptions>(OidcScheme, options =>
            {
                var previousTokenValidated = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    await previousTokenValidated(context);

                    var principal = context.Principal;
                    var externalSubject = principal?.FindFirst("oid")?.Value
                        ?? principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrWhiteSpace(externalSubject))
                    {
                        context.Fail("Entra token carried no subject claim.");
                        return;
                    }

                    var claims = new StaffExternalClaims(
                        ExternalSubject: externalSubject,
                        HasMfaClaim: EntraClaimTypes.HasMfaClaim(principal!.Claims, entraConfig.AcrValues),
                        Email: principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? externalSubject,
                        DisplayName: principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? externalSubject);

                    var services = context.HttpContext.RequestServices;
                    var pipeline = services.GetRequiredService<StaffSignInPipeline>();
                    var contextProvider = services.GetRequiredService<IStaffContextProvider>();
                    var adminDb = services.GetRequiredService<AdminDbContext>();

                    var result = await pipeline.SignIn(claims, contextProvider.ForStaffOperation(), context.HttpContext.RequestAborted);
                    if (result is not StaffSignInResult.Allowed allowed)
                    {
                        // §1b: every refusal is a neutral-register refusal, already audited by the
                        // pipeline itself — context.Fail closes the OIDC handshake without ever minting a
                        // cookie for this subject.
                        context.Fail($"staff sign-in refused: {result.GetType().Name}");
                        return;
                    }

                    context.Principal = await StaffSignInFlow.BuildCookiePrincipal(allowed, adminDb, CookieScheme, context.HttpContext.RequestAborted);
                };
            });
        }
        else
        {
            // Dev (or any environment with no Entra config — ProdStaffAuthGuard already refused to boot
            // if that environment is not Development): cookie scheme only, DevSeams issues the ticket.
            authBuilder.AddCookie(CookieScheme);
        }

        // Every cookie's real behavior — name, static options, revalidation — layered on TOP of
        // whichever branch above registered the scheme, so it applies identically either way.
        services.AddOptions<CookieAuthenticationOptions>(CookieScheme).Configure(options =>
        {
            // SLICE_S5_CONTRACT.md §1b / backend/e2e/admin-host.e2e.mjs's wire contract: the staff auth
            // cookie is named EXACTLY ".Svac.AdminAuth" — the e2e asserts its presence/absence BY NAME
            // throughout (sign-in, refusal, revocation, deactivation legs).
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.LoginPath = "/";
            options.AccessDeniedPath = "/";

            var previousValidatePrincipal = options.Events.OnValidatePrincipal;
            options.Events.OnValidatePrincipal = async context =>
            {
                await previousValidatePrincipal(context);
                await RevalidateStaffSession(context);
            };
        });

        // ExpireTimeSpan resolves from 9A admin.session_lifetime_hours (§4) — a scoped read, so this
        // Configure<T> delegate (which runs once, lazily, on first options access) must be able to
        // create its own DI scope; first real access happens once the app is actually serving traffic
        // (after migrations + config seeding complete in Program.cs's startup sequence), so the key is
        // reliably seeded by then. Falls back to the manifest's own v0 default (8h) if a read fails for
        // any reason — a fail-SAFE default, never a boot crash over a cookie lifetime.
        services.AddOptions<CookieAuthenticationOptions>(CookieScheme)
            .Configure<IServiceScopeFactory>((options, scopeFactory) =>
            {
                options.ExpireTimeSpan = TimeSpan.FromHours(ResolveIntConfigSync(scopeFactory, "admin.session_lifetime_hours", fallback: 8));
            });

        return services;
    }

    private static async Task RevalidateStaffSession(Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext context)
    {
        var staffIdClaim = context.Principal?.FindFirst(StaffClaimTypes.StaffId)?.Value;
        var stampClaim = context.Principal?.FindFirst(StaffClaimTypes.SecurityStamp)?.Value;
        if (staffIdClaim is null || stampClaim is null || !OpaqueId.TryParse(staffIdClaim, out var staffId))
        {
            return; // not a staff-shaped ticket at all — nothing for this leg to revalidate.
        }

        const string LastValidatedKey = "svac_last_validated_utc";
        var services = context.HttpContext.RequestServices;

        var revalidateSeconds = await ResolveIntConfigAsync(services, "admin.session_revalidate_seconds", fallback: 300, context.HttpContext.RequestAborted);

        var lastValidatedText = context.Properties.GetString(LastValidatedKey);
        var lastValidated = lastValidatedText is not null && DateTimeOffset.TryParse(lastValidatedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        if (DateTimeOffset.UtcNow - lastValidated < TimeSpan.FromSeconds(revalidateSeconds))
        {
            return; // not due yet — the whole point of a cookie is NOT hitting the DB every request.
        }

        var revalidator = services.GetRequiredService<IStaffSessionRevalidator>();
        var stillValid = await revalidator.IsStillValid(staffId, stampClaim, context.HttpContext.RequestAborted);

        if (!stillValid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieScheme);
            return;
        }

        context.Properties.SetString(LastValidatedKey, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private static int ResolveIntConfigSync(IServiceScopeFactory scopeFactory, string key, int fallback)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IConfigRegistry>();
            return registry.GetValue<int>(key).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return fallback;
        }
    }

    private static async Task<int> ResolveIntConfigAsync(IServiceProvider services, string key, int fallback, CancellationToken ct)
    {
        try
        {
            var registry = services.GetRequiredService<IConfigRegistry>();
            return await registry.GetValue<int>(key, ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return fallback;
        }
    }
}
