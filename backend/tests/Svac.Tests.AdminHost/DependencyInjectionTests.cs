using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost.Domain.DependencyInjection;
using Svac.AdminHost.Domain.I18n;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.DependencyInjection;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// The Scaffold-phase "trivial container test" (SLICE_PLAYBOOK.md Phase 1 gate), DI-resolution half:
/// proves <see cref="AdminHostServiceCollectionExtensions.AddAdminHostModule"/> is DI-resolvable end to
/// end against a real <c>AddDomainCore</c> composition — mirrors Svac.Tests.Identity.
/// DependencyInjectionTests.AddIdentityModule_OverAddDomainCore_ResolvesBothPublicInterfaces exactly.
/// Deterministic, zero network: <c>AddDbContext&lt;AdminDbContext&gt;</c> registers lazily.
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddAdminHostModule_OverAddDomainCore_ResolvesAdminDbContextAndTheStringCatalog()
    {
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: true);
        services.AddAdminHostModule("Host=localhost;Database=svac-di-check-only");

        using var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AdminDbContext>();
        var strings = provider.GetRequiredService<AdminStringCatalog>();

        Assert.NotNull(db);
        Assert.Equal("Staff sign-in", strings["admin.signin.title"]);
    }

    [Fact]
    public void AddAdminHostModule_UnionsWithCorePolicyTableSource_TheRealAdminRowsAreReachable()
    {
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: true);
        services.AddAdminHostModule("Host=localhost;Database=svac-di-check-only");

        using var provider = services.BuildServiceProvider();
        var table = provider.GetRequiredService<IPolicyTable>();

        // Both S1's own core rows AND admin's own §3 rows are reachable through ONE union — no slice
        // ever edits domain-core's table directly (PHASE_2A_SUBSTRATE.md §1).
        Assert.NotNull(table.Find("core.config.set.founder"));
        Assert.NotNull(table.Find("admin.host.transport"));
        Assert.NotNull(table.Find("admin.staff.provision"));
        Assert.NotNull(table.Find("admin.dashboard.read"));
    }

    [Fact]
    public void AddAdminHostModule_DoesNotOverride_TheFailClosedDenyAllStaffRoleResolverDefault()
    {
        // SLICE_S5_CONTRACT.md §1d/§0: a staff actor with no real resolver has NO roles, fail-closed —
        // this scaffold does NOT register a grant-table-backed resolver (Phase 2), so the domain-core
        // default MUST still be authoritative on this host.
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: true);
        services.AddAdminHostModule("Host=localhost;Database=svac-di-check-only");

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IStaffRoleResolver>();

        Assert.IsType<DenyAllStaffRoleResolver>(resolver);
    }
}
