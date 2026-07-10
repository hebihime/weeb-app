using Microsoft.Extensions.DependencyInjection;
using Svac.AimlRouter.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Con;
using Svac.DomainCore.Contracts.Payment;
using Svac.DomainCore.Contracts.Region;
using Svac.DomainCore.DependencyInjection;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// SLICE_S1_CONTRACT.md §1b/§12.16: an arch test proves DevSeams impls are never referenced from prod DI
/// composition. AddDomainCore(devSeamsEnabled: false) must never register a type carrying
/// [DevSeamsOnly]; AddDomainCore(devSeamsEnabled: true) must register exactly those types.
/// </summary>
public sealed class DevSeamsNotInProdDiTests
{
    [Theory]
    [InlineData(typeof(IPaymentService))]
    [InlineData(typeof(IRegionResolver))]
    [InlineData(typeof(IConDayResolver))]
    public void ProdComposition_NeverResolvesADevSeamsOnlyType(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: false);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(serviceType);
        Assert.False(
            Attribute.IsDefined(resolved.GetType(), typeof(DevSeamsOnlyAttribute)),
            $"{resolved.GetType().Name} is [DevSeamsOnly] but was resolved with devSeamsEnabled: false.");
    }

    [Theory]
    [InlineData(typeof(IPaymentService))]
    [InlineData(typeof(IRegionResolver))]
    [InlineData(typeof(IConDayResolver))]
    public void DevComposition_ResolvesTheDevSeamsOnlyType(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: true);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(serviceType);
        Assert.True(
            Attribute.IsDefined(resolved.GetType(), typeof(DevSeamsOnlyAttribute)),
            $"{resolved.GetType().Name} was resolved with devSeamsEnabled: true but is not [DevSeamsOnly] — dev and prod should never share a fake backend silently.");
    }

    [Fact]
    public void ProdComposition_ThrowsOnUnconfiguredFieldKeyVault_L18FailClosed()
    {
        var services = new ServiceCollection();
        services.AddDomainCore("Host=localhost;Database=svac-di-check-only", devSeamsEnabled: false);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<Svac.DomainCore.Contracts.FieldEncryption.IFieldKeyVault>());
    }

    // -----------------------------------------------------------------------------------------------
    // TRUST-BREAK-2 (SECURITY_REVIEW_S2.md): extends this exact never-in-prod-DI family to
    // AimlRouter's own internal SPI (IModelProvider / SeedProvider / AnthropicLocalTransport), the S2
    // gap TrustBoundaryLensS2Tests.cs's own BREAK 2 comment names explicitly ("DevSeamsNotInProdDiTests.
    // cs covers only AddDomainCore's IPaymentService family, never IModelProvider"). IModelProvider is
    // `internal` to Svac.AimlRouter (never crosses the module boundary, §1b SPI seam) — resolved here via
    // a runtime Type lookup instead of InternalsVisibleTo, so this architecture test never needs to see
    // inside the module's own implementation types, only its public AddAimlRouter wiring surface.
    // -----------------------------------------------------------------------------------------------
    private static Type ModelProviderType() =>
        typeof(Svac.AimlRouter.Providers.AnthropicApiKeyGuard).Assembly.GetType("Svac.AimlRouter.Providers.IModelProvider")
        ?? throw new InvalidOperationException("Svac.AimlRouter.Providers.IModelProvider not found by reflection — has it moved/renamed?");

    [Fact]
    public void ProdAimlRouterComposition_NeverResolvesADevSeamsOnlyModelProvider()
    {
        var services = new ServiceCollection();
        services.AddAimlRouter(devSeamsEnabled: false, environmentName: "Production", anthropicApiKey: "test-key-not-a-real-secret");
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(ModelProviderType());
        Assert.False(
            Attribute.IsDefined(resolved.GetType(), typeof(DevSeamsOnlyAttribute)),
            $"{resolved.GetType().Name} is [DevSeamsOnly] but was resolved from a Production AddAimlRouter composition.");
    }

    [Fact]
    public void DevAimlRouterComposition_ResolvesTheDevSeamsOnlyModelProvider()
    {
        var services = new ServiceCollection();
        services.AddAimlRouter(devSeamsEnabled: true, environmentName: "Development");
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService(ModelProviderType());
        Assert.True(
            Attribute.IsDefined(resolved.GetType(), typeof(DevSeamsOnlyAttribute)),
            $"{resolved.GetType().Name} was resolved with devSeamsEnabled: true but is not [DevSeamsOnly] — dev and prod should never share a fake backend silently.");
    }
}
