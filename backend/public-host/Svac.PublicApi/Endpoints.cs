using Svac.DomainCore.Contracts.Api;

namespace Svac.PublicApi;

/// <summary>
/// Every endpoint Svac.PublicApi maps (SLICE_S1_CONTRACT.md §0/§1c: zero business logic, health + one
/// bootstrap GET). Shared between the real host pipeline and the `--emit-openapi` contract emitter so
/// the emitted document always reflects exactly the endpoints the real host serves — one mapping call,
/// never two definitions to drift apart.
/// </summary>
public static class Endpoints
{
    public static void MapAll(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new HealthStatus("healthy", DateTimeOffset.UtcNow)))
            .WithName("GetHealth")
            .Produces<HealthStatus>(StatusCodes.Status200OK);

        app.MapGet("/v1/client-config", (ClientConfigResponse config) => Results.Ok(config))
            .WithName("GetClientConfig")
            .Produces<ClientConfigResponse>(StatusCodes.Status200OK);
    }
}
