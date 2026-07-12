using Microsoft.Extensions.Logging;

namespace Svac.AdminHost;

/// <summary>Source-generated logging delegates (CA1848) for Program.cs's startup config-seeding pass — mirrors Svac.PublicApi/Log.cs exactly.</summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "admin-host config seeding SKIP: manifest not found at {ManifestPath}")]
    public static partial void ConfigManifestNotFound(ILogger logger, string manifestPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "admin-host config seeding: {Seeded} new 9A key(s) seeded")]
    public static partial void ConfigSeedingComplete(ILogger logger, int seeded);

    [LoggerMessage(Level = LogLevel.Error, Message = "admin-host config seeding FAILED (non-fatal at scaffold — both admin manifests are empty-valid, so nothing depends on this yet, but this is not expected to fail)")]
    public static partial void ConfigSeedingFailed(ILogger logger, Exception exception);
}
