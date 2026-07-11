using Microsoft.Extensions.DependencyInjection;
using Svac.Identity.Contracts;

namespace Svac.Identity.DependencyInjection;

/// <summary>
/// Wires the identity module into DI (SLICE_S3_CONTRACT.md §0/§1a) — the second occupant of
/// <c>backend/modules/</c>, copying the AimlRouter module template exactly (backend/modules/AimlRouter/
/// Svac.AimlRouter/DependencyInjection/AimlRouterServiceCollectionExtensions.cs). Phase 1
/// (SLICE_PLAYBOOK.md scaffold gate) registers ONLY DI-resolvable stub implementations of
/// IAccountLifecycle/IAccountDirectory, each throwing NotImplementedException("S3 Phase 2"). No config,
/// no devSeams flag, no email/consent/export seam wiring — those all land with the real implementations
/// in the S3 BUILD phase (SLICE_PLAYBOOK.md Phase 2).
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<IAccountLifecycle, AccountLifecycleStub>();
        services.AddScoped<IAccountDirectory, AccountDirectoryStub>();
        return services;
    }
}
