using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Ledger;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Purge;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The purge-completeness PIPELINE proof (SLICE_S1_CONTRACT.md §6: "Pipeline proof (playbook
/// purge-completeness lens): seed every store -&gt; run every class -&gt; assert zero residue or asserted
/// tombstone state + a purge_run row + an audit event per store. Committed as a test, not a claim.").
/// PurgeRegistryGateTests.cs already proves the REGISTRY declares a (store, class) cell for every real
/// EF entity — this file proves the EXECUTOR actually does what the registry promises, against real seed
/// data written through real service calls (ILedger/IEventStore/IQuotaService/IFieldEncryptor — never a
/// raw SQL INSERT faking a row into existence) and a real Postgres.
///
/// RUNNING THIS SUITE FOUND TWO LAUNCH-BLOCKING BUGS IN THE PIPELINE ITSELF, NOT JUST GAPS IN COVERAGE:
///
/// 1. <see cref="AccountDeletion_RunAgainstAFullySeededSubject_MatchesTheRegisteredPostureForEveryReachableStore"/>
///    observably CRASHES today: PurgePipeline.ExecuteOnLedgerEntries attempts
///    <c>row.UserId = sentinel</c> (an UPDATE) to sever the subject from a ledger_entries row, but the
///    append-only trigger on core.ledger_entries (AppendOnlyTriggerTests.
///    LedgerEntries_NeverPermitsUpdateOrDelete_ReversalIsTheOnlyCorrectionVerb — same DB, same trigger)
///    rejects EVERY UPDATE unconditionally: "reversal is the ONLY correction verb" (§1b). The pipeline's
///    own registered "Tombstone via user-severing" strategy for ledger_entries is structurally impossible
///    against the schema it is itself supposed to enforce — Run(AccountDeletion, ...) throws an unhandled
///    Npgsql.PostgresException the instant it reaches ledger_entries, returning NO report at all to the
///    caller (e.g. S3's future account-deletion flow) and leaving the purge partially applied (whatever
///    ran before ledger_entries in iteration order, e.g. events_ledger's tombstone, is already committed;
///    everything after is never attempted).
/// 2. Any registry cell declaring PurgeVerb.Delete against ANY of the six events_&lt;stream&gt; tables
///    (events_behavioral for every class, events_reputation for MinorPurge, events_audit for
///    RetentionExpiry, events_heatmap_provenance for StatutoryErasure/MinorPurge/RetentionExpiry) hits
///    the exact same wall: the append-only trigger permits INSERT and the one tombstone UPDATE
///    transition ONLY — DELETE is rejected in-database, unconditionally, by design (§2). PurgePipeline.
///    ExecuteOnEventStream's <c>case PurgeVerb.Delete: table.RemoveRange(rows)</c> branch can never
///    succeed against any event-stream table; it can only ever have been exercised in a test that mocks
///    the DbContext or uses a non-Postgres provider. <see cref="MinorPurge_OnEventsReputation_IsAHardDelete_DistinctFromAccountDeletionsTombstone"/>
///    and <see cref="RetentionExpiry_DeletesTheHighVolumeAndAuditStreams_LeavesLedgerUntouched"/> both
///    reproduce this directly.
///
/// Both are B2/§6 "purge-completeness" failures of the most severe kind: the registered posture is not
/// merely unverified, it is provably unexecutable against the real schema. The fix belongs to whoever
/// owns backend/domain-core/Svac.DomainCore/Purge/PurgePipeline.cs (out of this test-author's "own the
/// test tree only" scope) — most likely: (a) ledger_entries needs its own non-destructive severing path
/// that does NOT go through a raw UPDATE (e.g. a reversal-shaped correction row, or a narrower trigger
/// carve-out mirroring the events_&lt;stream&gt; tombstone transition), and (b) every Delete verb against
/// an events_&lt;stream&gt; table needs to become Tombstone (payload -&gt; NULL, flag set) to match what
/// the schema actually allows, OR the append-only trigger needs a documented, deliberate DELETE
/// carve-out scoped to the purge role — a decision with real audit/immutability-posture consequences
/// that reads like a Confusion Protocol stop (CLAUDE.md), not a one-line fix.
/// </summary>
public sealed class PurgeCompletenessTests : IAsyncLifetime
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

    private static readonly ActorRef SystemActor = new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);

    private static (ActorRef Actor, RequestContext Ctx) UserContext(string userId)
    {
        var actor = new ActorRef(OpaqueId.Parse(userId), ActorKind.User);
        var ctx = new RequestContext(actor, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", "fixture-purge-seed");
        return (actor, ctx);
    }

    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    /// <summary>Seeds one subject's data across every real, subject-scoped store S1 ships. Returns the ledger entry id and the consent event id (both needed post-purge, since the rows' subject linkage is severed/re-keyed by AccountDeletion/etc).</summary>
    private static async Task<(string EntryId, string ConsentEventId)> SeedSubjectAcrossStores(CoreDbContext db, string userId, string quotaKey)
    {
        var eventStore = new PostgresEventStore(db);
        var (userActor, userCtx) = UserContext(userId);

        // events_ledger + ledger_entries + ledger_balances, through the real ledger seam (4A-authorized).
        var ledger = new LedgerService(db, eventStore, new PolicyEngine(new PolicyTable()));
        var entryId = await ledger.Append(
            new LedgerEntry(userId, CrewId: null, EventType: "quest_complete", Points: 10, Xp: 10, Svac: 0, QuestId: null, EvidenceRef: null),
            SystemActor, SystemCtx("fixture-ledger"));

        // events_reputation / events_consent / events_audit / events_heatmap_provenance — no dedicated
        // service exists at S1 (§0: zero feature modules), so direct IEventStore.Append IS the real,
        // intended API for "the substrate's own internal verbs" (§3) to write these streams.
        await eventStore.Append(StreamType.Reputation, userId, "reputation.fixture", "{\"score\":1}", userCtx, ExpectedVersion.AnyVersion);
        var consentEvent = await eventStore.Append(StreamType.Consent, userId, "consent.granted", "{\"scope\":\"profile\"}", userCtx, ExpectedVersion.AnyVersion);
        await eventStore.Append(StreamType.Audit, userId, "audit.fixture", "{\"note\":\"seed\"}", userCtx, ExpectedVersion.AnyVersion);
        await eventStore.Append(StreamType.HeatmapProvenance, userId, "heatmap.fixture", "{\"cell\":\"c1\"}", userCtx, ExpectedVersion.AnyVersion);

        // events_behavioral, through the real one-door emitter (SubstrateBehavioralEmitter keys the
        // stream off ctx.Actor.Id.Value, so userActor.Id.Value must equal userId for the purge subject
        // lookup on StreamId to find it).
        var behavioral = new Svac.DomainCore.Behavioral.SubstrateBehavioralEmitter(eventStore);
        await behavioral.Emit("fixture.behavioral.event", "{}", userCtx);

        // quota_counters, through the real 10A verb — seeds the cap config row via a normal EF write
        // (never raw SQL) since the manifest loader requires a "consumer" field this fixture key has none of.
        db.ConfigEntries.Add(new ConfigEntryEntity
        {
            Key = $"quota.{quotaKey}.cap", Type = "int", ValueJson = "5", Scope = "ops",
            RequiresReason = false, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "sys_fixture",
        });
        await db.SaveChangesAsync();
        var quotaService = new Svac.DomainCore.Quota.QuotaService(db, new InlineConfigRegistry(db), Array.Empty<Svac.DomainCore.Contracts.Quota.ICapModifier>());
        var quotaCtx = new Svac.DomainCore.Contracts.Quota.QuotaContext(
            new Svac.DomainCore.Deterministic.ResetSpec(Svac.DomainCore.Deterministic.ResetCadence.Daily, Svac.DomainCore.Deterministic.WindowLocality.ConLocal),
            TimeZoneInfo.Utc, new TimeOnly(4, 0), DateTimeOffset.UtcNow);
        await quotaService.Consume(userActor, quotaKey, quotaCtx);

        return (entryId, consentEvent.EventId);
    }

    private static PurgePipeline BuildPipeline(CoreDbContext db)
    {
        var vault = new DevKeyringFieldKeyVault();
        return new PurgePipeline(
            db,
            new PostgresEventStore(db),
            new PurgeRegistry(),
            new AesFieldEncryptor(vault),
            new PolicyEngine(new PolicyTable()),
            vault);
    }

    [Fact]
    public async Task AccountDeletion_RunAgainstAFullySeededSubject_MatchesTheRegisteredPostureForEveryReachableStore()
    {
        var userId = FreshUserId();
        var quotaKey = $"fixture.quota.{Guid.NewGuid():N}";
        string entryId;
        string consentEventId;
        using (var seedDb = NewDb())
        {
            (entryId, consentEventId) = await SeedSubjectAcrossStores(seedDb, userId, quotaKey);
        }

        using var db = NewDb();
        var pipeline = BuildPipeline(db);
        var reports = await pipeline.Run(PurgeClass.AccountDeletion, new SubjectRef("user", userId), SystemActor, SystemCtx("fixture-purge-run"));

        // A purge_run row + an audit event per store touched (§6 pipeline proof) — checked once, up front,
        // for every store the registry declares against this class.
        var registry = new PurgeRegistry();
        var expectedStores = registry.Entries.Where(e => e.PurgeClass == PurgeClass.AccountDeletion).Select(e => e.StoreKey).ToHashSet();
        Assert.Equal(expectedStores, reports.Select(r => r.StoreKey).ToHashSet());
        foreach (var report in reports)
        {
            Assert.True(await db.PurgeRuns.AnyAsync(r => r.Id == report.RunId && r.StoreKey == report.StoreKey));
        }
        foreach (var report in reports)
        {
            var found = false;
            await foreach (var e in new PostgresEventStore(db).ReadStream(StreamType.Audit, report.RunId))
            {
                if (e.EventType == "purge.run") { found = true; }
            }
            Assert.True(found, $"expected an audit-stream 'purge.run' event for store {report.StoreKey}, run {report.RunId}");
        }

        var violations = new List<string>();

        // events_ledger: Tombstone — the row survives, payload nulled, flag set.
        var ledgerEvents = await ReadAll(db, StreamType.Ledger, userId);
        if (ledgerEvents.Count != 1 || !ledgerEvents[0].Tombstone || ledgerEvents[0].PayloadJson is not null)
        {
            violations.Add($"events_ledger: expected 1 tombstoned row with null payload, got {Describe(ledgerEvents)}");
        }

        // ledger_entries: Tombstone via user-severing (row survives under a redacted sentinel user id;
        // magnitudes are preserved for reconciliation).
        var entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == entryId);
        if (entry is null)
        {
            violations.Add("ledger_entries: the row was deleted outright — the registry declares Tombstone (survive under a sentinel), not Delete.");
        }
        else if (entry.UserId == userId)
        {
            violations.Add("ledger_entries: user_id was never severed from the subject after AccountDeletion.");
        }
        else if (entry.Points != 10 || entry.Xp != 10)
        {
            violations.Add($"ledger_entries: point/xp magnitudes were altered by the purge (points={entry.Points}, xp={entry.Xp}), expected them preserved.");
        }

        // events_reputation: Tombstone.
        var reputationEvents = await ReadAll(db, StreamType.Reputation, userId);
        if (reputationEvents.Count != 1 || !reputationEvents[0].Tombstone)
        {
            violations.Add($"events_reputation: expected 1 tombstoned row, got {Describe(reputationEvents)}");
        }

        // events_consent: Pseudonymize — row survives (looked up by EventId, invariant under the
        // subject re-key fix, never by stream_id=userId — pseudonymize now re-keys stream_id AWAY from
        // the raw subject id by design), actor_ref AND stream_id are both re-keyed away from the original.
        var consentRow = await db.EventsFor(StreamType.Consent).SingleOrDefaultAsync(e => e.EventId == consentEventId);
        if (consentRow is null)
        {
            violations.Add("events_consent: the seeded row is gone — the registry declares Pseudonymize (survive, re-keyed), not Delete.");
        }
        else
        {
            if (consentRow.ActorRef == new ActorRef(OpaqueId.Parse(userId), ActorKind.User).ToString())
            {
                violations.Add("events_consent: actor_ref was never pseudonymized — the original actor reference still reads back verbatim.");
            }
            if (consentRow.StreamId == userId)
            {
                violations.Add("events_consent: stream_id (the subject-scope column, §2) was never re-keyed — the row is still trivially linkable via the raw subject id.");
            }
            if (consentRow.LawfulBasis != "legal_obligation")
            {
                violations.Add($"events_consent: expected lawful_basis='legal_obligation' on the pseudonymized survivor (OQ-1, §15), got '{consentRow.LawfulBasis}'.");
            }
        }

        // events_behavioral: Delete — zero residue.
        var behavioralEvents = await ReadAll(db, StreamType.Behavioral, userId);
        if (behavioralEvents.Count != 0)
        {
            violations.Add($"events_behavioral: expected zero residue after Delete, got {behavioralEvents.Count} row(s)");
        }

        // events_audit: Tombstone — the SEEDED fixture row survives, tombstoned (independent of the
        // purge pipeline's own audit rows, which live on different streamIds keyed by run id).
        var auditEvents = await ReadAll(db, StreamType.Audit, userId);
        if (auditEvents.Count != 1 || !auditEvents[0].Tombstone)
        {
            violations.Add($"events_audit: expected the seeded fixture row to survive tombstoned, got {Describe(auditEvents)}");
        }

        // events_heatmap_provenance: NotApplicable for AccountDeletion — full-history default, untouched.
        var heatmapEvents = await ReadAll(db, StreamType.HeatmapProvenance, userId);
        if (heatmapEvents.Count != 1 || heatmapEvents[0].Tombstone || heatmapEvents[0].PayloadJson is null)
        {
            violations.Add($"events_heatmap_provenance: expected the seeded row untouched (full-history default), got {Describe(heatmapEvents)}");
        }

        // quota_counters: registry declares Delete ("hard delete the subject's counters", §6). A THIRD,
        // DISTINCT finding (currently unreached in a real run because the class-level doc comment's
        // finding #1 crashes first, at ledger_entries, before the loop ever reaches quota_counters — this
        // assertion documents what would ALSO need fixing once that earlier crash is resolved).
        // PurgePipeline.ExecuteOnQuotaCounters (Purge/PurgePipeline.cs) filters
        // `QuotaCounters.Where(q => q.ActorRef == subject.ResourceId)`, but QuotaService.Consume persists
        // ActorRef as `actor.ToString()` — the "{ActorKind}:{OpaqueId}" format (e.g. "User:usr_...") —
        // never the bare subject id every OTHER store's purge path matches on. A caller passing
        // SubjectRef("user", userId) exactly as this test (and every other store in this same run) does
        // will therefore purge every other store correctly but leave the subject's quota_counters rows
        // completely untouched. This assertion pins the CONTRACT'S promise, not the current behavior, so
        // it is expected to fail until that format mismatch is fixed.
        var remainingQuotaRows = await db.QuotaCounters.Where(q => q.QuotaKey == quotaKey).ToListAsync();
        if (remainingQuotaRows.Count != 0)
        {
            violations.Add(
                $"quota_counters: expected zero rows after AccountDeletion (registry: Delete), found {remainingQuotaRows.Count} — " +
                "see PurgePipeline.ExecuteOnQuotaCounters: it matches subject.ResourceId directly against QuotaCounterEntity.ActorRef, " +
                "which QuotaService.Consume actually persists as \"{ActorKind}:{OpaqueId}\" (e.g. \"User:usr_...\"), not the bare id " +
                "every other store's purge path uses for the same SubjectRef. Fix: either persist ActorRef bare in QuotaService, or " +
                "match on actor.ToString() in ExecuteOnQuotaCounters — the two sides of this contract must agree on one shape.");
        }

        // data_protection_keys / field_key_refs: no data was protected under this subject in this test
        // (CryptoShredTests.cs covers protect->shred->unprotect-fails in isolation); here we only assert
        // the pipeline reaches the pair at all for AccountDeletion, which the report-set assertion above
        // already covers (both keys are in expectedStores).

        Assert.Empty(violations);
    }

    [Fact]
    public async Task MinorPurge_OnEventsReputation_IsAHardDelete_DistinctFromAccountDeletionsTombstone()
    {
        // Proves the per-class verb differentiation is REAL, not just declared: the SAME store
        // (events_reputation) gets a softer verb (Tombstone) for AccountDeletion and a harder one
        // (Delete) for MinorPurge (SLICE_S1_CONTRACT.md §6 table).
        var userId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var eventStore = new PostgresEventStore(seedDb);
            var (_, userCtx) = UserContext(userId);
            await eventStore.Append(StreamType.Reputation, userId, "reputation.fixture", "{}", userCtx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        var pipeline = BuildPipeline(db);
        await pipeline.Run(PurgeClass.MinorPurge, new SubjectRef("user", userId), SystemActor, SystemCtx("fixture-minor-purge"));

        var remaining = await ReadAll(db, StreamType.Reputation, userId);
        Assert.Empty(remaining); // hard delete — zero residue, not a tombstone.
    }

    [Fact]
    public async Task RetentionExpiry_DeletesTheHighVolumeAndAuditStreams_LeavesLedgerUntouched()
    {
        var userId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var eventStore = new PostgresEventStore(seedDb);
            var (_, userCtx) = UserContext(userId);
            await eventStore.Append(StreamType.Behavioral, userId, "behavioral.fixture", "{}", userCtx, ExpectedVersion.AnyVersion);
            await eventStore.Append(StreamType.Audit, userId, "audit.fixture", "{}", userCtx, ExpectedVersion.AnyVersion);
            await eventStore.Append(StreamType.HeatmapProvenance, userId, "heatmap.fixture", "{}", userCtx, ExpectedVersion.AnyVersion);
            await eventStore.Append(StreamType.Ledger, userId, "ledger.fixture", "{}", userCtx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        var pipeline = BuildPipeline(db);
        await pipeline.Run(PurgeClass.RetentionExpiry, new SubjectRef("user", userId), SystemActor, SystemCtx("fixture-retention"));

        Assert.Empty(await ReadAll(db, StreamType.Behavioral, userId));
        // events_audit now carries a real age floor (Purge-F4, SECURITY_REVIEW_S1.md — PurgePipeline's
        // RetentionMinimumAge): a seconds-old audit row is nowhere near any conceivable statutory
        // retention window, so it survives untouched. This assertion previously blessed the pre-fix bug
        // (an unconditional, age-blind delete); PurgeCompletenessAdversaryTests.
        // RetentionExpiry_MustNotDeleteRowsYoungerThanAnyRetentionWindow pins the corrected behavior.
        var survivingAudit = await ReadAll(db, StreamType.Audit, userId);
        Assert.Single(survivingAudit);
        Assert.False(survivingAudit[0].Tombstone);
        Assert.Empty(await ReadAll(db, StreamType.HeatmapProvenance, userId));
        // events_ledger: RetentionExpiry is NotApplicable ("ledger is retained indefinitely") — untouched.
        var ledgerEvents = await ReadAll(db, StreamType.Ledger, userId);
        Assert.Single(ledgerEvents);
        Assert.False(ledgerEvents[0].Tombstone);
    }

    private static async Task<List<RecordedEvent>> ReadAll(CoreDbContext db, StreamType stream, string streamId)
    {
        var events = new List<RecordedEvent>();
        await foreach (var e in new PostgresEventStore(db).ReadStream(stream, streamId))
        {
            events.Add(e);
        }
        return events;
    }

    private static string Describe(List<RecordedEvent> events) =>
        events.Count == 0 ? "0 rows" : string.Join(", ", events.Select(e => $"[{e.EventType} tombstone={e.Tombstone} payloadNull={e.PayloadJson is null}]"));

    /// <summary>Thin IConfigRegistry adapter over a live CoreDbContext for seeding a quota cap without the manifest loader's consumer-field requirement.</summary>
    private sealed class InlineConfigRegistry(CoreDbContext db) : Svac.DomainCore.Contracts.Config.IConfigRegistry
    {
        public async Task<T> GetValue<T>(string key, CancellationToken ct = default)
        {
            var row = await db.ConfigEntries.SingleAsync(e => e.Key == key, ct);
            return System.Text.Json.JsonSerializer.Deserialize<T>(row.ValueJson)!;
        }

        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("fixture adapter: not exercised in this suite.");

        public Task<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>> ListEntries(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>>(Array.Empty<Svac.DomainCore.Contracts.Config.ConfigEntryView>());
    }
}
