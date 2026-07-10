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
/// ADVERSARIAL purge-completeness lens (SLICE_S1_CONTRACT.md §6: "13A classes vs seeded stores;
/// derivatives inherit lifetime"). Every test here is a DEMONSTRATED break: it seeds real data through
/// the real seams, runs the real pipeline against real Postgres, and asserts the posture the contract
/// promises — each currently FAILS because PurgePipeline does not deliver that posture. These are
/// deliberately distinct from PurgeCompletenessTests.cs, which asserts the pipeline's CURRENT behavior
/// and thereby blesses several of the leaks below (e.g. it reads the "pseudonymized" consent row back
/// BY the purged subject's id and calls that success).
///
/// Findings pinned by this file:
///  A1  ledger_balances: registered Tombstone for AccountDeletion ("balances rebuilt by Replay",
///      PurgeRegistry.cs:31) but PurgePipeline.ExecuteVerb falls through `_ =&gt; 0`
///      (PurgePipeline.cs:97) — the derivative projection row keyed by the RAW user_id survives every
///      purge class, and the purge_run receipt reports verb=Tombstone rows_affected=0 as a success.
///  A2  events_consent Pseudonymize re-keys ONLY actor_ref (PurgePipeline.cs:137-143); stream_id — the
///      DDL-designated "subject scope" column — still carries the erased subject's raw id, and the
///      append-only trigger's pseudonymize branch structurally PINS stream_id to OLD
///      (20260710085111_InitialCore.cs:470), so "pseudonymize subject (irreversible re-key)" cannot
///      ever re-key the subject. Payload also survives verbatim.
///  A3  Crypto-shred is purpose-GLOBAL: AesFieldEncryptor.Shred ignores subjectScope
///      (AesFieldEncryptor.cs:58-59), so ONE subject's AccountDeletion destroys EVERY subject's key
///      material for all four purposes — cross-subject data destruction, run twice per purge (once for
///      data_protection_keys, once for field_key_refs). The two registered stores' own rows
///      (field_key_refs.retired_at, data_protection_keys) are never touched by their registered verb.
///  A4  RetentionExpiry has no age predicate anywhere in IPurgePipeline: Run(RetentionExpiry, subject)
///      hard-deletes ALL of the subject's rows regardless of recorded_at, including events_audit rows
///      the registry itself says fall only "once the age threshold is reached" (PurgeRegistry.cs:69).
///      (No scheduler exists either: domain-core.config.json:42 declares consumer "purge pipeline
///      scheduler" for core.purge.sweep_interval_minutes; no such hosted service is in the tree.)
///  A5  purge_runs.subject_ref persists the raw subject id forever (PurgePipeline.cs:64) while the
///      registry exempts purge_runs from every subject-scoped class as "non-PII operational metadata"
///      (PurgeRegistry.cs:116) — the purge receipts are themselves an unregistered PII store, and the
///      registered reason is falsified by the pipeline's own write.
/// </summary>
public sealed class PurgeCompletenessAdversaryTests : IAsyncLifetime
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

    private static (ActorRef Actor, RequestContext Ctx) UserContext(string userId)
    {
        var actor = new ActorRef(OpaqueId.Parse(userId), ActorKind.User);
        var ctx = new RequestContext(actor, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", "adversary-purge-seed");
        return (actor, ctx);
    }

    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    private static PurgePipeline BuildPipeline(CoreDbContext db, IFieldEncryptor? encryptor = null) => new(
        db,
        new PostgresEventStore(db),
        new PurgeRegistry(),
        encryptor ?? new AesFieldEncryptor(new DevKeyringFieldKeyVault()),
        new PolicyEngine(new PolicyTable()),
        new DevKeyringFieldKeyVault()); // pseudonymization's HMAC secret only — independent of the crypto-shred encryptor's own vault.

    // ------------------------------------------------------------------------------------------------
    // A1 — derivative inherits lifetime: the balance PROJECTION must not outlive the purged source.
    // Registry: ledger_balances / AccountDeletion = Tombstone ("balances rebuilt by Replay").
    // Reality: PurgePipeline.ExecuteVerb has no ledger_balances arm (`_ => 0`), nothing tombstones the
    // row, nothing triggers the Replay rebuild — the subject's points/xp/svac survive keyed by the RAW
    // user id after the events were tombstoned and ledger_entries.user_id was severed. The purge_run
    // receipt for the store nonetheless records verb=Tombstone as if the posture were applied.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task AccountDeletion_LeavesNoLedgerBalanceRowKeyedByTheRawSubjectId()
    {
        var userId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var ledger = new LedgerService(seedDb, new PostgresEventStore(seedDb), new PolicyEngine(new PolicyTable()));
            await ledger.Append(
                new LedgerEntry(userId, CrewId: null, EventType: "quest_complete", Points: 10, Xp: 10, Svac: 0, QuestId: null, EvidenceRef: null),
                SystemActor, SystemCtx("adversary-a1-seed"));
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(PurgeClass.AccountDeletion, new SubjectRef("user", userId), SystemActor, SystemCtx("adversary-a1-run"));

        // The registered posture: the derivative is severed from the subject (tombstoned / rebuilt by
        // Replay over tombstoned events). ANY row still keyed by the raw subject id is residue.
        var residue = await db.LedgerBalances.Where(b => b.UserId == userId).ToListAsync();
        Assert.True(residue.Count == 0,
            $"ledger_balances: {residue.Count} row(s) still keyed by the purged subject's RAW user_id " +
            $"(points={residue.FirstOrDefault()?.Points}, xp={residue.FirstOrDefault()?.Xp}) after AccountDeletion — " +
            "the registry declares Tombstone (\"balances rebuilt by Replay\", PurgeRegistry.cs:31) but " +
            "PurgePipeline.ExecuteVerb (PurgePipeline.cs:97) silently no-ops this store and still writes a purge_run receipt for it.");
    }

    // ------------------------------------------------------------------------------------------------
    // A2 — statutory erasure must not leave the subject's raw identifier on the consent stream.
    // "Pseudonymize subject (irreversible re-key)" — the subject key IS stream_id ("subject scope",
    // §2 DDL). The pipeline re-keys only actor_ref; the trigger's pseudonymize branch structurally
    // forbids a stream_id change; payload survives verbatim. The row remains trivially linkable:
    // SELECT * FROM core.events_consent WHERE stream_id = '<erased user>' still returns it.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task StatutoryErasure_LeavesNoConsentRowLinkableByTheRawSubjectId()
    {
        var userId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var (_, userCtx) = UserContext(userId);
            await new PostgresEventStore(seedDb).Append(
                StreamType.Consent, userId, "consent.granted", "{\"scope\":\"profile\"}", userCtx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(PurgeClass.StatutoryErasure, new SubjectRef("user", userId), SystemActor, SystemCtx("adversary-a2-run"));

        var linkable = await db.EventsFor(StreamType.Consent).Where(e => e.StreamId == userId).ToListAsync();
        Assert.True(linkable.Count == 0,
            $"events_consent: {linkable.Count} row(s) still directly linkable to the erased subject via stream_id='{userId}' " +
            $"(payload intact: {linkable.FirstOrDefault()?.PayloadJson}) after StatutoryErasure — " +
            "PurgePipeline.ExecuteOnEventStream's Pseudonymize arm (PurgePipeline.cs:137-143) re-keys ONLY actor_ref; " +
            "the subject-scope column stream_id is never re-keyed (and the append-only trigger's pseudonymize transition, " +
            "20260710085111_InitialCore.cs:470, structurally pins it). Retaining the primary identifier is not pseudonymization.");
    }

    // ------------------------------------------------------------------------------------------------
    // A3 — a purge is SUBJECT-scoped: deleting account A must never destroy account B's data.
    // AesFieldEncryptor.Shred(purpose, subjectScope) discards subjectScope and destroys the ONE shared
    // purpose-wide key; PurgePipeline.ExecuteCryptoShred loops EVERY purpose on EVERY AccountDeletion.
    // So the first account deletion after S10 ships field encryption crypto-shreds every user's
    // special-category data platform-wide.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task AccountDeletion_OfSubjectA_MustNotDestroySubjectBsProtectedData()
    {
        var subjectA = FreshUserId();
        var encryptor = new AesFieldEncryptor(new DevKeyringFieldKeyVault()); // one keyring = one "vault", shared by pipeline and victim

        // Subject B protects data BEFORE subject A's account deletion runs.
        var subjectB = FreshUserId();
        var subjectBBlob = await encryptor.Protect(FieldEncryptionPurpose.SpecialCategory, new SubjectScope(subjectB), "subject-B-special-category-data");

        using var db = NewDb();
        await BuildPipeline(db, encryptor).Run(PurgeClass.AccountDeletion, new SubjectRef("user", subjectA), SystemActor, SystemCtx("adversary-a3-run"));

        // Subject B's data must still unprotect: A's purge is scoped to A.
        var roundTripped = await encryptor.Unprotect(FieldEncryptionPurpose.SpecialCategory, subjectBBlob);
        Assert.Equal("subject-B-special-category-data", roundTripped);
    }

    // ------------------------------------------------------------------------------------------------
    // A4 — RetentionExpiry is an AGE-gated class ("hard delete once the age threshold is reached",
    // PurgeRegistry.cs:69; "statutory retention period governs"). The pipeline has no age parameter and
    // no recorded_at predicate: it deletes EVERYTHING the subject has, including an audit row recorded
    // milliseconds ago — premature destruction of the accountability record OQ-1 posture (a) exists to
    // preserve.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task RetentionExpiry_MustNotDeleteRowsYoungerThanAnyRetentionWindow()
    {
        var userId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var (_, userCtx) = UserContext(userId);
            var store = new PostgresEventStore(seedDb);
            await store.Append(StreamType.Audit, userId, "audit.staff_action", "{\"note\":\"fresh, inside every statutory window\"}", userCtx, ExpectedVersion.AnyVersion);
            await store.Append(StreamType.Behavioral, userId, "behavioral.fresh", "{}", userCtx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(PurgeClass.RetentionExpiry, new SubjectRef("user", userId), SystemActor, SystemCtx("adversary-a4-run"));

        // Rows recorded seconds ago are inside EVERY conceivable retention window; an age-gated class
        // must leave them standing. (The behavioral row's window is an S5-set value, but no value makes
        // a seconds-old row expired; the audit row's window is statutory.)
        var freshAuditSurvives = await db.EventsFor(StreamType.Audit).AnyAsync(e => e.StreamId == userId);
        Assert.True(freshAuditSurvives,
            "events_audit: a seconds-old audit row was hard-deleted by RetentionExpiry — " +
            "PurgePipeline.Run has no age threshold anywhere in its signature or its verb execution " +
            "(PurgePipeline.cs:101-147 filters on StreamId only), so the \"statutory retention period governs\" " +
            "registration (PurgeRegistry.cs:69) is unimplementable: every RetentionExpiry run is total, immediate deletion.");
    }

    // ------------------------------------------------------------------------------------------------
    // A5 — the purge receipts are themselves a subject-keyed store. purge_runs.subject_ref persists
    // "user:usr_..." verbatim and the registry exempts purge_runs from every subject-scoped class as
    // "non-PII operational metadata" — so after AccountDeletion the erased subject's identifier lives
    // forever in a store no purge class will ever touch. The receipt must carry a severed/pseudonymized
    // subject reference, or purge_runs must stop claiming to be non-PII.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task PurgeReceipts_MustNotRetainTheRawSubjectIdentifierOfAnErasedSubject()
    {
        var userId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var (_, userCtx) = UserContext(userId);
            await new PostgresEventStore(seedDb).Append(StreamType.Behavioral, userId, "behavioral.fixture", "{}", userCtx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(PurgeClass.AccountDeletion, new SubjectRef("user", userId), SystemActor, SystemCtx("adversary-a5-run"));

        var receiptsCarryingRawId = await db.PurgeRuns.Where(r => r.SubjectRef.Contains(userId)).CountAsync();
        Assert.True(receiptsCarryingRawId == 0,
            $"purge_runs: {receiptsCarryingRawId} receipt row(s) permanently retain the erased subject's raw id in subject_ref " +
            "(PurgePipeline.cs:64) while the registry registers purge_runs as \"non-PII operational metadata\" with " +
            "NotApplicable for every subject-scoped class (PurgeRegistry.cs:116) — an identifier of the erased subject " +
            "survives, unregistered as personal data, in a store no purge class will ever sever.");
    }
}
