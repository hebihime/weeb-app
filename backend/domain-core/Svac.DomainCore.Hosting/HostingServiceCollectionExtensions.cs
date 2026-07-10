using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;

namespace Svac.DomainCore.Hosting;

/// <summary>DI + middleware wiring every host mounts identically (SLICE_S1_CONTRACT.md §0/§1a).</summary>
public static class HostingServiceCollectionExtensions
{
    public static IServiceCollection AddSvacHosting(this IServiceCollection services)
    {
        services.AddSingleton<AmbientRequestContextAccessor>();
        services.AddSingleton<IRequestContextAccessor>(sp => sp.GetRequiredService<AmbientRequestContextAccessor>());
        return services;
    }

    /// <summary>Mounts RequestContextMiddleware. Call first in the pipeline — before any module code runs.</summary>
    public static IApplicationBuilder UseSvacRequestContext(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestContextMiddleware>();
}
