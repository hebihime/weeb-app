using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// Append-only enforced IN-DATABASE, not by convention (SLICE_S1_CONTRACT.md §2, §10.1): a constraint
/// trigger permits INSERT always; UPDATE only the (tombstone=false-&gt;true, payload-&gt;NULL)
/// transition; DELETE never. Real Postgres via Testcontainers — the migration's raw-SQL trigger is
/// exactly what is under test, so an in-memory provider would prove nothing.
/// </summary>
public sealed class AppendOnlyTriggerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options;
        using var db = new CoreDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Insert_IsAlwaysPermitted()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options;
        using var db = new CoreDbContext(options);
        db.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).Add(FixtureRow("evt_insert_ok"));
        await db.SaveChangesAsync(); // must not throw
    }

    [Fact]
    public async Task Update_TombstoneTransition_IsPermittedExactlyOnce()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options;

        using (var db = new CoreDbContext(options))
        {
            db.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).Add(FixtureRow("evt_tombstone_once"));
            await db.SaveChangesAsync();
        }

        using (var db = new CoreDbContext(options))
        {
            var row = await db.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit)
                .SingleAsync(e => e.EventId == "evt_tombstone_once");
            row.Tombstone = true;
            row.PayloadJson = null;
            await db.SaveChangesAsync(); // the ONE sanctioned transition — must not throw
        }

        using (var db = new CoreDbContext(options))
        {
            var row = await db.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit)
                .SingleAsync(e => e.EventId == "evt_tombstone_once");
            row.Tombstone = false; // attempting to un-tombstone — the trigger must reject this
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.IsType<PostgresException>(ex.InnerException);
        }
    }

    [Fact]
    public async Task Update_AnyNonTombstoneChange_IsRejected()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options;

        using (var db = new CoreDbContext(options))
        {
            db.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).Add(FixtureRow("evt_no_edit"));
            await db.SaveChangesAsync();
        }

        using var db2 = new CoreDbContext(options);
        var row = await db2.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).SingleAsync(e => e.EventId == "evt_no_edit");
        row.EventType = "mutated.event.type"; // not a tombstone transition — must be rejected
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
    }

    [Fact]
    public async Task Delete_IsNeverPermitted()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options;

        using (var db = new CoreDbContext(options))
        {
            db.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).Add(FixtureRow("evt_no_delete"));
            await db.SaveChangesAsync();
        }

        using var db2 = new CoreDbContext(options);
        var row = await db2.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).SingleAsync(e => e.EventId == "evt_no_delete");
        db2.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit).Remove(row);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
    }

    [Fact]
    public async Task LedgerEntries_NeverPermitsUpdateOrDelete_ReversalIsTheOnlyCorrectionVerb()
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options;

        using (var db = new CoreDbContext(options))
        {
            db.LedgerEntries.Add(new LedgerEntryEntity
            {
                Id = "led_fixture_no_edit",
                UserId = "usr_fixture",
                EventType = "quest_complete",
                Points = 10,
                Xp = 10,
                Svac = 0,
                Region = "US",
                LawfulBasis = "conservative_global_v0",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var db2 = new CoreDbContext(options);
        var entry = await db2.LedgerEntries.SingleAsync(e => e.Id == "led_fixture_no_edit");
        entry.Points = 999; // data surgery — must have no policy entry AND be database-rejected
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.IsType<PostgresException>(ex.InnerException);
    }

    private static EventRow FixtureRow(string eventId) => new()
    {
        EventId = eventId,
        StreamId = "usr_fixture",
        Seq = 1,
        EventType = "fixture.created",
        PayloadJson = "{}",
        ActorRef = "sys_fixture",
        Region = "US",
        LawfulBasis = "conservative_global_v0",
        OccurredAt = DateTimeOffset.UtcNow,
        RecordedAt = DateTimeOffset.UtcNow,
    };
}
