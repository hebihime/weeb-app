using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AimlRouter.Config;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.DependencyInjection;
using Svac.AimlRouter.Routing;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.Deterministic;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Purge;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL LENS — S2 pass: minor protection (L1-L4 stack; 18+ invariants). Same posture as
/// <see cref="MinorProtectionLensTests"/> for S1: every test here is EXPECTED TO FAIL against the
/// current S2 code — each pins a demonstrated break of a SLICE_S2_CONTRACT.md promise the minor stack
/// (S18 L1-L4, BUILD.md "minor purge" cells) inherits. Fix the code, not the tests.
///
/// Finding (severity, location, break):
///
/// S2-F1 HIGH   modules/AimlRouter/Svac.AimlRouter/AimlRouterService.cs:182 (Append with
///              streamId = invocationId, an aiv_ ULID) + :168 (the SUBJECT's raw ActorRef serialized
///              into the event payload as subject_ref) vs domain-core Purge/PurgePipeline.cs:144
///              (ExecuteOnEventStream selects a subject's rows by StreamId == subject.ResourceId) and
///              PurgePipeline.cs:335 (ResolveSubjectRegion, same key).
///
///              SLICE_S2_CONTRACT.md §6 claims: "aiml.route_decided events are metadata-only, so
///              subject purge rides existing audit-stream machinery with nothing new to register."
///              FALSE in the running code. The existing machinery is stream_id-keyed; the router keys
///              its audit stream by INVOCATION id and stamps actor_ref with the calling SYSTEM actor
///              (RequestContext.System). The purged subject exists ONLY inside the payload JSON — a
///              column no purge verb selects on. Consequence, demonstrated below: a MinorPurge run
///              (events_audit registered verb = Tombstone, PurgeRegistry.cs:67) matches ZERO
///              aiml.route_decided rows. The purged minor's raw opaque id survives verbatim and
///              non-tombstoned in core.events_audit — joined to task kind, payload_class, provider,
///              model, token counts, and payload_sha256 (a stable, subject-linked content fingerprint
///              of e.g. the minor's reported DM transcript once T9/S12 goes live) — under the audit
///              stream's 7-year retention floor (RetentionMinimumAge). The same key mismatch also
///              breaks AccountDeletion and StatutoryErasure for these rows, and the purge receipt
///              reports events_audit as run with rowsAffected=0: false evidence of completeness, the
///              exact failure the L1-L4 enumerated-purge promise (BUILD.md S18 row: "L1–L4 stack incl.
///              ... enumerated purge") exists to prevent. Fix directions (either satisfies both
///              tests): key subject-bearing route events by a purgeable subject scope, or teach the
///              audit-stream purge verbs a payload-level subject match for subject_ref-bearing events.
/// </summary>
public sealed class MinorProtectionLensS2Tests : IAsyncLifetime
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

    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

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

    /// <summary>
    /// Drives the REAL router (public test-DI surface, SeedProvider) against the REAL Postgres-backed
    /// event store — exactly the wiring the first consumer slice performs — so the audit row this
    /// produces is the row production would produce, not a hand-seeded imitation.
    /// </summary>
    private static async Task<AimlResult> InvokeRouterFor(ActorRef minorSubject, CoreDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(new PostgresEventStore(db));
        services.AddSingleton<IConfigRegistry>(new LensConfigRegistry());
        services.AddSingleton<IQuotaService>(new LensQuotaService());
        services.AddAimlRouterWithSeedProvider();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var router = scope.ServiceProvider.GetRequiredService<IAimlRouter>();

        // The T9 shape (SLICE_S2_CONTRACT.md §1b: task kinds pre-declared from ratified consumer
        // specs): system-actor triage of a REPORTED USER's transcript, subject-scoped to that user.
        // For the minor lens the subject is the reported minor whose L1-L4 removal will later demand
        // a complete purge of every row scoped to them.
        var request = new AimlRequest(
            AimlTaskKind.ModerateText,
            CallerModule.Integrity,
            PayloadClass.Pseudonymous,
            Subject: minorSubject,
            Payload: AimlPayload.ForUserTurn("report-transcript excerpt"),
            TargetLocale: null,
            ExplicitPin: null);

        return await router.InvokeAsync(request, RequestContext.System(SystemActor, "lens-minor-s2"));
    }

    // ------------------------------------------------------------------ S2-F1
    [Fact]
    public async Task S2F1_MinorPurge_MustLeaveNoRouterAuditRowStillReferencingTheMinor()
    {
        var minorId = FreshUserId();
        var minorSubject = new ActorRef(OpaqueId.Parse(minorId), ActorKind.User);

        using (var seedDb = NewDb())
        {
            var result = await InvokeRouterFor(minorSubject, seedDb);
            Assert.IsType<AimlResult.Success>(result); // precondition: a real, successful routed call
        }

        // Precondition: the route decision landed on events_audit carrying the minor's raw opaque id.
        using (var checkDb = NewDb())
        {
            var seeded = await checkDb.EventsFor(StreamType.Audit)
                .Where(e => e.EventType == "aiml.route_decided")
                .ToListAsync();
            Assert.Contains(seeded, e => e.PayloadJson != null && e.PayloadJson.Contains(minorId, StringComparison.Ordinal));
        }

        using var db = NewDb();
        await BuildPipeline(db).Run(
            PurgeClass.MinorPurge,
            new SubjectRef("user", minorId),
            SystemActor,
            RequestContext.System(SystemActor, "lens-minor-s2-purge"));

        // Registry: events_audit / MinorPurge = Tombstone ("OQ-1 interim posture (a)": payload nulled,
        // record survives). SLICE_S2_CONTRACT.md §6: subject purge "rides existing audit-stream
        // machinery". If that were true, no live payload could still name the minor. CURRENT BEHAVIOR:
        // the pipeline selects by StreamId == subject id, the router streams by invocation id, so the
        // minor's raw id + payload_sha256 content fingerprint survive un-tombstoned for 7+ years.
        using var verifyDb = NewDb();
        var residue = await verifyDb.EventsFor(StreamType.Audit)
            .Where(e => e.EventType == "aiml.route_decided" && !e.Tombstone && e.PayloadJson != null)
            .ToListAsync();
        Assert.DoesNotContain(residue, e => e.PayloadJson!.Contains(minorId, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ S2-F1 (receipt honesty)
    [Fact]
    public async Task S2F1b_MinorPurge_Receipt_MustNotClaimEventsAuditCleanWhileRouterResidueExists()
    {
        var minorId = FreshUserId();
        var minorSubject = new ActorRef(OpaqueId.Parse(minorId), ActorKind.User);

        using (var seedDb = NewDb())
        {
            Assert.IsType<AimlResult.Success>(await InvokeRouterFor(minorSubject, seedDb));
        }

        using var db = NewDb();
        var reports = await BuildPipeline(db).Run(
            PurgeClass.MinorPurge,
            new SubjectRef("user", minorId),
            SystemActor,
            RequestContext.System(SystemActor, "lens-minor-s2-purge"));

        // Exactly one subject-scoped row exists on events_audit (the route event seeded above). The
        // §6 "purge-completeness" evidence contract is "zero residue or asserted tombstone state" —
        // a receipt asserting the store was run while the subject-bearing row was never touched is
        // false completeness evidence (the same failure class as S1's F2). CURRENT BEHAVIOR:
        // rowsAffected == 0 while the row survives with a live payload naming the minor.
        var auditReport = reports.Single(r => r.StoreKey == "events_audit");
        Assert.True(
            auditReport.RowsAffected >= 1,
            $"MinorPurge receipt for events_audit reports rowsAffected={auditReport.RowsAffected} while an " +
            "aiml.route_decided row whose payload names the purged minor survives un-tombstoned — false evidence of purge completeness.");
    }
}

/// <summary>Minimal typed 9A stand-in for the router's three read keys — allowlist/policy/timeout only, seed provider allowlisted the way the module's own gate-lane fakes do.</summary>
file sealed class LensConfigRegistry : IConfigRegistry
{
    private static readonly IReadOnlyList<ProviderAllowlistEntry> Allowlist = new[]
    {
        new ProviderAllowlistEntry(
            Name: "seed", Family: "claude", Kinds: new[] { "llm" },
            PayloadClassCeiling: PayloadClass.Personal, DpaSigned: true, SpecialCategoryOk: false,
            Residency: "global", Models: new[] { "seed-model-1" }),
    };

    private static readonly RoutingPolicy Policy = new(
        Version: 1,
        DefaultChain: new[] { new TaskChainLink("seed", "seed-model-1") },
        TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
        ResidencyOverrides: Array.Empty<ResidencyOverride>());

    public Task<T> GetValue<T>(string key, CancellationToken ct = default) => key switch
    {
        AimlRouterConfigKeys.ProviderAllowlist => Task.FromResult((T)(object)Allowlist),
        AimlRouterConfigKeys.RoutingPolicy => Task.FromResult((T)(object)Policy),
        AimlRouterConfigKeys.InvokeTimeoutSeconds => Task.FromResult((T)(object)60),
        _ => throw new InvalidOperationException($"lens config registry has no value for '{key}'"),
    };

    public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
        throw new NotSupportedException("read-only lens fake");
}

/// <summary>Always-allow quota fake — the budget breaker is out of scope for this lens's finding.</summary>
file sealed class LensQuotaService : IQuotaService
{
    public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default) =>
        Task.FromResult<QuotaResult>(new QuotaResult.Ok(new Consumed(int.MaxValue, DateTimeOffset.UtcNow.AddDays(1))));
}
