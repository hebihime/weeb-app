using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.DependencyInjection;
using Svac.Identity.Contracts;
using Svac.Identity.DependencyInjection;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// The Scaffold-phase "trivial container test" (SLICE_PLAYBOOK.md Phase 1 gate): proves
/// <see cref="IdentityServiceCollectionExtensions.AddIdentityModule"/> is DI-resolvable end to end against
/// a real <c>AddDomainCore</c> composition — the same shape
/// backend/tests/Svac.Tests.Architecture/DevSeamsNotInProdDiTests.cs already proves for AddDomainCore
/// alone. Deterministic, zero network: <c>AddDbContext&lt;CoreDbContext&gt;</c> registers lazily, so
/// building the provider never opens a real Postgres connection.
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddIdentityModule_OverAddDomainCore_ResolvesBothPublicInterfaces()
    {
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: true);
        services.AddIdentityModule();

        using var provider = services.BuildServiceProvider();

        var lifecycle = provider.GetRequiredService<IAccountLifecycle>();
        var directory = provider.GetRequiredService<IAccountDirectory>();

        Assert.NotNull(lifecycle);
        Assert.NotNull(directory);
    }
}
