using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Ledger;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
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
/// ADVERSARIAL LENS: minor protection (L1-L4 stack; 18+ invariants). Every test in this file is
/// EXPECTED TO FAIL against the current S1 code — each one pins a demonstrated break of a
/// SLICE_S1_CONTRACT.md promise the minor stack (S18 verification, S24 dm-media age legs, S25
/// character metric gate) will inherit as a structural foundation. Fix the code, not the tests.
///
/// Findings (severity, location, break):
///
/// F1 CRITICAL  FieldEncryption/AesFieldEncryptor.cs:62 (Shred) + Purge/PurgePipeline.cs:191-213
///              (ExecuteCryptoShred). Shred ignores SubjectScope and destroys the PURPOSE-WIDE key
///              ("field-enc-birthdate-v1" etc). PurgePipeline calls Shred for EVERY purpose on EVERY
///              CryptoShred-registered store per subject — so ONE MinorPurge (or AccountDeletion) run
///              for ONE subject permanently destroys every OTHER user's Birthdate, SpecialCategory,
///              VerificationAudit and IdentityExclusionFilters ciphertexts, and every subsequent
///              Protect/Unprotect throws. The registered §6 posture is per-subject key destruction;
///              what ships is population-wide destruction of the exact purposes the 18+ verification
///              stack (T2r birthdate attest, verification audit) depends on.
///
/// F2 HIGH      Purge/PurgePipeline.cs:88-98 (ExecuteVerb "_ =&gt; 0") vs Purge/PurgeRegistry.cs:29-37.
///              The registry declares Tombstone for ledger_balances under MinorPurge, but the pipeline
///              has no case for ledger_balances and silently no-ops (the code comment claims its
///              reachable cells are all NotApplicable — false, the registry group loop registers
///              Tombstone). LedgerService.Append materializes a core.ledger_balances row keyed by the
///              minor's user_id (LedgerService.cs:115-145); after MinorPurge that row survives intact,
///              and the PurgeReport records the store as run with rowsAffected=0 — false evidence of
///              completeness. §6 pipeline proof promises "zero residue or asserted tombstone state".
///
/// F3 HIGH      Purge/PurgePipeline.cs:137-143 (Pseudonymize) + Persistence/Migrations/
///              20260710085111_InitialCore.cs:466-478. The §6 registered posture for events_consent
///              under MinorPurge is "pseudonymize SUBJECT (irreversible re-key)". The implementation
///              re-keys only actor_ref; stream_id — the subject scope per §2 — is untouched, and the
///              append-only trigger's pseudonymize transition structurally REQUIRES stream_id
///              unchanged (migration line 470). Post-purge, ReadStream(Consent, minor's-real-id)
///              still returns the minor's consent history keyed by their real opaque id: the subject
///              linkage the purge exists to sever survives verbatim.
///
/// F4 MEDIUM    Purge/PurgePipeline.cs:215-220 (PseudonymizeRef). The "irreversible re-key" is an
///              UNSALTED, UNKEYED SHA256 of (purgeClass + the original actor ref). Anyone holding a
///              candidate user id (e.g. from tombstoned rows on other streams, logs, or backups) can
///              recompute the pseudonym and confirm "this pseudonymized consent row belongs to that
///              minor" — deterministic confirmation-attack linkage. Real pseudonymization requires a
///              secret key (HMAC under an IFieldKeyVault-held key) or a random per-subject mapping.
///
/// F5 MEDIUM    TrustDtoArchTest.cs:17-18. The L20 server-authoritative-trust gate's field pattern
///              (^verification|reputation|premium|moderation_state|age_estimate|trust|tier) is blind
///              to the canonical forgeable-18+ attest field names: age_verified, age_attested,
///              is_adult, adult_verified, birthdate_verified, minor_flag. A future request DTO
///              carrying any of these sails through the gate — the exact client-forged-adulthood
///              vector L20 and the minor stack exist to kill. (The scan is also top-level-properties-
///              only over "*Request"-named types in one namespace, not the "request-DTO type graphs"
///              §8 promises — a nested payload type with a trust field is invisible to it.)
///
/// F6 LOW       SLICE_S1_CONTRACT.md §8 row "L19 rank-by-attestation / on-platform-REAL: arch-rule
///              slots land now, binding future types by name". No such slot exists anywhere in the
///              architecture suite (grep L19/attestation: zero hits). The 15A row's equivalent
///              promise WAS honored (ProviderSdkArchTest); the L19 one — the S1 hook the S25
///              character 18+ metric-gate types would bind to by name — was silently dropped.
/// </summary>
public sealed class MinorProtectionLensTests
{
    // ------------------------------------------------------------------ F1
    [Fact]
    public async Task F1_CryptoShred_OfOneSubject_MustNotDestroyOtherSubjectsAgeData()
    {
        var encryptor = new AesFieldEncryptor(new DevKeyringFieldKeyVault());

        // An adult's attested birthdate, protected under the Birthdate purpose (the §1b closed enum
        // entry the S18 verification stack will use).
        var adultBlob = await encryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_adult"), "1990-01-01");

        // A DIFFERENT subject (a minor) is purged — exactly what PurgePipeline.ExecuteCryptoShred
        // does per purpose for the purged subject's scope.
        await encryptor.Shred(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_minor_being_purged"));

        // §6 registered posture: shred is scoped to the SUBJECT. The adult's data must survive.
        // CURRENT BEHAVIOR: AesFieldEncryptor.Shred destroys the purpose-wide key
        // ("field-enc-birthdate-v1"), so this Unprotect throws InvalidOperationException and the
        // entire population's birthdate attestation data is gone.
        var roundTripped = await encryptor.Unprotect(FieldEncryptionPurpose.Birthdate, adultBlob);
        Assert.Equal("1990-01-01", roundTripped);
    }

    [Fact]
    public async Task F1b_CryptoShred_MustNotBrickAllFutureBirthdateProtection()
    {
        var encryptor = new AesFieldEncryptor(new DevKeyringFieldKeyVault());

        await encryptor.Shred(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_minor_being_purged"));

        // A NEW user signs up after the purge run and attests their birthdate. CURRENT BEHAVIOR:
        // Protect throws — the purpose key is destroyed forever, for everyone, including users who
        // do not exist yet.
        var blob = await encryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope("usr_new_signup"), "1995-06-15");
        Assert.NotEmpty(blob);
    }

    // ------------------------------------------------------------------ F5
    [Fact]
    public void F5_L20TrustGate_MustCatchForgeable18PlusAttestFields()
    {
        // Reflect the DEPLOYED gate regex (not a copy) so this pins the actual blind spot.
        var patternField = typeof(TrustDtoArchTest).GetField("TrustFieldPattern", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(patternField);
        var pattern = (Regex)patternField!.GetValue(null)!;

        // Canonical client-forged-adulthood field names. Every one of these on a request DTO is a
        // client asserting its own 18+ status — the exact L20 violation class for the minor stack.
        var forgeableAgeFields = new[]
        {
            "age_verified", "AgeAttested", "is_adult", "adult_verified", "birthdate_verified", "minor_flag",
        };

        var missed = forgeableAgeFields.Where(f => !pattern.IsMatch(f)).ToList();
        Assert.Empty(missed); // CURRENT BEHAVIOR: all six are missed (only the literal prefix "age_estimate" is covered).
    }

    // ------------------------------------------------------------------ F6
    [Fact]
    public void F6_L19RankByAttestation_ArchRuleSlot_MustLandAtS1()
    {
        // §8: "L19 rank-by-attestation / on-platform-REAL — arch-rule slots land now, binding future
        // types by name." The 15A row's identical promise produced ProviderSdkArchTest; this one
        // produced nothing. Assert a named slot exists in the suite.
        var slotTypes = typeof(TrustDtoArchTest).Assembly.GetTypes()
            .Where(t => t.Name.Contains("Attestation", StringComparison.OrdinalIgnoreCase)
                     || t.Name.Contains("L19", StringComparison.OrdinalIgnoreCase)
                     || t.Name.Contains("RankBy", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(slotTypes); // CURRENT BEHAVIOR: empty — the promised slot never landed.
    }
}

/// <summary>
/// The pipeline half of the lens (F2, F3, F4): runs MinorPurge — the purge class the L1-L4 stack's
/// enumerated-purge promise (BUILD.md S18 row) rides on — against real Postgres with real seeded data.
/// The in-repo suite only exercises MinorPurge against events_reputation
/// (PurgeCompletenessTests.MinorPurge_OnEventsReputation_...); these tests cover the stores it never
/// asserts on, and each currently fails.
/// </summary>
public sealed class MinorProtectionLensPipelineTests : IAsyncLifetime
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

    private static (ActorRef Actor, RequestContext Ctx) UserContext(string userId)
    {
        var actor = new ActorRef(OpaqueId.Parse(userId), ActorKind.User);
        var ctx = new RequestContext(actor, RegionCode.Unknown, RegionSource.System, LawfulBasisVariant.ConservativeGlobalV0, "en", "lens-minor-seed");
        return (actor, ctx);
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

    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    // ------------------------------------------------------------------ F2
    [Fact]
    public async Task F2_MinorPurge_LeavesNoBalanceRowKeyedByTheMinor()
    {
        var minorId = FreshUserId();
        using (var seedDb = NewDb())
        {
            // Real seam: LedgerService.Append stages the ledger_balances projection row in the same tx.
            var ledger = new LedgerService(seedDb, new PostgresEventStore(seedDb), new PolicyEngine(new PolicyTable()));
            await ledger.Append(
                new LedgerEntry(minorId, CrewId: null, EventType: "quest_complete", Points: 10, Xp: 10, Svac: 0, QuestId: null, EvidenceRef: null),
                SystemActor, RequestContext.System(SystemActor, "lens-minor-ledger"));
        }

        using var db = NewDb();
        Assert.True(await db.LedgerBalances.AnyAsync(b => b.UserId == minorId), "seed precondition: the balance row must exist before the purge");

        await BuildPipeline(db).Run(PurgeClass.MinorPurge, new SubjectRef("user", minorId), SystemActor, RequestContext.System(SystemActor, "lens-minor-purge"));

        // Registry: ledger_balances / MinorPurge = Tombstone ("user refs severed; balances rebuilt by
        // Replay", §6). CURRENT BEHAVIOR: PurgePipeline.ExecuteVerb has no ledger_balances case
        // ("_ => 0") — the minor's balance row survives, keyed by their real user_id, forever.
        using var verifyDb = NewDb();
        Assert.False(
            await verifyDb.LedgerBalances.AnyAsync(b => b.UserId == minorId),
            "core.ledger_balances still holds a row keyed by the purged minor's user_id — residue after MinorPurge.");
    }

    // ------------------------------------------------------------------ F3
    [Fact]
    public async Task F3_MinorPurge_OnEventsConsent_MustSeverTheSubjectKey()
    {
        var minorId = FreshUserId();
        using (var seedDb = NewDb())
        {
            var (_, ctx) = UserContext(minorId);
            await new PostgresEventStore(seedDb).Append(StreamType.Consent, minorId, "consent.granted", "{\"scope\":\"profile\"}", ctx, ExpectedVersion.AnyVersion);
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(PurgeClass.MinorPurge, new SubjectRef("user", minorId), SystemActor, RequestContext.System(SystemActor, "lens-minor-purge"));

        // §6 posture: "pseudonymize SUBJECT (irreversible re-key)". If the subject were re-keyed,
        // reading the stream BY THE MINOR'S REAL ID would return nothing. CURRENT BEHAVIOR:
        // PurgePipeline.Pseudonymize rewrites actor_ref only; stream_id (the subject scope, §2) is
        // untouched — and the append-only trigger's pseudonymize transition REQUIRES it untouched —
        // so the minor's consent history still reads back keyed by their real opaque id.
        var stillKeyedBySubject = new List<RecordedEvent>();
        await foreach (var e in new PostgresEventStore(db).ReadStream(StreamType.Consent, minorId))
        {
            stillKeyedBySubject.Add(e);
        }
        Assert.Empty(stillKeyedBySubject);
    }

    // ------------------------------------------------------------------ F4
    [Fact]
    public async Task F4_MinorPurge_Pseudonym_MustNotBeRecomputableFromPublicInputs()
    {
        var minorId = FreshUserId();
        var (minorActor, ctx) = UserContext(minorId);
        string seededEventId;
        using (var seedDb = NewDb())
        {
            var seeded = await new PostgresEventStore(seedDb).Append(StreamType.Consent, minorId, "consent.granted", "{}", ctx, ExpectedVersion.AnyVersion);
            seededEventId = seeded.EventId;
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(PurgeClass.MinorPurge, new SubjectRef("user", minorId), SystemActor, RequestContext.System(SystemActor, "lens-minor-purge"));

        // Looked up by EventId (the row's primary key, invariant under re-keying) rather than by
        // stream_id=minorId: F3 (above) already proves the subject-scope column itself is re-keyed away
        // from the minor's real id as part of THIS SAME fix, so a stream_id-keyed lookup would no longer
        // find the survivor at all — that is the fix working, not a second bug.
        var row = await db.EventsFor(StreamType.Consent).SingleAsync(e => e.EventId == seededEventId);

        // The confirmation attack: an adversary holding a candidate id (from another stream's
        // tombstoned rows, a log, a backup) recomputes SHA256("{purgeClass}:{originalActorRef}")
        // exactly as PurgePipeline.PseudonymizeRef does — no secret required — and confirms the
        // pseudonymized row belongs to the minor. "Irreversible re-key" (§6/§15) must mean an
        // adversary CANNOT do this; a keyed construction (HMAC under IFieldKeyVault) would break it.
        var recomputed = Ulid.WithPrefix(
            "pseudo",
            Ulid.Encode(0, SHA256.HashData(Encoding.UTF8.GetBytes($"{PurgeClass.MinorPurge}:{minorActor}")).AsSpan(0, 10)));

        Assert.NotEqual(recomputed, row.ActorRef); // CURRENT BEHAVIOR: equal — linkage confirmed with zero secrets.
    }
}
