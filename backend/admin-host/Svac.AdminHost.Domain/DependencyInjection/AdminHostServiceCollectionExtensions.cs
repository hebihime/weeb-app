using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.I18n;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.AdminHost.Domain.Purge;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Purge;

namespace Svac.AdminHost.Domain.DependencyInjection;

/// <summary>
/// Wires the admin host's own additive slice into DI (SLICE_S5_CONTRACT.md §0/§1a). Mirrors
/// Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions.AddIdentityModule's shape:
/// AdminDbContext (schema `admin`) + its migration hosted service, the admin
/// <see cref="IPolicyTableSource"/> (unioned with CorePolicyTableSource at boot — never edits another
/// source's rows), admin's own additive slice of the 13A registry, and (Phase 2, this pass's own
/// deliverable, §1c/§8 seam 3) the audited-action chokepoint itself. Call AFTER AddSvacHosting/
/// AddDomainCore in the host's Program.cs, exactly like AddIdentityModule.
///
/// Deliberately NOT wired here (Pass A's own deliverable, registered by AddStaffAuth instead — see
/// GrantTableStaffRoleResolver's own doc comment + DependencyInjectionTests.cs, which pins that a bare
/// AddAdminHostModule composition must still resolve DenyAllStaffRoleResolver): the real, grant-table-
/// backed <see cref="IStaffRoleResolver"/> override, and the staff-auth-backed
/// <see cref="Svac.DomainCore.Hosting.IBearerAuthenticator"/> override. <see cref="IAdminActionExecutor"/>
/// itself has no opinion on WHICH IStaffRoleResolver the injected <see cref="IPolicyEngine"/> carries —
/// registering it here, ahead of AddStaffAuth, is correct regardless of composition order.
/// </summary>
public static class AdminHostServiceCollectionExtensions
{
    public static IServiceCollection AddAdminHostModule(this IServiceCollection services, string postgresConnectionString)
    {
        services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(postgresConnectionString));
        // Pass B: ALSO register the factory form. EF Core's DbContext is not safe for concurrent use, and
        // Blazor's static-SSR renderer does not guarantee AdminLayout's own OnInitializedAsync (which
        // reads IStaffRoleResolver -> the SAME request-scoped AdminDbContext, for nav filtering) finishes
        // before a CHILD page's OnInitializedAsync starts touching that same scoped instance — Dashboard.
        // razor never hit this (it never touches AdminDbContext), but StaffRoles.razor (Pass B, the FIRST
        // desk page with its own DB-backed initialization) does, and DID trip EF's ConcurrencyDetector in
        // a live HTTP round-trip test before this registration was added. A desk page that needs its OWN
        // DB read during initialization should resolve IDbContextFactory<AdminDbContext> and create a
        // FRESH, independent context/connection (Microsoft's own documented pattern for exactly this
        // Blazor-plus-EF-Core hazard) rather than share the ambient scoped instance AdminLayout also
        // touches — never edits AddStaffAuth/AdminLayout.razor (Pass A) to "fix" the shared instance
        // itself.
        services.AddDbContextFactory<AdminDbContext>(options => options.UseNpgsql(postgresConnectionString));
        services.AddHostedService<AdminMigrationHostedService>();

        // Phase-2a union (PHASE_2A_SUBSTRATE.md §1): admin's OWN IPolicyTableSource, unioned with
        // CorePolicyTableSource at boot — never edits another source's rows. Duplicate action key across
        // sources is a boot refusal (PolicyTable's constructor throws, already proven at S1/S3).
        services.AddSingleton<IPolicyTableSource, AdminPolicyTableSource>();

        // Phase-2a union (SLICE_S3_CONTRACT.md §6a pattern, extended by S5 §6): admin's own additive
        // slice of the 13A registry, unioned with CorePurgeRegistrySource at boot.
        services.AddSingleton<IPurgeRegistrySource, AdminPurgeRegistrySource>();

        // SLICE_S5_CONTRACT.md §8 seam 14: the keyed-string catalog every Razor page renders through.
        services.AddSingleton<AdminStringCatalog>();

        // SLICE_S5_CONTRACT.md §1c/§8 seam 3 (Phase 2, this pass): the ONE door every staff mutation
        // flows through. Scoped — it holds the request-scoped AdminDbContext/CoreDbContext it briefly
        // re-points at a shared connection+transaction for the duration of one Execute() call (see
        // AdminActionExecutor's own doc comment), never a singleton (that would leak one request's
        // connection swap into every other concurrent request sharing the instance).
        services.AddScoped<IAdminActionExecutor, AdminActionExecutor>();

        return services;
    }
}
