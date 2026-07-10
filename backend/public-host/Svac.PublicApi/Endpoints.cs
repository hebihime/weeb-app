using Microsoft.AspNetCore.Mvc;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Behavioral;

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

        app.MapGet("/v1/client-config", async (
                [FromServices] ClientConfigResponse config,
                [FromServices] IBehavioralStream behavioral,
                [FromServices] IRequestContextAccessor requestContext,
                CancellationToken ct) =>
            {
                // §8 "analytics written AND received" / §10.4: the behavioral seam is proven on a REAL
                // request path — emit here, then the substrate E2E reads the row back and asserts the
                // watermark advanced. Never emit-only (the hmg emit-without-sink scar). One door only:
                // IBehavioralStream (an Append onto StreamType.Behavioral keyed by the calling actor).
                await behavioral.Emit("client_config.fetched", "{}", requestContext.Current, ct);
                return Results.Ok(config);
            })
            .WithName("GetClientConfig")
            .Produces<ClientConfigResponse>(StatusCodes.Status200OK);
    }
}
