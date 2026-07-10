using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Purge;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL LENS: PII / residency + special-category (encryption, region/lawful-basis).
/// Each test asserts the S1 CONTRACT'S promise and is RED against the shipped code, demonstrating a
/// concrete break with inputs -> wrong result. Findings, most severe first:
///
///   F1 (CRITICAL, special-category encryption blast radius): AesFieldEncryptor.Shred ignores its
///      SubjectScope and destroys the WHOLE purpose key (AesFieldEncryptor.cs:58-59). Because every
///      subject's special-category blob is wrapped under one shared per-purpose key, crypto-shredding
///      ONE subject (a single GDPR erasure, wired live through PurgePipeline.ExecuteCryptoShred today)
///      makes EVERY other subject's special-category plaintext permanently unrecoverable.
///
///   F2 (HIGH, lawful-basis): the NOT NULL `lawful_basis` column (contract §1b/§2 "resolved from a code
///      table keyed (stream/store, event_type, region)") is populated with the config VARIANT KEY
///      ("conservative_global_v0"), not a resolved lawful basis. PostgresEventStore.cs:54 /
///      LedgerService.cs:44,85. A variant identifier is not a lawful basis; no resolver exists.
///
///   F3 (HIGH, OQ-1 residency posture unreachable): the ratified OQ-1 posture (contract §15) requires
///      post-account-deletion consent/audit rows to carry lawful_basis='legal_obligation'. The
///      pseudonymize path (PurgePipeline.PseudonymizeRef) re-keys ONLY actor_ref, and the append-only
///      trigger's pseudonymize transition pins lawful_basis to its OLD value — so the mandated basis
///      can NEVER be recorded on the surviving record.
///
///   F4 (MEDIUM, residency provenance): contract §1b — "System-actor writes inherit the SUBJECT's
///      region (a purge run on a German user's data is EU-scoped work)". PurgePipeline.Run stamps its
///      purge.run audit events with the CALLER's ctx region (ZZ for the system scheduler), and has no
///      path to the subject's region at all, so a German user's deletion audit trail is recorded as
///      region 'ZZ', losing residency provenance.
/// </summary>
public sealed class PiiResidencyLensTests : IAsyncLifetime
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

    private static readonly ActorRef SystemActor =
        new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);

    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    private static (ActorRef Actor, RequestContext Ctx) UserContext(string userId, RegionCode region)
    {
        var actor = new ActorRef(OpaqueId.Parse(userId), ActorKind.User);
        var ctx = new RequestContext(actor, region, RegionSource.Signup, LawfulBasisVariant.ConservativeGlobalV0, "en", "lens-seed");
        return (actor, ctx);
    }

    // ---------------------------------------------------------------------------------------------
    // F1 — special-category crypto-shred blast radius. Pure in-memory (no key material in Postgres, §2).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task F1_CryptoShreddingOneSubject_MustNotDestroyAnotherSubjectsSpecialCategoryData()
    {
        var vault = new DevKeyringFieldKeyVault();
        var encryptor = new AesFieldEncryptor(vault);

        // Two different subjects both have special-category data protected.
        var alice = await encryptor.Protect(FieldEncryptionPurpose.SpecialCategory, new SubjectScope("usr_alice"), "alice-HIV-status");
        var bob = await encryptor.Protect(FieldEncryptionPurpose.SpecialCategory, new SubjectScope("usr_bob"), "bob-HIV-status");

        // Alice exercises her right to erasure -> crypto-shred scoped to ALICE ONLY.
        await encryptor.Shred(FieldEncryptionPurpose.SpecialCategory, new SubjectScope("usr_alice"));

        // Alice's data must be gone.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => encryptor.Unprotect(FieldEncryptionPurpose.SpecialCategory, alice));

        // BOB'S data must STILL decrypt — his consent was never withdrawn. Shipped code shares one
        // per-purpose key, so this Unprotect throws: Alice's erasure destroyed Bob's data. RED.
        var bobRecovered = await encryptor.Unprotect(FieldEncryptionPurpose.SpecialCategory, bob);
        Assert.Equal("bob-HIV-status", bobRecovered);
    }

    // ---------------------------------------------------------------------------------------------
    // F2 — lawful_basis carries the config variant key, not a resolved lawful basis.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task F2_AppendedEvent_LawfulBasisColumn_IsAResolvedBasis_NotTheConfigVariantKey()
    {
        var userId = FreshUserId();
        using var db = NewDb();
        var store = new PostgresEventStore(db);
        var (_, ctx) = UserContext(userId, new RegionCode("DE", null));

        await store.Append(StreamType.Consent, userId, "consent.granted", "{\"scope\":\"profile\"}", ctx, ExpectedVersion.AnyVersion);

        var row = await db.EventsFor(StreamType.Consent).SingleAsync(e => e.StreamId == userId);

        // The variant KEY selects WHICH code table resolves the basis (§1b); it is not itself a basis.
        // Persisting it verbatim into the lawful_basis column means every row's residency ledger reads
        // "conservative_global_v0" instead of e.g. "consent" / "legitimate_interest" / "legal_obligation".
        Assert.NotEqual(LawfulBasisVariant.ConservativeGlobalV0.Key, row.LawfulBasis);
    }

    // ---------------------------------------------------------------------------------------------
    // F3 — OQ-1: pseudonymized consent survivor must carry lawful_basis='legal_obligation' (§15).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task F3_ConsentSurvivingAccountDeletion_CarriesLegalObligationLawfulBasis()
    {
        var userId = FreshUserId();
        string seededEventId;
        using (var seed = NewDb())
        {
            var store = new PostgresEventStore(seed);
            var (_, ctx) = UserContext(userId, new RegionCode("DE", null));
            var seeded = await store.Append(StreamType.Consent, userId, "consent.granted", "{\"scope\":\"profile\"}", ctx, ExpectedVersion.AnyVersion);
            seededEventId = seeded.EventId;
        }

        using var db = NewDb();
        var pipeline = BuildPipeline(db);
        await pipeline.Run(PurgeClass.AccountDeletion, new SubjectRef("user", userId), SystemActor, SystemCtx("lens-purge"));

        // Looked up by EventId (invariant under the Purge-F2/MinorProt-F3 subject re-key fix), not by
        // stream_id=userId — the pseudonymize verb now re-keys stream_id AWAY from the raw subject id,
        // which is the whole point of the fix this same purge run exercises.
        var consent = await db.EventsFor(StreamType.Consent).SingleAsync(e => e.EventId == seededEventId);

        // OQ-1 ratified posture (a): the surviving record's basis flips to the defensible-record basis.
        // The pseudonymize path never touches lawful_basis and the trigger forbids changing it -> RED.
        Assert.Equal("legal_obligation", consent.LawfulBasis);
    }

    // ---------------------------------------------------------------------------------------------
    // F4 — purge audit events must inherit the SUBJECT's region, not the system caller's ZZ.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task F4_PurgeRunAuditEvent_InheritsSubjectRegion_NotSystemZZ()
    {
        var userId = FreshUserId();
        using (var seed = NewDb())
        {
            var store = new PostgresEventStore(seed);
            var (_, ctx) = UserContext(userId, new RegionCode("DE", null)); // a German subject
            await store.Append(StreamType.Behavioral, userId, "behavioral.fixture", "{}", ctx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        var pipeline = BuildPipeline(db);
        var reports = await pipeline.Run(PurgeClass.AccountDeletion, new SubjectRef("user", userId), SystemActor, SystemCtx("lens-purge"));

        // Every purge.run audit event for a German user's erasure is EU-scoped work (§1b) and must be
        // stamped region 'DE', never the pure-system 'ZZ'. The pipeline stamps the caller's ctx region.
        var report = reports[0];
        var audit = new List<RecordedEvent>();
        await foreach (var e in new PostgresEventStore(db).ReadStream(StreamType.Audit, report.RunId))
        {
            audit.Add(e);
        }
        var purgeRun = audit.Single(e => e.EventType == "purge.run");
        Assert.Equal("DE", purgeRun.Region);
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
}
