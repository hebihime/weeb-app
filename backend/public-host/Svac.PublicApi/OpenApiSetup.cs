using Microsoft.AspNetCore.OpenApi;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.PublicApi;

/// <summary>
/// Shared OpenAPI document configuration between the real host and the emit-openapi CLI mode (Program.
/// cs, OpenApiContractEmitter.cs) — one call, so both paths always produce an identical document shape.
/// </summary>
public static class OpenApiSetup
{
    public static void Configure(OpenApiOptions options)
    {
        // The emitter boots on an ephemeral loopback port (a fresh random port every run) purely to
        // fetch the generated document over real HTTP; without this transformer, `servers[0].url`
        // would embed that random port and contracts/openapi.v0.json would "drift" on every single
        // regeneration even with zero real API-surface change, defeating the git-diff drift gate (§1c).
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Servers?.Clear();
            return Task.CompletedTask;
        });

        // Shared components pinned NOW so clients generate once (§1c), even though zero endpoints
        // reference them yet (S1 ships zero mutation paths, §0): LimitReached, Problem, CursorPage<T>,
        // OpaqueId (string format). Registered via GetOrCreateSchemaAsync so the SAME reflection-driven
        // schema generation real endpoint types get is used here too — one code path, not a
        // hand-duplicated schema.
        options.AddDocumentTransformer(async (document, context, ct) =>
        {
            document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
            await RegisterSchema<LimitReached>(document, context, ct);
            await RegisterSchema<Problem>(document, context, ct);
            await RegisterSchema<CursorPage<string>>(document, context, ct);
            await RegisterOpaqueIdSchema(document, context, ct);

            // securitySchemes.bearer declared placeholder (§1c); S3 fills semantics. No trust-shaped
            // property exists in any request schema (contract-lint rule 1 goes from vacuous to live).
            document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Placeholder declared at S1; S3 (identity) fills real semantics.",
            };
        });
    }

    private static async Task RegisterSchema<T>(Microsoft.OpenApi.OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        var schema = await context.GetOrCreateSchemaAsync(typeof(T), cancellationToken: ct);
        document.Components!.Schemas ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSchema>();
        var name = typeof(T).IsGenericType ? typeof(T).Name[..typeof(T).Name.IndexOf('`', StringComparison.Ordinal)] : typeof(T).Name;
        document.Components.Schemas[name] = schema;
    }

    private static async Task RegisterOpaqueIdSchema(Microsoft.OpenApi.OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        // The [JsonConverter(typeof(OpaqueIdJsonConverter))] attribute on OpaqueId makes it serialize as
        // a bare string; reflection-driven schema generation may still infer an object shape from the
        // record struct's own properties, so the type + format are pinned explicitly here rather than
        // trusted to converter-aware inference.
        _ = await context.GetOrCreateSchemaAsync(typeof(OpaqueId), cancellationToken: ct);
        document.Components!.Schemas ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSchema>();
        document.Components.Schemas["OpaqueId"] = new Microsoft.OpenApi.OpenApiSchema
        {
            Type = Microsoft.OpenApi.JsonSchemaType.String,
            Format = "opaque-id",
            Description = "A prefixed ULID (e.g. \"usr_01H...\"). No raw Guid/uuid ever crosses the API (SLICE_S1_CONTRACT.md §1b).",
        };
    }
}
