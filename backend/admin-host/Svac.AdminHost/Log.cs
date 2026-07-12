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

    [LoggerMessage(Level = LogLevel.Information, Message = "admin-host bootstrap: provisioned first SuperAdmin for subject {Subject}")]
    public static partial void BootstrapProvisioned(ILogger logger, string subject);

    [LoggerMessage(Level = LogLevel.Error, Message = "admin-host bootstrap FAILED (non-fatal — fix SVAC_ADMIN_BOOTSTRAP_SUBJECT and restart; a crash loop here would also break every other staff sign-in)")]
    public static partial void BootstrapFailed(ILogger logger, Exception exception);
}
