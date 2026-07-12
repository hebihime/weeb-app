using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;

namespace Svac.Identity.Persistence;

/// <summary>
/// A short-lived, FRESH connection+transaction spanning schema `identity` (<see cref="IdentityDbContext"/>)
/// and schema `core` (a locally-constructed <see cref="CoreDbContext"/>/<see cref="PostgresEventStore"/>)
/// for the handful of S3 mutations that must be atomic across BOTH (SLICE_S3_CONTRACT.md §1c
/// signup/complete's account+consent+audit+behavioral+session write; the refresh-reuse family-revocation's
/// session/family revoke + audit event). SLICE_S1_CONTRACT.md 1A: one physical Postgres — schema
/// boundaries do not stop one transaction from spanning both; the DI-scoped IdentityDbContext and
/// CoreDbContext are two SEPARATE ADO.NET connections by default (each AddDbContext registration owns its
/// own pooled connection), so true cross-schema atomicity needs its own connection shared by fresh
/// instances of both contexts, not the ambient DI-scoped ones.
///
/// Deliberately never calls <see cref="IEventStore.Replay"/> inside this scope: <c>PostgresEventStore.
/// Replay</c> unconditionally calls <c>Database.BeginTransactionAsync</c> internally (its own checkpoint-row
/// locking), which throws against an already-ambient transaction on the same context. Callers append
/// consent/audit/behavioral events directly via <see cref="Events"/> (never <c>IConsentLedgerWriter.Record</c>,
/// whose convenience Replay trigger would hit exactly that conflict) and re-trigger the identity
/// projections' Replay AFTER this scope commits, against the ordinary DI-scoped <c>IEventStore</c> (no
/// ambient transaction at that point — Postgres MVCC means that second, separate connection reads the
/// just-committed rows normally).
/// </summary>
public sealed class IdentityAtomicScope : IAsyncDisposable
{
    public IdentityDbContext Db { get; }

    public IEventStore Events { get; }

    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly CoreDbContext _coreDb;
    private bool _completed;

    private IdentityAtomicScope(NpgsqlConnection connection, NpgsqlTransaction transaction, IdentityDbContext db, CoreDbContext coreDb)
    {
        _connection = connection;
        _transaction = transaction;
        Db = db;
        _coreDb = coreDb;
        Events = new PostgresEventStore(coreDb);
    }

    /// <param name="connectionString">Read from the DI-scoped IdentityDbContext via <c>Database.GetConnectionString()</c> by the caller — both DbContexts are configured against the SAME physical database (1A), so any valid connection string for one is a valid connection string for the other's schema too.</param>
    public static async Task<IdentityAtomicScope> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var transaction = await connection.BeginTransactionAsync(ct);

        var db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
        var coreDb = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(connection).Options);

        await db.Database.UseTransactionAsync(transaction, ct);
        await coreDb.Database.UseTransactionAsync(transaction, ct);

        return new IdentityAtomicScope(connection, transaction, db, coreDb);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        await _transaction.CommitAsync(ct);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_completed)
        {
            return;
        }
        await _transaction.RollbackAsync(ct);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Connection may already be broken by whatever caused the caller to skip an explicit
                // Commit/Rollback — disposal must never throw a second exception over the first.
            }
        }

        await Db.DisposeAsync();
        await _coreDb.DisposeAsync();
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
