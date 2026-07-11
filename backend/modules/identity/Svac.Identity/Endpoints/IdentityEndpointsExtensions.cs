namespace Svac.Identity.Endpoints;

/// <summary>The single call a host makes to map every identity endpoint this build ships (SLICE_S3_CONTRACT.md §1c: signup/* + auth/* + the minimal GET /v1/me). Mirrors Svac.PublicApi.Endpoints.MapAll's shared-mapping pattern — one call, used by both the real host and the DB-free OpenAPI emitter.</summary>
public static class IdentityEndpointsExtensions
{
    public static void MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapSignupEndpoints();
        app.MapAuthEndpoints();
        app.MapMeEndpoints();
    }
}
