using Npgsql;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.DependencyInjection;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.DependencyInjection;

// backend/e2e/aiml-router-diagnostic-host/Program.cs — SLICE_S2_CONTRACT.md §1c / §10.3's "test-host
// diagnostic canary, never shipped in the contract." Test tooling only: never referenced by
// Svac.PublicApi, never in Svac.sln's production hosts, no Dockerfile, not a release train. See the
// .csproj header and backend/e2e/aiml-router.e2e.mjs's own header for the full design rationale.
//
// Talks to its OWN isolated Postgres database (created here, on the SAME compose-managed Postgres
// server the real stack uses) so its diagnostic-only 9A config values (config/diagnostic.config.json)
// never collide with whatever the real `svac` database holds.

var pgHost = Environment.GetEnvironmentVariable("AIML_E2E_PGHOST") ?? "localhost";
var pgPort = Environment.GetEnvironmentVariable("AIML_E2E_PGPORT") ?? "5433";
var pgUser = Environment.GetEnvironmentVariable("AIML_E2E_PGUSER") ?? "svac";
var pgPassword = Environment.GetEnvironmentVariable("AIML_E2E_PGPASSWORD") ?? "svac_dev_only";
var dbName = Environment.GetEnvironmentVariable("AIML_E2E_DBNAME") ?? "svac_aiml_e2e_diag";
var freshBoot = Environment.GetEnvironmentVariable("AIML_ROUTER_E2E_FRESH") == "1";
var httpPort = Environment.GetEnvironmentVariable("AIML_E2E_PORT") ?? "5299";

var maintenanceConnectionString = $"Host={pgHost};Port={pgPort};Username={pgUser};Password={pgPassword};Database=postgres";
var appConnectionString = $"Host={pgHost};Port={pgPort};Username={pgUser};Password={pgPassword};Database={dbName}";

await EnsureDiagnosticDatabase(maintenanceConnectionString, dbName, freshBoot);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{httpPort}");
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; });

// devSeamsEnabled: true, ALWAYS — this whole process only exists under DevSeams (SLICE_S2_CONTRACT.md
// §1b/§12.2: local-vs-API is a transport selected by ENVIRONMENT, never a 9A value). AddAimlRouter picks
// AnthropicLocalTransport under DevSeams — the real local `claude` CLI, no key, billed (if at all) to
// whatever the CLI's own session already bills, never a router secret.
builder.Services.AddDomainCore(appConnectionString, devSeamsEnabled: true);
builder.Services.AddAimlRouter(devSeamsEnabled: true);

var app = builder.Build();

var ready = false;

app.MapGet("/health", () =>
    ready ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

// The ONE diagnostic route (SLICE_S2_CONTRACT.md §10.3). Deliberately calls IAimlRouter.InvokeAsync
// IN-PROCESS exactly the way a real future consumer module would (§0: "consumers are backend modules
// calling in-process through the contract assembly") — this route is the stand-in for that not-yet-built
// consumer, never a client-facing surface, never mapped on the real Svac.PublicApi host, never in
// contracts/openapi.v0.json. Every outcome (Success or any Failure cause) returns HTTP 200 with the same
// response shape distinguished only by body fields — deliberately, so this diagnostic tool itself proves
// FAILURE UNOBSERVABILITY at the transport level: there is no distinct HTTP status per failure cause to
// probe (SLICE_S2_CONTRACT.md §1b). Exposing FailureCause/Provider/Model in this JSON body is safe only
// because this is diagnostic tooling with no client audience — the arch-tested rule these fields NEVER
// serialize into a real client-bound DTO (TrustDtoArchTest.cs) is about Svac.PublicApi's real DTOs, which
// this project is not.
app.MapPost("/invoke", async (InvokeDiagnosticRequest body, IAimlRouter router, CancellationToken ct) =>
{
    AimlTaskKind task;
    CallerModule caller;
    PayloadClass payloadClass;
    try
    {
        task = Enum.Parse<AimlTaskKind>(body.Task, ignoreCase: true);
        caller = Enum.Parse<CallerModule>(body.Caller, ignoreCase: true);
        payloadClass = Enum.Parse<PayloadClass>(body.PayloadClass, ignoreCase: true);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = $"invalid diagnostic request field: {ex.Message}" });
    }

    var pin = body.ExplicitPin is { } p ? new ProviderPin(p.Provider, p.Model) : null;
    var payload = AimlPayload.ForUserTurn(body.UserText);
    var request = new AimlRequest(task, caller, payloadClass, Subject: null, payload, body.TargetLocale, pin);

    var actor = ActorRef.System(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared));
    var ctx = RequestContext.System(actor, correlationId: $"aiml-e2e-{Guid.NewGuid():N}");

    var result = await router.InvokeAsync(request, ctx, ct);

    var response = result switch
    {
        AimlResult.Success s => new InvokeDiagnosticResponse(
            Outcome: "Success",
            InvocationId: s.Receipt.InvocationId.Value,
            Provider: s.Receipt.Provider,
            Model: s.Receipt.Model,
            DecisionSource: s.Receipt.DecisionSource.ToString(),
            PolicyVersion: s.Receipt.PolicyVersion,
            FallbackDepth: s.Receipt.FallbackDepth,
            FailoverFrom: s.Receipt.FailoverFrom,
            FailureCause: null,
            OutputText: s.Output.OutputText),
        AimlResult.Failure f => new InvokeDiagnosticResponse(
            Outcome: "Failure",
            InvocationId: null,
            Provider: null,
            Model: null,
            DecisionSource: null,
            PolicyVersion: null,
            FallbackDepth: null,
            FailoverFrom: null,
            FailureCause: f.Cause.ToString(),
            OutputText: null),
        _ => throw new InvalidOperationException("unreachable: AimlResult is a closed Success|Failure union (SLICE_S2_CONTRACT.md §1b)."),
    };

    // Always 200 (see the route's own doc comment above) — the failure/success distinction lives ONLY
    // in the body, never the transport, so this diagnostic tool cannot itself become an observability
    // side-channel the real product doesn't have.
    return Results.Ok(response);
});

await app.StartAsync();
await SeedDiagnosticConfig(app);
ready = true;
Console.WriteLine($"aiml-router-diagnostic-host READY on http://0.0.0.0:{httpPort} (db={dbName})");
await app.WaitForShutdownAsync();
return 0;

static async Task EnsureDiagnosticDatabase(string maintenanceConnectionString, string dbName, bool fresh)
{
    // Npgsql identifiers cannot be parameterized; dbName is operator/CI-controlled (an env var this
    // process's own launcher sets), never untrusted network input, so a direct interpolation into DDL
    // here is the same trust boundary as every other one-time-bootstrap script in this repo (e.g.
    // build/scripts/*.sh) — never a query bound to request data.
    await using var connection = new NpgsqlConnection(maintenanceConnectionString);
    await connection.OpenAsync();

    if (fresh)
    {
        // WITH (FORCE) (Postgres 13+): disconnects any lingering session before dropping, so a killed
        // previous run of this same host never leaves the fresh-boot clause blocked on its own stale
        // connection. IF EXISTS: a first-ever run has nothing to drop.
        await using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
        await dropCommand.ExecuteNonQueryAsync();
    }

    await using var existsCommand = connection.CreateCommand();
    existsCommand.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name;";
    existsCommand.Parameters.AddWithValue("name", dbName);
    var exists = await existsCommand.ExecuteScalarAsync() is not null;

    if (!exists)
    {
        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = $"CREATE DATABASE \"{dbName}\";";
        await createCommand.ExecuteNonQueryAsync();
    }
}

static async Task SeedDiagnosticConfig(WebApplication webApp)
{
    // Mirrors Svac.PublicApi/Program.cs's SeedConfigOnStartup exactly (same ConfigSeedLoader, same
    // idempotent-additive contract) — just pointed at THIS project's own diagnostic manifest instead of
    // domain-core's, and against the isolated database opened above.
    using var scope = webApp.Services.CreateScope();
    var loader = scope.ServiceProvider.GetRequiredService<ConfigSeedLoader>();
    var manifestPath = Path.Combine(AppContext.BaseDirectory, "config", "diagnostic.config.json");
    if (!File.Exists(manifestPath))
    {
        throw new InvalidOperationException($"diagnostic config manifest not found at {manifestPath} — this project must ship its own copy under config/.");
    }

    var systemActor = ActorRef.System(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared));
    var ctx = RequestContext.System(systemActor, correlationId: "aiml-e2e-diagnostic-host-seed");
    var seeded = await loader.SeedFromFile(manifestPath, ctx);
    Console.WriteLine($"aiml-router-diagnostic-host: seeded {seeded} diagnostic config row(s).");
}

/// <summary>POST /invoke request shape — a diagnostic-only, non-versioned JSON contract (never contracts/openapi.v0.json).</summary>
public sealed record InvokeDiagnosticRequest(
    string Task,
    string Caller,
    string PayloadClass,
    string UserText,
    string? TargetLocale = null,
    InvokeDiagnosticPin? ExplicitPin = null);

public sealed record InvokeDiagnosticPin(string Provider, string Model);

/// <summary>POST /invoke response shape — diagnostic-only telemetry never rendered through any real client DTO.</summary>
public sealed record InvokeDiagnosticResponse(
    string Outcome,
    string? InvocationId,
    string? Provider,
    string? Model,
    string? DecisionSource,
    int? PolicyVersion,
    int? FallbackDepth,
    string? FailoverFrom,
    string? FailureCause,
    string? OutputText);
