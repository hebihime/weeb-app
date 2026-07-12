using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost.Domain.I18n;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.AdminHost.Domain.Purge;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Purge;

namespace Svac.AdminHost.Domain.DependencyInjection;

/// <summary>
/// Wires the admin host's own additive slice into DI (SLICE_S5_CONTRACT.md §0/§1a) — SCAFFOLD phase.
/// Mirrors Svac.Identity.DependencyInjection.IdentityServiceCollectionExtensions.AddIdentityModule's
/// shape: AdminDbContext (schema `admin`) + its migration hosted service, the admin
/// <see cref="IPolicyTableSource"/> (unioned with CorePolicyTableSource at boot — never edits another
/// source's rows), and admin's own additive slice of the 13A registry. Call AFTER AddSvacHosting/
/// AddDomainCore in the host's Program.cs, exactly like AddIdentityModule.
///
/// Deliberately NOT wired here (Phase 2, explicitly out of this scaffold's deliverable list): a real
/// <see cref="IStaffRoleResolver"/> (the grant-table-backed one — DenyAllStaffRoleResolver, AddDomainCore's
/// default, stays authoritative on this host until Phase 2 registers the real one), a staff-auth-backed
/// <see cref="Svac.DomainCore.Hosting.IBearerAuthenticator"/> override (this host uses cookie auth via a
/// dedicated staff-auth transport, Phase 2 — AddSvacHosting's anonymous default is byte-identical and
/// correct for the anonymous-reachable sign-in-page/component-dispatch surface this scaffold ships),
/// AdminActionExecutor, and any tile source.
/// </summary>
public static class AdminHostServiceCollectionExtensions
{
    public static IServiceCollection AddAdminHostModule(this IServiceCollection services, string postgresConnectionString)
    {
        services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(postgresConnectionString));
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

        return services;
    }
}
