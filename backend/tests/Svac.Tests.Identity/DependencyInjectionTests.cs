using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.DependencyInjection;
using Svac.Identity.Contracts;
using Svac.Identity.DependencyInjection;
using Svac.Identity.Email;
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
        // BUILD phase: AddIdentityModule now takes IdentityDbContext's own connection string (mirrors
        // AddDomainCore's shape). AddDbContext<T> registers lazily, so building the provider still never
        // opens a real Postgres connection. Pass 2b's real IAccountLifecycle (AccountLifecycle, replacing
        // the Phase-1 throwing stub) legitimately needs IEmailSender at construction (RequestDeletion
        // sends email.deletion_scheduled) — smtpOptions is passed now so resolving IAccountLifecycle below
        // does not trip L18's fail-closed IEmailSender throw; DevSeamsNotInProdDiTests.cs +
        // DeletionEndpointsHttpTests.cs separately prove the no-smtp-configured throw itself still fires.
        services.AddIdentityModule("Host=localhost;Database=svac-di-check-only", SmtpTransportOptions.MailpitDefault());

        using var provider = services.BuildServiceProvider();

        var lifecycle = provider.GetRequiredService<IAccountLifecycle>();
        var directory = provider.GetRequiredService<IAccountDirectory>();

        Assert.NotNull(lifecycle);
        Assert.NotNull(directory);
    }
}
