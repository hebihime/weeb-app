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
    /// <param name="devSeamsEnabled">The DevSeams environment flag (never a 9A entry, §1b/§12.16) — selects the LocalProcess transport.</param>
    /// <param name="environmentName">The hosting environment's EnvironmentName. Defaults to "Production" — the strictest posture — for callers (and this method's own adversarial tests) that never state one explicitly; a real host always passes its own <c>IHostEnvironment.EnvironmentName</c>.</param>
    /// <param name="anthropicApiKey">The resolved Key Vault-backed API key, or null when unconfigured.</param>
    public static IServiceCollection AddAimlRouter(this IServiceCollection services, bool devSeamsEnabled, string environmentName = "Production", string? anthropicApiKey = null)
    {
        services.AddScoped<IAimlRouter, AimlRouterService>();
        services.AddSingleton<IVendorEgressAuthorizer, RefuseAllSpecialCategoryAuthorizer>();

        if (devSeamsEnabled)
        {
            services.AddSingleton<IModelProvider, AnthropicLocalTransport>();
            return services;
        }

        // TRUST-BREAK-2/TRUST-BREAK-3 (SECURITY_REVIEW_S2.md): AnthropicApiTransport is registered by
        // TYPE (constructor-based), never a factory lambda, and its constructor requires an
        // AnthropicApiKey — a typed dependency this method registers ONLY when the composition is
        // lawful. An unlawful composition (missing key outside Development) leaves that dependency
        // structurally unresolvable, so the standard DI dependency-resolvability pass
        // (BuildServiceProvider(ValidateOnBuild: true)) throws AT BUILD, never at the first
        // IAimlRouter.InvokeAsync call. AnthropicApiKeyGuard.Enforce remains the single source of truth
        // for the LAW itself (Trust-F1 parity: allowlist Development by NAME) — a real host's Program.cs
        // calls it eagerly before this method, exactly the way Svac.PublicApi/Program.cs already calls
        // ProdFieldKeyVaultGuard.Enforce before AddDomainCore, for the earliest and friendliest possible
        // error message; this method's own build-time fail-closed behavior is the structural backstop
        // that holds even if a host forgets that eager call.
        services.AddSingleton<IModelProvider, AnthropicApiTransport>();

        var apiKeyConfigured = !string.IsNullOrWhiteSpace(anthropicApiKey);
        var isDevelopmentEnvironment = string.Equals(environmentName, Microsoft.Extensions.Hosting.Environments.Development, StringComparison.OrdinalIgnoreCase);
        if (isDevelopmentEnvironment || apiKeyConfigured)
        {
            services.AddSingleton(new AnthropicApiKey(anthropicApiKey ?? string.Empty));
        }
        // else: AnthropicApiKey is deliberately left unregistered — see the comment above.

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
