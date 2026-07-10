using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.DependencyInjection;
using Svac.AimlRouter.Routing;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Quota;
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
/// ADVERSARIAL purge-completeness lens for S2 (SLICE_S2_CONTRACT.md §6: "Zero new registrations — a
/// design theorem, not an omission ... subject purge rides existing audit-stream machinery with nothing
/// new to register"). Every test here is a DEMONSTRATED break of that theorem against the running code:
/// it routes a real request through the real IAimlRouter (SeedProvider DI, the sanctioned test surface),
/// lands the real `aiml.route_decided` event on real Postgres via the real PostgresEventStore, runs the
/// real PurgePipeline, and asserts the posture §6 promises. Each currently FAILS.
///
/// Findings pinned by this file:
///  S2P1  `aiml.route_decided` is appended with streamId = the aiv_ invocation id
///        (AimlRouterService.cs:182) while the erased subject's identifier rides INSIDE the payload as
///        `subject_ref` (AimlRouteDecidedEvent.cs:17). The ONLY subject-scoped selector the audit-stream
///        purge machinery has is `stream_id == subject.ResourceId` (PurgePipeline.cs:144), so
///        AccountDeletion / StatutoryErasure / MinorPurge — all registered Tombstone for events_audit
///        (PurgeRegistry.cs:65-67) — tombstone ZERO router rows: the subject's raw id (and the
///        payload_sha256 content-derivative beside it) survives every purge class, forever. The §6 claim
///        that the derivative inherits the subject's lifetime through "existing machinery" is falsified:
///        the existing machinery cannot FIND the row.
///  S2P2  The same mis-keying makes events_audit's RetentionExpiry=Delete verb (PurgeRegistry.cs:69)
///        structurally unreachable for router rows: retention runs are subject-scoped through the same
///        stream_id selector, and no subject enumeration ever yields an aiv_ id — router decision rows
///        have NO reachable purge class at all, i.e. an infinite de-facto retention the registry never
///        declared.
///  S2P3  AnthropicLocalTransport shells to the `claude` CLI (AnthropicLocalTransport.cs:40-58), whose
///        print mode persists the full prompt + completion to its own on-disk session history
///        (~/.claude) by default — a content store holding AimlPayload derivatives with unbounded
///        lifetime, zero 13A registration, and no purge verb, contradicting §1b's "the router holds
///        content for the duration of the call and not one second longer." Dev-only DI caps the blast
///        radius but the evals pump checked-in fixture content through it today and the store outlives
///        every call. The transport must isolate the CLI's persistence root (e.g. CLAUDE_CONFIG_DIR to
///        a per-call temp dir removed after exit) so any CLI-side derivative dies with the call.
/// </summary>
public sealed class AimlPurgeCompletenessLensTests : IAsyncLifetime
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

    private static readonly ActorRef SystemActor =
        new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);

    private static string FreshUserId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    // ---------------------------------------------------------------------------------------------
    // Real-seam plumbing: the real router resolved out of the SANCTIONED test DI surface
    // (AddAimlRouterWithSeedProvider, §1b "SeedProvider is test DI only"), over the REAL Postgres
    // event store — only 9A/10A are faked, exactly as the gate lane does, because neither is the
    // subsystem under adversarial test here.
    // ---------------------------------------------------------------------------------------------

    private static ServiceProvider BuildRouterHost(CoreDbContext db)
    {
        var services = new ServiceCollection();
        services.AddAimlRouterWithSeedProvider();
        services.AddSingleton<IEventStore>(new PostgresEventStore(db));
        services.AddSingleton<IConfigRegistry>(new LensConfigRegistry());
        services.AddSingleton<IQuotaService>(new AllowAllQuota());
        return services.BuildServiceProvider();
    }

    /// <summary>Routes one Pseudonymous-class request FOR the given subject through the real router and returns the recorded audit rows.</summary>
    private static async Task RouteOnceFor(CoreDbContext db, string subjectUserId)
    {
        await using var host = BuildRouterHost(db);
        using var scope = host.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<IAimlRouter>();

        var subject = new ActorRef(OpaqueId.Parse(subjectUserId), ActorKind.User);
        var request = new AimlRequest(
            Task: AimlTaskKind.ModerateText,
            Caller: CallerModule.Integrity,
            PayloadClass: PayloadClass.Pseudonymous,
            Subject: subject, // "Opaque; region + PURGE SCOPING" (§1b) — this is the field under test.
            Payload: AimlPayload.ForUserTurn("lens: T9-style triage of this subject's transcript"),
            TargetLocale: null,
            ExplicitPin: null);

        var result = await router.InvokeAsync(request, SystemCtx("purge-lens-route"));
        Assert.IsType<AimlResult.Success>(result); // precondition: a real routed decision, not a refusal.
    }

    // ---------------------------------------------------------------------------------------------
    // S2P1 — the routed subject's erasure must erase/tombstone the subject_ref-bearing route event.
    // ---------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(PurgeClass.AccountDeletion)]
    [InlineData(PurgeClass.StatutoryErasure)]
    [InlineData(PurgeClass.MinorPurge)]
    public async Task S2P1_RouteDecidedEvent_MustNotSurviveSubjectPurge_WithRawSubjectRefInPayload(PurgeClass purgeClass)
    {
        var userId = FreshUserId();

        using (var routeDb = NewDb())
        {
            await RouteOnceFor(routeDb, userId);
        }

        // Precondition (this part passes): the decision row exists and its payload carries the raw
        // subject id — proving the row is subject-scoped data, not the "metadata only, nothing to
        // register" posture §6 records. "User:usr_..." is ActorRef.ToString() of the subject.
        using (var check = NewDb())
        {
            var seeded = (await check.EventsFor(StreamType.Audit)
                .Where(e => e.EventType == "aiml.route_decided")
                .ToListAsync()) // payload_json is jsonb — LIKE does not translate; filter in memory.
                .Where(e => e.PayloadJson?.Contains(userId, StringComparison.Ordinal) == true)
                .ToList();
            Assert.NotEmpty(seeded);
        }

        // The subject exercises the purge class. Real pipeline, real registry, real verbs.
        using (var purgeDb = NewDb())
        {
            var pipeline = BuildPipeline(purgeDb);
            await pipeline.Run(purgeClass, new SubjectRef("user", userId), SystemActor, SystemCtx("purge-lens-run"));
        }

        // events_audit's registered verb for all three classes is Tombstone ("tombstone actor PII in
        // payload, record survives", PurgeRegistry.cs:65). The record may survive; the subject's raw id
        // may NOT. FAILS: PurgePipeline selects by stream_id == usr_..., the router streamed by aiv_...,
        // so zero rows were touched and subject_ref (plus payload_sha256) survives verbatim.
        using (var verify = NewDb())
        {
            var survivors = (await verify.EventsFor(StreamType.Audit)
                .Where(e => e.EventType == "aiml.route_decided" && !e.Tombstone)
                .ToListAsync()) // payload_json is jsonb — LIKE does not translate; filter in memory.
                .Where(e => e.PayloadJson?.Contains(userId, StringComparison.Ordinal) == true)
                .ToList();
            Assert.Empty(survivors);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // S2P2 — derivative-keying invariant: a subject-scoped audit row must be reachable by the ONE
    // subject selector the purge machinery owns. If subject_ref rides in the payload, stream_id must
    // be the subject — otherwise NO purge class (including RetentionExpiry's Delete) can ever reach
    // the row, an undeclared infinite retention.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task S2P2_RouteDecidedEvent_CarryingASubject_MustBeKeyedBySubjectStreamId()
    {
        var userId = FreshUserId();

        using var db = NewDb();
        await RouteOnceFor(db, userId);

        var row = await db.EventsFor(StreamType.Audit).SingleAsync(e => e.EventType == "aiml.route_decided");

        // "Subject: ... region + purge scoping" (SLICE_S2_CONTRACT.md §1b) and the DDL designates
        // stream_id as THE subject-scope column (S1 §2; PurgePipeline.ExecuteOnEventStream,
        // PurgePipeline.cs:144, is its only reader). FAILS: stream_id is the aiv_ invocation id
        // (AimlRouterService.cs:182), so the row is purge-orphaned the moment it is written.
        Assert.Equal(userId, row.StreamId);
    }

    // ---------------------------------------------------------------------------------------------
    // S2P3 — the local transport must not let the `claude` CLI persist prompt/completion derivatives
    // beyond the call (an unregistered content store with no 13A row and no purge verb).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void S2P3_LocalTransport_MustIsolateTheCliSessionStore_SoContentDerivativesDieWithTheCall()
    {
        var repoRoot = FindRepoRoot();
        var transportSource = Path.Combine(
            repoRoot, "backend", "modules", "AimlRouter", "Svac.AimlRouter", "Providers", "AnthropicLocalTransport.cs");
        Assert.True(File.Exists(transportSource), $"expected transport source at {transportSource}");

        var source = File.ReadAllText(transportSource);

        // The CLI's print mode writes the full session (prompt + completion) into its config root's
        // project history by default. "The router holds content for the duration of the call and not
        // one second longer" (§1b) requires the transport to point that root at a per-call scratch
        // location it deletes after exit. FAILS: no persistence isolation exists in the transport.
        Assert.Contains("CLAUDE_CONFIG_DIR", source, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------

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

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }

    /// <summary>9A fake scoped to exactly the three keys the router reads — routes to SeedProvider the same way the gate lane does (AimlRouterServiceTests precedent).</summary>
    private sealed class LensConfigRegistry : IConfigRegistry
    {
        private static readonly IReadOnlyList<ProviderAllowlistEntry> Allowlist = new[]
        {
            new ProviderAllowlistEntry(
                Name: "seed", Family: "claude", Kinds: new[] { "llm" },
                PayloadClassCeiling: PayloadClass.Pseudonymous, DpaSigned: false, SpecialCategoryOk: false,
                Residency: "global", Models: new[] { "seed-v0" }),
        };

        private static readonly RoutingPolicy Policy = new(
            Version: 1,
            DefaultChain: new[] { new TaskChainLink("seed", "seed-v0") },
            TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
            ResidencyOverrides: Array.Empty<ResidencyOverride>());

        public Task<T> GetValue<T>(string key, CancellationToken ct = default) => key switch
        {
            "aiml.provider_allowlist" => Task.FromResult((T)Allowlist),
            "aiml.routing_policy" => Task.FromResult((T)(object)Policy),
            "aiml.invoke_timeout_seconds" => Task.FromResult((T)(object)5),
            _ => throw new KeyNotFoundException($"purge-lens config fake has no value for \"{key}\"."),
        };

        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("the purge lens never writes config.");
    }

    private sealed class AllowAllQuota : IQuotaService
    {
        public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default) =>
            Task.FromResult<QuotaResult>(new QuotaResult.Ok(new Consumed(Remaining: 9999, ResetsAt: context.Now.AddDays(1))));
    }
}
