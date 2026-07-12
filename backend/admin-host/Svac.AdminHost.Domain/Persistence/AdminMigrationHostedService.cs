using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Svac.AdminHost.Domain.Persistence;

/// <summary>
/// Applies every AdminDbContext migration on startup under a Postgres advisory lock — mirrors
/// Svac.DomainCore.Persistence.MigrationHostedService / Svac.Identity.Persistence.
/// IdentityMigrationHostedService exactly, with its OWN distinct advisory-lock key so a schema-`admin`
/// migration boot never contends with schema-`core`'s or schema-`identity`'s lock. Register this AFTER
/// Svac.DomainCore's MigrationHostedService (AddAdminHostModule does this) so schema `core` exists
/// first; the admin host registers no stream consumers today (§0's "zero projections" ruling), so there
/// is nothing else that must wait behind this one.
/// </summary>
public sealed partial class AdminMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AdminMigrationHostedService> logger)
    : IHostedService
{
    // Fixed, arbitrary 64-bit advisory-lock key distinct from Svac.DomainCore.Persistence.
    // MigrationHostedService's ("SVAC_CO") and Svac.Identity.Persistence.IdentityMigrationHostedService's
    // ("SVAC_ID") own keys — three schemas, three locks, never contending.
    private const long AdvisoryLockKey = 0x53_56_41_43_5F_41_44; // ASCII "SVAC_AD" truncated to fit a long

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();

        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("AdminDbContext has no connection string configured.");

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

    [LoggerMessage(Level = LogLevel.Information, Message = "AdminMigrationHostedService: acquiring advisory lock {Key} before migrating schema admin")]
    private partial void LogAcquiringLock(long key);

    [LoggerMessage(Level = LogLevel.Information, Message = "AdminMigrationHostedService: applying pending migrations")]
    private partial void LogApplyingMigrations();

    [LoggerMessage(Level = LogLevel.Information, Message = "AdminMigrationHostedService: migrations applied cleanly")]
    private partial void LogMigrationsApplied();
}
