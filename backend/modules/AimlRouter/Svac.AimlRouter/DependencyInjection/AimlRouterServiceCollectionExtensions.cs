using Microsoft.Extensions.DependencyInjection;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Svac.AimlRouter.Security;

namespace Svac.AimlRouter.DependencyInjection;

/// <summary>
/// Wires the router into DI (SLICE_S2_CONTRACT.md §0/§1a) — the first occupant of
/// <c>backend/modules/</c>, so this is also THE MODULE TEMPLATE every later slice's own
/// <c>AddXyz(IServiceCollection)</c> extension copies. Nothing calls this yet: S2 ships zero consumers
/// (§0), so no host's Program.cs invokes it. It exists, compiles, and is unit-testable now so the first
/// consuming slice's job is "call this one method," not "design how a module wires into a host."
///
/// DevSeams-gated exactly like <c>DomainCoreServiceCollectionExtensions.AddDomainCore</c> (§12.2, the
/// ruling-contradiction fix): local-vs-API is a TRANSPORT selected by THIS flag, never a 9A allowlist
/// value — a desk edit can never route production traffic to a keyless local process.
/// </summary>
public static class AimlRouterServiceCollectionExtensions
{
    public static IServiceCollection AddAimlRouter(this IServiceCollection services, bool devSeamsEnabled, string? anthropicApiKey = null)
    {
        services.AddScoped<IAimlRouter, AimlRouterService>();
        services.AddSingleton<IVendorEgressAuthorizer, RefuseAllSpecialCategoryAuthorizer>();

        if (devSeamsEnabled)
        {
            services.AddSingleton<IModelProvider, AnthropicLocalTransport>();
        }
        else
        {
            services.AddSingleton<IModelProvider>(_ =>
            {
                AnthropicApiKeyGuard.Enforce(policyCanReachApiTransport: true, devSeamsEnabled: false, apiKeyConfigured: !string.IsNullOrWhiteSpace(anthropicApiKey));
                return new AnthropicApiTransport(anthropicApiKey!);
            });
        }

        return services;
    }

    /// <summary>Test-only DI surface (SLICE_S2_CONTRACT.md §1b: SeedProvider is "test DI only, NEVER an allowlist or policy value"). Never called from any host's Program.cs.</summary>
    public static IServiceCollection AddAimlRouterWithSeedProvider(this IServiceCollection services)
    {
        services.AddScoped<IAimlRouter, AimlRouterService>();
        services.AddSingleton<IVendorEgressAuthorizer, RefuseAllSpecialCategoryAuthorizer>();
        services.AddSingleton<IModelProvider, SeedProvider>();
        return services;
    }
}
