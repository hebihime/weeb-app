using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Ledger;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// Regression test for the functional break SECURITY_REVIEW_S1.md filed against LedgerService.Reverse
/// (Concurrency out-of-lens observation): the shipped code wrote <c>Points = -original.Points</c>, which
/// always violates <c>ck_ledger_entries_points_nonneg</c> (CoreDbContext.cs: "points &gt;= 0") the moment
/// original.Points is greater than zero — Reverse could NEVER succeed against real Postgres. Fixed by
/// mirroring the original's positive magnitudes on the reversal row (ReversalOf is the direction signal)
/// while the balance projection still folds the movement OUT via the original's own values.
/// </summary>
public sealed class LedgerReversalRegressionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static readonly ActorRef SystemActor = new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);
    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    [Fact]
    public async Task Reverse_OfAPositiveEarnEntry_SucceedsAndRestoresTheOriginalBalance()
    {
        var user = FreshUserId();
        using var db = NewDb();
        var ledger = new LedgerService(db, new PostgresEventStore(db), new PolicyEngine(new PolicyTable()));

        var entryId = await ledger.Append(
            new LedgerEntry(user, CrewId: null, EventType: "quest_complete", Points: 50, Xp: 50, Svac: 0, QuestId: null, EvidenceRef: null),
            SystemActor, SystemCtx("regression-seed"));

        var before = await ledger.BalanceOf(user);
        Assert.Equal(50, before.Points);

        // Regression proof: this must not throw ck_ledger_entries_points_nonneg.
        var ex = await Record.ExceptionAsync(() => ledger.Reverse(entryId, SystemActor, "regression test", SystemCtx("regression-reverse")));
        Assert.Null(ex);

        var after = await ledger.BalanceOf(user);
        Assert.Equal(0, after.Points);
        Assert.Equal(0, after.Xp);

        var reversalRow = await db.LedgerEntries.SingleAsync(e => e.ReversalOf == entryId);
        Assert.True(reversalRow.Points >= 0, "the reversal row must never violate ck_ledger_entries_points_nonneg");
        Assert.Equal(reversalRow.Points, reversalRow.Xp); // ck_ledger_entries_xp_eq_points
    }

    [Fact]
    public async Task Reverse_OfASinkPurchase_CreditsBackTheSpentSvac()
    {
        var user = FreshUserId();
        using var db = NewDb();
        var ledger = new LedgerService(db, new PostgresEventStore(db), new PolicyEngine(new PolicyTable()));

        var entryId = await ledger.Append(
            new LedgerEntry(user, CrewId: null, EventType: "sink_purchase", Points: 0, Xp: 0, Svac: -100, QuestId: null, EvidenceRef: null),
            SystemActor, SystemCtx("regression-seed"));

        var before = await ledger.BalanceOf(user);
        Assert.Equal(-100, before.Svac);

        await ledger.Reverse(entryId, SystemActor, "refund", SystemCtx("regression-reverse"));

        var after = await ledger.BalanceOf(user);
        Assert.Equal(0, after.Svac);
    }
}
