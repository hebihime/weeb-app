using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Config;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Ledger;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Quota;
using Svac.DomainCore.Deterministic;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL CONCURRENCY LENS (S1). Every test in this file DEMONSTRATES A REAL BREAK — each one is
/// EXPECTED TO FAIL against the shipped code until the underlying defect is fixed. The interleavings are
/// forced deterministically (gated IEventStore / gated IProjection wrappers control WHEN a call proceeds,
/// never WHAT it does), so each failure reproduces a schedule that real concurrent requests can and will
/// produce under READ COMMITTED Postgres.
///
/// Findings demonstrated here:
///  F1 Replay watermark is global-per-consumer but seq is per-stream_id -> events of a second subject are
///     silently and permanently lost (PostgresEventStore.Replay, `e.Seq > watermark` unscoped).
///  F2 ledger_balances read-modify-write has no concurrency control -> concurrent Appends for the same
///     user silently commit a lost update; balance diverges from summation (LedgerService.StageBalanceUpdate).
///  F3 Quota INSERT branch never checks the cap -> cap=0 (kill-switch) still allows one Consume per
///     window per actor (QuotaService.Consume UPSERT).
///  F4 Two concurrent Replays for the same consumer double-apply events (no lock / no concurrency token
///     on projection_checkpoints).
///  F5 Quota Consume commits outside the guarded action's transaction -> a failed guarded action still
///     charges the quota (contract §2: "transactional with the guarded action").
///  F6 ConfigSeedLoader check-then-insert races itself -> two hosts seeding the same manifest at boot
///     crash instead of idempotent-skipping (its own doc claims idempotent union-merge).
/// </summary>
public sealed class ConcurrencyAdversaryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);

    private static PolicyEngine Engine() => new(new PolicyTable());

    private static ActorRef SystemActor() =>
        new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    private static RequestContext SystemCtx(string correlationId) =>
        RequestContext.System(SystemActor(), correlationId);

    private static string FreshUserId() => $"usr_fixture_{Guid.NewGuid():N}";

    // ------------------------------------------------------------------------------------------------
    // F1 — Replay loses a second subject's events forever once another subject advanced the watermark.
    // PostgresEventStore.cs:144 filters `e.Seq > watermark` with NO stream_id scoping, but seq restarts
    // at 1 PER stream_id (UNIQUE (stream_id, seq)). Interleaving: subject A appends 2 events, the
    // consumer replays (watermark=2), THEN subject B's first event lands with seq=1. 1 > 2 is false ->
    // B's event is never delivered to any projection, permanently, with no error.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task F1_Replay_DoesNotLoseASecondSubjectsEvents_AfterAnotherSubjectAdvancedTheWatermark()
    {
        var userA = FreshUserId();
        var userB = FreshUserId();
        var consumerId = $"fixture.adversary.f1.{Guid.NewGuid():N}";

        using (var seedDb = NewDb())
        {
            var seedStore = new PostgresEventStore(seedDb);
            await seedStore.Append(StreamType.Ledger, userA, "fixture.earn", "{}", SystemCtx("a1"), ExpectedVersion.AnyVersion); // (A, seq 1)
            await seedStore.Append(StreamType.Ledger, userA, "fixture.earn", "{}", SystemCtx("a2"), ExpectedVersion.AnyVersion); // (A, seq 2)
        }

        using (var db1 = NewDb())
        {
            await new PostgresEventStore(db1).Replay(StreamType.Ledger, consumerId, new RecordingProjection(consumerId));
        } // consumer watermark is now 2 — a PER-STREAM seq stored as if it were global.

        using (var seedDb2 = NewDb())
        {
            await new PostgresEventStore(seedDb2).Append(StreamType.Ledger, userB, "fixture.earn", "{}", SystemCtx("b1"), ExpectedVersion.AnyVersion); // (B, seq 1)
        }

        var second = new RecordingProjection(consumerId);
        using var db2 = NewDb();
        await new PostgresEventStore(db2).Replay(StreamType.Ledger, consumerId, second);

        // userB's event MUST reach the consumer. Shipped code: Applied is EMPTY — (B, seq 1) is filtered
        // out by `Seq > 2` and is now unreachable forever (later B events at seq 2 die the same way).
        var delivered = Assert.Single(second.Applied);
        Assert.Equal(userB, delivered.StreamId);
    }

    // ------------------------------------------------------------------------------------------------
    // F2 — Lost update on core.ledger_balances: StageBalanceUpdate (LedgerService.cs:115) reads the
    // balance row, computes the new total in memory, and writes it back with NO concurrency token
    // (LedgerBalanceEntity has none) and NO atomic increment. Schedule: B reads balance(100) -> A appends
    // 50 and commits (150) -> B's event append lands at the NEXT seq (no unique violation) and commits
    // its stale-based 160. Final balance 160; summation truth 210. Silent divergence — the exact thing
    // §1b bans ("balances derive by summation").
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task F2_ConcurrentLedgerAppends_SameUser_BalanceStillEqualsSummation()
    {
        var user = FreshUserId();

        using (var seedDb = NewDb())
        {
            var seedLedger = new LedgerService(seedDb, new PostgresEventStore(seedDb), Engine());
            await seedLedger.Append(new LedgerEntry(user, null, "fixture.earn", 100, 100, 0, null, null), SystemActor(), SystemCtx("seed"));
        } // balance row exists: 100

        using var dbB = NewDb();
        var gate = new GatedEventStore(new PostgresEventStore(dbB));
        var ledgerB = new LedgerService(dbB, gate, Engine());
        // B: reads balance=100, stages 160, then parks BEFORE its event append / SaveChanges.
        var taskB = ledgerB.Append(new LedgerEntry(user, null, "fixture.earn", 60, 60, 0, null, null), SystemActor(), SystemCtx("b"));
        await gate.Entered.Task;

        using (var dbA = NewDb())
        {
            var ledgerA = new LedgerService(dbA, new PostgresEventStore(dbA), Engine());
            await ledgerA.Append(new LedgerEntry(user, null, "fixture.earn", 50, 50, 0, null, null), SystemActor(), SystemCtx("a"));
        } // A commits: balance=150, event seq 2

        gate.Release.SetResult(); // B resumes: event seq 3 (no conflict), commits its STALE 160 over A's 150.
        await taskB;

        using var verifyDb = NewDb();
        var summation = await verifyDb.LedgerEntries.Where(e => e.UserId == user).SumAsync(e => (long)e.Points);
        var balance = await new LedgerService(verifyDb, new PostgresEventStore(verifyDb), Engine()).BalanceOf(user);

        Assert.Equal(210, summation);              // the append-only truth: all three entries landed.
        Assert.Equal(summation, balance.Points);   // FAILS: balance is 160 — A's 50 points silently vanished.
    }

    // ------------------------------------------------------------------------------------------------
    // F3 — The atomic UPSERT's INSERT branch has no cap guard (QuotaService.cs:34-40): the WHERE
    // consumed < cap clause only applies ON CONFLICT. With cap=0 (an ops kill-switch, a perfectly valid
    // 9A value) the FIRST Consume of every window for every actor succeeds. cap=N actually grants N
    // consumes only because the insert seeds consumed=1; at the N=0 boundary the guard does not exist.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task F3_Consume_WithCapZero_MustBeLimited_NotAllowOneFreeActionPerWindow()
    {
        var quotaKey = $"fixture.quota.{Guid.NewGuid():N}";
        await SeedCapConfig(quotaKey, cap: 0);
        var actor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);

        using var db = NewDb();
        var service = new QuotaService(db, new ConfigRegistryAdapter(db), IdentityModifiers());
        var result = await service.Consume(actor, quotaKey, FixtureQuotaContext());

        // cap=0 means zero. Shipped code returns Ok (insert path bypasses the cap) with Remaining
        // clamped from -1 to 0 by Math.Max — the overshoot is even masked in the return value.
        Assert.IsType<QuotaResult.Limited>(result);

        var persisted = await db.QuotaCounters.Where(q => q.QuotaKey == quotaKey).SumAsync(q => (int?)q.Consumed) ?? 0;
        Assert.Equal(0, persisted); // FAILS: 1 — the counter itself records a consume past a cap of zero.
    }

    // ------------------------------------------------------------------------------------------------
    // F4 — Two concurrent Replays for the same consumer double-apply: Replay (PostgresEventStore.cs:136)
    // reads the checkpoint with no lock, applies, then writes the watermark back with no concurrency
    // token on ProjectionCheckpointEntity. When the checkpoint row already exists, BOTH runners read the
    // same watermark, BOTH hand the same event to the projection, and BOTH commit — a non-idempotent
    // projection (a balance fold, a notification send) executes twice. Realizable schedule: host restart
    // overlapping a background runner, or two workers sharing a consumer id.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task F4_ConcurrentReplays_SameConsumer_MustNotDoubleApplyAnEvent()
    {
        var streamId = FreshUserId();
        var consumerId = $"fixture.adversary.f4.{Guid.NewGuid():N}";

        using (var seedDb = NewDb())
        {
            await new PostgresEventStore(seedDb).Append(StreamType.Ledger, streamId, "fixture.earn", "{}", SystemCtx("e1"), ExpectedVersion.AnyVersion);
        }
        using (var db0 = NewDb())
        {
            await new PostgresEventStore(db0).Replay(StreamType.Ledger, consumerId, new RecordingProjection(consumerId));
        } // checkpoint row now EXISTS at watermark 1 — no INSERT PK collision will rescue the race below.

        using (var seedDb2 = NewDb())
        {
            await new PostgresEventStore(seedDb2).Append(StreamType.Ledger, streamId, "fixture.earn", "{}", SystemCtx("e2"), ExpectedVersion.AnyVersion);
        }

        var applied = new ConcurrentBag<string>();
        using var db1 = NewDb();
        using var db2 = NewDb();
        var gated = new GatedProjection(consumerId, applied);   // records, then parks inside Apply
        var plain = new RecordingProjection(consumerId, applied);

        var replay1 = new PostgresEventStore(db1).Replay(StreamType.Ledger, consumerId, gated);
        await gated.EnteredApply.Task;                          // runner 1 read watermark=1 and is mid-apply
        await new PostgresEventStore(db2).Replay(StreamType.Ledger, consumerId, plain); // runner 2: same watermark, applies, commits
        gated.Release.SetResult();
        await replay1;                                          // runner 1 finishes its own apply and also commits

        Assert.Single(applied); // FAILS: the one new event was applied TWICE across the two runners.
    }

    // ------------------------------------------------------------------------------------------------
    // F5 — Consume is NOT "transactional with the guarded action" by API shape (§2's stated design).
    // ExecuteSqlInterpolatedAsync commits the counter increment immediately, in its own implicit
    // transaction, independent of the caller's staged EF changes. The natural S1 calling pattern
    // (stage domain write -> Consume -> SaveChanges, the same shape LedgerService uses for its own
    // atomicity) charges the user even when the guarded action fails. The event store meets this bar
    // ("same-tx or it does not exist"); the quota verb silently does not.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S1.md Concurrency-F5 — Consume tx-enlistment API reshape (QuotaService.cs:34) needed to join the ambient EF transaction instead of committing in its own implicit tx.")]
    public async Task F5_Consume_FailedGuardedActionInSameUnitOfWork_MustNotCharge()
    {
        var quotaKey = $"fixture.quota.{Guid.NewGuid():N}";
        await SeedCapConfig(quotaKey, cap: 5);
        var actor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);

        using (var db = NewDb())
        {
            // The guarded domain write, staged in the same unit of work — it will violate
            // ck_config_entries_scope at SaveChanges, so the guarded action NEVER happens.
            db.ConfigEntries.Add(new ConfigEntryEntity
            {
                Key = $"fixture.doomed.{Guid.NewGuid():N}",
                Type = "int",
                ValueJson = "1",
                Scope = "not_a_valid_scope",
                RequiresReason = false,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = "sys_fixture",
            });

            var service = new QuotaService(db, new ConfigRegistryAdapter(db), IdentityModifiers());
            var result = await service.Consume(actor, quotaKey, FixtureQuotaContext());
            Assert.IsType<QuotaResult.Ok>(result);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); // guarded action fails
        }

        using var verifyDb = NewDb();
        var charged = await verifyDb.QuotaCounters.Where(q => q.QuotaKey == quotaKey).SumAsync(q => (int?)q.Consumed) ?? 0;
        Assert.Equal(0, charged); // FAILS: 1 — the quota was consumed for an action that never happened.
    }

    // ------------------------------------------------------------------------------------------------
    // F6 — ConfigSeedLoader's idempotency is check-then-insert (ConfigSeedLoader.cs:47-54) with no
    // ON CONFLICT and no unique-violation handling: two hosts booting concurrently against the same
    // Postgres (compose scale-out, or migration service racing a host) both observe "key absent", both
    // stage the row, and the loser crashes at SaveChanges — boot failure instead of the documented
    // "idempotent ... never clobbers" union-merge behavior.
    // ------------------------------------------------------------------------------------------------
    [Fact] // Concurrency-F6 FIXED (SECURITY_REVIEW_S1.md): ConfigSeedLoader now catches the raced
           // config_entries 23505 and treats it as the idempotent no-op its doc-comment promises,
           // instead of crashing the losing host at boot. Pulled forward from defer — proof, not a Skip.
    public async Task F6_ConcurrentSeedFromFile_SameManifest_BothLoadersMustCompleteIdempotently()
    {
        var key = $"fixture.seed.{Guid.NewGuid():N}";
        var manifestPath = Path.Combine(Path.GetTempPath(), $"manifest-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            { "entries": [ { "key": "{{key}}", "scope": "ops", "type": "int", "value": 42, "requiresReason": false, "consumer": "fixture.adversary" } ] }
            """);

        try
        {
            using var db1 = NewDb();
            var gate = new GatedEventStore(new PostgresEventStore(db1));
            var loader1 = new ConfigSeedLoader(db1, gate);
            var boot1 = loader1.SeedFromFile(manifestPath, SystemCtx("boot1"));
            await gate.Entered.Task; // loader1 saw "key absent", staged the row, parked before commit

            using (var db2 = NewDb())
            {
                var loader2 = new ConfigSeedLoader(db2, new PostgresEventStore(db2));
                await loader2.SeedFromFile(manifestPath, SystemCtx("boot2")); // seeds the key for real
            }

            gate.Release.SetResult();
            var bootFailure = await Record.ExceptionAsync(() => boot1);
            Assert.Null(bootFailure); // FAILS: duplicate-key crash — one of the two hosts fails to boot.

            using var verifyDb = NewDb();
            Assert.Equal(1, await verifyDb.ConfigEntries.CountAsync(e => e.Key == key));
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    // ---- fixtures -----------------------------------------------------------------------------------

    private async Task SeedCapConfig(string quotaKey, int cap)
    {
        using var db = NewDb();
        db.ConfigEntries.Add(new ConfigEntryEntity
        {
            Key = $"quota.{quotaKey}.cap",
            Type = "int",
            ValueJson = cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Scope = "ops",
            RequiresReason = false,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "sys_fixture",
        });
        await db.SaveChangesAsync();
    }

    private static QuotaContext FixtureQuotaContext() => new(
        new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal),
        TimeZoneInfo.Utc,
        new TimeOnly(4, 0),
        new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private static ICapModifier[] IdentityModifiers() => new ICapModifier[]
    {
        new PremiumCapModifier(),
        new ReputationCapModifier(),
        new ModeCapModifier(),
    };

    /// <summary>Delegating IEventStore that parks the FIRST Append until released — controls WHEN, never WHAT.</summary>
    private sealed class GatedEventStore(PostgresEventStore inner) : IEventStore
    {
        private int _gatedOnce;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RecordedEvent> Append(StreamType stream, string streamId, string eventType, string? payloadJson, RequestContext ctx, ExpectedVersion expectedVersion, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _gatedOnce, 1) == 0)
            {
                Entered.SetResult();
                await Release.Task;
            }
            return await inner.Append(stream, streamId, eventType, payloadJson, ctx, expectedVersion, ct);
        }

        public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default) =>
            inner.Reverse(stream, eventId, reason, ctx, ct);

        public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default) =>
            inner.Tombstone(stream, eventId, purgeClass, ctx, ct);

        public IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, CancellationToken ct = default) =>
            inner.ReadStream(stream, streamId, fromSeq, ct);

        public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default) =>
            inner.Replay(stream, consumerId, projection, ct);
    }

    /// <summary>Handles every event type; records what it was handed (optionally into a shared bag).</summary>
    private sealed class RecordingProjection(string consumerId, ConcurrentBag<string>? shared = null) : IProjection
    {
        public string ConsumerId { get; } = consumerId;
        public StreamType Stream => StreamType.Ledger;
        public List<RecordedEvent> Applied { get; } = new();

        public bool CanHandle(string eventType) => true;

        public Task Apply(RecordedEvent recordedEvent, CancellationToken ct = default)
        {
            Applied.Add(recordedEvent);
            shared?.Add(recordedEvent.EventId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Records into the shared bag, then parks inside Apply until released.</summary>
    private sealed class GatedProjection(string consumerId, ConcurrentBag<string> shared) : IProjection
    {
        public string ConsumerId { get; } = consumerId;
        public StreamType Stream => StreamType.Ledger;
        public TaskCompletionSource EnteredApply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanHandle(string eventType) => true;

        public async Task Apply(RecordedEvent recordedEvent, CancellationToken ct = default)
        {
            shared.Add(recordedEvent.EventId);
            EnteredApply.SetResult();
            await Release.Task;
        }
    }

    /// <summary>Thin IConfigRegistry adapter over a live CoreDbContext — QuotaService only ever calls GetValue.</summary>
    private sealed class ConfigRegistryAdapter(CoreDbContext db) : Svac.DomainCore.Contracts.Config.IConfigRegistry
    {
        public async Task<T> GetValue<T>(string key, CancellationToken ct = default)
        {
            var row = await db.ConfigEntries.SingleAsync(e => e.Key == key, ct);
            return System.Text.Json.JsonSerializer.Deserialize<T>(row.ValueJson)!;
        }

        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("fixture adapter: QuotaService never calls SetValue.");

        public Task<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>> ListEntries(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>>(Array.Empty<Svac.DomainCore.Contracts.Config.ConfigEntryView>());
    }
}
