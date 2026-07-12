using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Svac.Identity.Persistence;

/// <summary>
/// Applies every IdentityDbContext migration on startup under a Postgres advisory lock — mirrors
/// Svac.DomainCore.Persistence.MigrationHostedService exactly (SLICE_S3_CONTRACT.md item 1: "Wire
/// IdentityDbContext migration to APPLY at boot"), with its OWN distinct advisory-lock key so a schema-
/// `identity` migration boot never contends with schema-`core`'s lock. Register this AFTER
/// Svac.DomainCore's MigrationHostedService (AddIdentityModule does this) so no identity stream consumer
/// starts before schema `core` exists — identity's own consumers (the consent projections) additionally
/// depend on schema `identity` existing, which this hosted service itself guarantees before it completes.
/// </summary>
public sealed partial class IdentityMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityMigrationHostedService> logger)
    : IHostedService
{
    // Fixed, arbitrary 64-bit advisory-lock key distinct from Svac.DomainCore.Persistence.
    // MigrationHostedService's own key — two different schemas, two different locks, never contending.
    private const long AdvisoryLockKey = 0x53_56_41_43_5F_49_44; // ASCII "SVAC_ID" truncated to fit a long

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("IdentityDbContext has no connection string configured.");

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

    [LoggerMessage(Level = LogLevel.Information, Message = "IdentityMigrationHostedService: acquiring advisory lock {Key} before migrating schema identity")]
    private partial void LogAcquiringLock(long key);

    [LoggerMessage(Level = LogLevel.Information, Message = "IdentityMigrationHostedService: applying pending migrations")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(Level = LogLevel.Information, Message = "IdentityMigrationHostedService: migrations applied cleanly")]
    private partial void LogMigrationsApplied();
}
