using Microsoft.Extensions.Logging;

namespace Svac.PublicApi;

/// <summary>Source-generated logging delegates (CA1848) for Program.cs's startup config-seeding pass.</summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "startup config seeding SKIP: manifest not found at {ManifestPath}")]
    public static partial void ConfigManifestNotFound(ILogger logger, string manifestPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "startup config seeding: {Seeded} new 9A key(s) seeded")]
    public static partial void ConfigSeedingComplete(ILogger logger, int seeded);

    [LoggerMessage(Level = LogLevel.Error, Message = "startup config seeding FAILED (non-fatal at S1 — no request path yet depends on it, but this is not expected to fail)")]
    public static partial void ConfigSeedingFailed(ILogger logger, Exception exception);
}
