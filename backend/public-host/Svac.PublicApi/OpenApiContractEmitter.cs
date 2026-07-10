using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Svac.DomainCore.Contracts.Api;

namespace Svac.PublicApi;

/// <summary>
/// Emits contracts/openapi.v0.json (SLICE_S1_CONTRACT.md §1c) by booting a minimal, DB-free instance of
/// this exact host — same Endpoints.MapAll call the real Program.cs uses — on an ephemeral loopback
/// port, fetching the framework-generated document over real HTTP, and writing it to disk. Zero
/// DomainCore/DB wiring: the drift gate (CI regenerates + git diff --exit-code) stays hermetic.
/// </summary>
public static class OpenApiContractEmitter
{
    public static async Task<int> Run(string[] args)
    {
        var outputPath = args.Length > 1 ? args[1] : DefaultOutputPath();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddOpenApi("v0", OpenApiSetup.Configure);

        var localesPath = ClientConfigLoader.ResolveDefaultLocalesPath(builder.Environment.ContentRootPath);
        builder.Services.AddSingleton(ClientConfigLoader.Load(localesPath));

        var app = builder.Build();
        app.MapOpenApi("/openapi/{documentName}.json");
        Endpoints.MapAll(app);

        await app.StartAsync();
        try
        {
            var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("no server address feature available.");
            var baseUrl = addressFeature.Addresses.First();

            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var json = await client.GetStringAsync("/openapi/v0.json");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, json);
            Console.WriteLine($"emit-openapi: wrote {outputPath} ({json.Length} bytes)");
            return 0;
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string DefaultOutputPath()
    {
        // backend/public-host/Svac.PublicApi -> repo root -> contracts/openapi.v0.json
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "contracts", "openapi.v0.json");
    }
}
