using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Svac.DomainCore.Persistence;

/// <summary>
/// Applies every CoreDbContext migration on startup under a Postgres advisory lock (SLICE_S1_CONTRACT.md
/// task brief; docker-compose.yml's L13 comment; BUILD.md §8 clause 2 fresh-boot discipline). Session-
/// level advisory lock on a fixed key so two API instances booting concurrently against the same
/// database serialize instead of racing EF's migration history table. Register this hosted service
/// FIRST — every stream-consumer hosted service (none exist at S1; the first lands with a feature
/// module) must be registered AFTER it, so no consumer ever starts reading a stream before the schema
/// it reads from exists.
/// </summary>
public sealed partial class MigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<MigrationHostedService> logger)
    : IHostedService
{
    // Fixed, arbitrary 64-bit advisory-lock key. Any two hosts (public/admin/partner, present or future)
    // migrating schema `core` share this exact key so they serialize against each other, not just
    // against themselves.
    private const long AdvisoryLockKey = 0x53_56_41_43_5F_43_4F; // ASCII "SVAC_CO" truncated to fit a long

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("CoreDbContext has no connection string configured.");

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);

        LogAcquiringLock(AdvisoryLockKey);
        await using (var acquire = lockConnection.CreateCommand())
        {
            acquire.CommandText = "SELECT pg_advisory_lock(@key)";
            acquire.Parameters.AddWithValue("key", AdvisoryLockKey);
            await acquire.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            LogApplyingMigrations();
            await db.Database.MigrateAsync(cancellationToken);
            LogMigrationsApplied();
        }
        finally
        {
            await using var release = lockConnection.CreateCommand();
            release.CommandText = "SELECT pg_advisory_unlock(@key)";
            release.Parameters.AddWithValue("key", AdvisoryLockKey);
            await release.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "MigrationHostedService: acquiring advisory lock {Key} before migrating schema core")]
    private partial void LogAcquiringLock(long key);

    [LoggerMessage(Level = LogLevel.Information, Message = "MigrationHostedService: applying pending migrations")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(Level = LogLevel.Information, Message = "MigrationHostedService: migrations applied cleanly")]
    private partial void LogMigrationsApplied();
}
