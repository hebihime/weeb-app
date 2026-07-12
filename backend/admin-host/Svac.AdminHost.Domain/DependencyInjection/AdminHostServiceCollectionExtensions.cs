using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost.Domain.Audit;
using Svac.AdminHost.Domain.Execution;
using Svac.AdminHost.Domain.I18n;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.AdminHost.Domain.Purge;
using Svac.AdminHost.Domain.Search;
using Svac.AdminHost.Domain.Tiles;
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
        // [Finisher fix] Register ONLY AddDbContextFactory, never ALSO AddDbContext for the SAME context
        // type: EF Core's DbContextFactory<TContext> constructor takes an optional DbContextOptions
        // <TContext> parameter, and AddDbContext<TContext> separately registers a SCOPED DbContextOptions
        // <TContext> in DI — combining both makes ASP.NET Core's ValidateOnBuild (on by default in the
        // Development environment, exactly what docker-compose.yml's admin-host service sets) throw
        // "Cannot consume scoped service 'DbContextOptions<AdminDbContext>' from singleton
        // 'IDbContextFactory<AdminDbContext>'" at boot — a real fresh-boot crash, not caught by the
        // deterministic suite (its test hosts do not run in the Development environment, so ValidateOnBuild
        // never fires there). The fix Microsoft's own docs give for needing BOTH a request-scoped
        // AdminDbContext (StaffSignInPipeline/StaffBootstrapper/GrantTableStaffRoleResolver/
        // DevSeamsStaffTransport/AdminActionExecutor all take one via direct constructor injection) AND an
        // injectable factory (StaffRoles.razor.cs's own documented concurrent-DbContext hazard, immediately
        // below): register the factory as the ONE source of DbContextOptions, then derive the scoped
        // AdminDbContext FROM that factory rather than a second, independently-configured registration.
        services.AddDbContextFactory<AdminDbContext>(options => options.UseNpgsql(postgresConnectionString));
        services.AddScoped<AdminDbContext>(sp => sp.GetRequiredService<IDbContextFactory<AdminDbContext>>().CreateDbContext());
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

        // SLICE_S5_CONTRACT.md §0/§8 seam 6/§9 (Pass D): the host-owned search port. EmptyUserSearchSource
        // is the day-one registration — honest-dark UI, never fabricated rows; S3's real adapter is a
        // later ONE-DI-LINE swap (see IUserSearchSource's own doc comment), never a signature change here.
        services.AddSingleton<IUserSearchSource, EmptyUserSearchSource>();
        // The audited-execute path around that port (auth->4A->quota->audit->render, §0/§9) — scoped
        // exactly like IAdminActionExecutor itself, which this service is a thin, single-purpose client of.
        services.AddScoped<UserSearchExecutionService>();

        // SLICE_S5_CONTRACT.md §0 (Pass D): the Audit Trail desk's audited-view path — same shape,
        // one action (admin.audit.read) instead of admin.user_search.execute.
        services.AddScoped<AuditViewExecutionService>();

        // SLICE_S5_CONTRACT.md §8 seam 2 (Pass D): every LIVE S1/S2 tile source, registered as
        // IEnumerable<IMetricsTileSource> (mirrors the IPolicyTableSource/IPurgeRegistrySource union
        // pattern above) — Dashboard.razor renders exactly the set resolved here, role-filtered.
        // ConfigChangeTileSource is registered FIRST (§8 seam 2: "the config-change tile is tile #1") —
        // the built-in container resolves IEnumerable<T> in registration order, and Dashboard.razor.cs
        // renders tiles in the order it receives them, so this ordering IS the rendering order, never a
        // second, separate sort key to keep in sync.
        services.AddScoped<IMetricsTileSource, ConfigChangeTileSource>();
        services.AddScoped<IMetricsTileSource, PurgeRunsTileSource>();
        services.AddScoped<IMetricsTileSource, StreamVolumeTileSource>();
        services.AddScoped<IMetricsTileSource, StaffSignInsTileSource>();
        services.AddScoped<IMetricsTileSource, AimlRouteTileSource>();

        return services;
    }
}
