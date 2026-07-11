using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.DependencyInjection;
using Svac.AimlRouter.Routing;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Api;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Quota;
using Svac.DomainCore.Contracts.Streams;
using Svac.DomainCore.EventStore;
using Svac.DomainCore.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL LENS — S2 pass: PII / residency + special-category (encryption, region/lawful-basis,
/// vendor-egress classification). Same posture as PiiResidencyLensTests (the S1 pass): every test
/// asserts a SLICE_S2_CONTRACT.md promise and is RED against the shipped code, demonstrating a concrete
/// break with inputs -&gt; wrong result. Purge-reachability of `aiml.route_decided` rows is deliberately
/// NOT re-tested here — AimlPurgeCompletenessLensTests (S2P1/S2P2) and MinorProtectionLensS2Tests
/// (S2-F1) already pin that mis-keying; this file owns what no other lens covers:
///
///   PII-S2-F1 (HIGH, special-category lock missing): contract §1b promises "TWO independent locks"
///       on special-category vendor egress — RefuseAllSpecialCategoryAuthorizer AND "the allowlist
///       bounds rule that refuses saving any special_category_ok:true entry until S17 ships" (§4:
///       "fails bounds at SetValue — an unlawful ... policy cannot be *saved*, not merely
///       not-served"; §10.2 names "special_category_ok" in the 9A-bounds-rejections gate suite).
///       ConfigRegistry.SetValue (backend/domain-core/Svac.DomainCore/Config/ConfigRegistry.cs:27-47)
///       performs ZERO bounds validation of any kind — no rule for special_category_ok, no ceiling
///       rule, no family=="claude" default rule; ConfigSeedLoader.cs:61 even seeds BoundsJson = null.
///       One founder-scope Set (`special_category_ok:true`, ceiling "SpecialCategory") saves cleanly,
///       and the entire special-category posture then hangs on the single DI-registered authorizer.
///
///   PII-S2-F2 (HIGH, residency dead): contract §1b — "residency-aware deterministic routing ...
///       structural at S2", "`residency_overrides` is a first-class policy input", §8 seam row
///       "`residency_overrides` a first-class resolver input", §10.2 golden vectors "region variants".
///       Resolver.Resolve (backend/modules/AimlRouter/Svac.AimlRouter/Routing/Resolver.cs:25-66)
///       accepts `region` and never reads it; it never touches RoutingPolicy.ResidencyOverrides nor
///       the allowlist entry's `residency` field. Worse, RoutingPolicy.ResidencyOverrides is typed
///       IReadOnlyList&lt;string&gt; — a REAL (structured) override cannot even deserialize, and because
///       SetValue has no bounds (F1) an ops-desk save of such an override is ACCEPTED and then poisons
///       every subsequent Automatic-path GetValue&lt;RoutingPolicy&gt; with a raw JsonException: a desk
///       edit meant to TIGHTEN residency takes the whole egress down instead of routing by it.
///
///   PII-S2-F3 (HIGH, region/lawful-basis provenance): contract §1b — "Region rides S1's
///       RequestContext law: system-actor calls inherit the SUBJECT's region — T9 triage of a German
///       user's transcript is EU-scoped work"; §8 — "subject-region inheritance for system actors".
///       AimlRouterService.AppendDecision (AimlRouterService.cs:158-183) stamps the CALLER's ctx
///       verbatim and has no path to the subject's region — the exact defect S1's PII-F4 fixed for
///       purge.run events (PurgePipeline.ResolveSubjectRegion) with no analog here. A system-actor
///       call about a DE subject records region='ZZ', and LawfulBasisResolver then resolves
///       lawful_basis='n/a' ("no personal data") on a decision event that carries the subject's raw
///       id and PayloadClass=Pseudonymous/Personal — a falsified lawful-basis record for exactly the
///       consumer shape (T9 background triage) this slice pre-declares.
///
///   PII-S2-F4 (MEDIUM, blob contents via argv) lives in
///       backend/tests/Svac.Tests.AimlRouter/PiiResidencyLensS2ArgvTests.cs (needs InternalsVisibleTo).
/// </summary>
public sealed class PiiResidencyLensS2Tests : IAsyncLifetime
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

    private static readonly ActorRef FounderActor =
        new(OpaqueId.New(IdPrefixes.Staff, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Staff);

    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
    }

    private static readonly string[] LlmKinds = { "llm" };
    private static readonly string[] OpusModels = { "claude-opus-4-8" };

    /// <summary>Seeds the REAL S2 manifest into the REAL registry — the exact state a booted host has.</summary>
    private static async Task<ConfigRegistry> SeededRegistry(CoreDbContext db)
    {
        var manifest = Path.Combine(
            FindRepoRoot(), "backend", "modules", "AimlRouter", "config", "aiml-router.config.json");
        Assert.True(File.Exists(manifest), $"expected the S2 manifest at {manifest}");

        var eventStore = new PostgresEventStore(db);
        await new ConfigSeedLoader(db, eventStore).SeedFromFile(manifest, SystemCtx("pii-lens-seed"));
        return new ConfigRegistry(db, eventStore);
    }

    // ---------------------------------------------------------------------------------------------
    // PII-S2-F1 — the second special-category lock (§1b "Two independent locks"; §4 bounds rule;
    // §10.2 "9A bounds rejections ... special_category_ok") must refuse the save. FAILS: SetValue
    // has no bounds machinery at all — the unlawful entry saves and reads back verbatim.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task PiiS2F1_Allowlist_SpecialCategoryOkTrue_MustFailBoundsAtSetValue_BeforeS17Exists()
    {
        using var db = NewDb();
        var registry = await SeededRegistry(db);

        var unlawful = new[]
        {
            new ProviderAllowlistEntry(
                Name: "anthropic", Family: "claude", Kinds: LlmKinds,
                PayloadClassCeiling: PayloadClass.SpecialCategory,
                DpaSigned: false,
                SpecialCategoryOk: true, // §4: "refuses saving any allowlist entry with special_category_ok: true until S17 ships"
                Residency: "global", Models: OpusModels),
        };

        // The contract's law is that this entry "cannot be *saved*, not merely not-served" (§4) —
        // an audited, typed refusal at SetValue. FAILS: ConfigRegistry.SetValue (ConfigRegistry.cs:27)
        // validates nothing, the row lands, and the two-lock posture silently degrades to one lock.
        var ex = await Record.ExceptionAsync(() => registry.SetValue(
            "aiml.provider_allowlist", unlawful,
            reason: "adversarial lens: special-category egress enablement before S17",
            FounderActor, SystemCtx("pii-lens-f1")));

        Assert.NotNull(ex);

        // And the stored row must still be the conservative OQ-A posture, not the unlawful one.
        var stored = await registry.GetValue<IReadOnlyList<ProviderAllowlistEntry>>("aiml.provider_allowlist");
        Assert.All(stored, e => Assert.False(e.SpecialCategoryOk,
            "an allowlist entry with special_category_ok:true was persisted before S17's consent ledger exists (§1b two-locks law)"));
    }

    // ---------------------------------------------------------------------------------------------
    // PII-S2-F2a — "residency-aware deterministic routing ... structural at S2" (§1b). The resolver
    // must actually CONSUME its residency inputs: the policy's residency_overrides and the allowlist
    // entry's residency field. FAILS: Resolver.cs never references either — `region` is an ignored
    // parameter, so routing is region-blind by construction and no golden "region variant" can exist.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void PiiS2F2a_Resolver_MustConsumeResidencyInputs_NotAcceptAndIgnoreThem()
    {
        var resolverSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "backend", "modules", "AimlRouter", "Svac.AimlRouter", "Routing", "Resolver.cs"));

        // Strip line comments so a doc-comment mention cannot satisfy the assertion — the resolver's
        // CODE must read the residency inputs, not its prose.
        var code = string.Join('\n', resolverSource
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        Assert.Contains("ResidencyOverrides", code, StringComparison.Ordinal); // §1b/§8: first-class resolver input.
        Assert.Contains(".Residency", code, StringComparison.Ordinal);          // allowlist residency vs subject region.
    }

    // ---------------------------------------------------------------------------------------------
    // PII-S2-F2b — the registry/resolver pair must agree: a routing_policy the registry ACCEPTS must
    // be a policy the resolver can READ. A structured residency override (the only kind that can
    // express "DE routes here") saves cleanly (no bounds, F1) and then every Automatic-path call
    // dies on GetValue deserialization — an ops tightening edit becomes a full egress outage.
    // FAILS at the GetValue assert (JsonException: object into IReadOnlyList<string> element).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task PiiS2F2b_SavedResidencyOverride_MustEitherBeRefusedAtSet_OrRemainReadableByTheResolver()
    {
        using var db = NewDb();
        var registry = await SeededRegistry(db);

        var tightened = new
        {
            version = 2,
            default_chain = new[] { new { provider = "anthropic", model = "claude-opus-4-8" } },
            task_chains = new Dictionary<string, object>(),
            residency_overrides = new[]
            {
                new { region = "DE", chain = new[] { new { provider = "anthropic", model = "claude-opus-4-8" } } },
            },
        };

        Exception? setEx = await Record.ExceptionAsync(() => registry.SetValue(
            "aiml.routing_policy", tightened,
            reason: "adversarial lens: EU residency pin", FounderActor, SystemCtx("pii-lens-f2b")));

        if (setEx is null)
        {
            // The save was accepted — then the resolver's own read of the SAME key must not throw.
            // This is the exact read AimlRouterService.InvokeAsync performs on every Automatic call
            // (AimlRouterService.cs:73); if it throws here, it throws there, for every caller, until
            // the row is manually repaired.
            var readEx = await Record.ExceptionAsync(() => registry.GetValue<RoutingPolicy>("aiml.routing_policy"));
            Assert.Null(readEx);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // PII-S2-F3 — subject-region inheritance (§1b verbatim: "system-actor calls inherit the SUBJECT's
    // region — T9 triage of a German user's transcript is EU-scoped work"; §8 seam row). Route a
    // Pseudonymous-class request FOR a subject whose recorded rows are DE, from a system-actor
    // context (region ZZ, exactly how a background triage worker calls). The decision event must be
    // EU-scoped work: region 'DE', lawful_basis resolved for DE (Audit -> 'legal_obligation'), never
    // the pure-system 'ZZ'/'n/a' sentinel pair — 'n/a' asserts "no personal data" on a row carrying
    // the subject's raw id. FAILS: AimlRouterService stamps the caller's ctx verbatim.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public async Task PiiS2F3_RouteDecided_ForAGermanSubject_FromASystemActor_MustBeEuScopedWork_NeverZzNa()
    {
        var userId = OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();
        var subject = new ActorRef(OpaqueId.Parse(userId), ActorKind.User);

        using var db = NewDb();
        var eventStore = new PostgresEventStore(db);

        // The subject exists with recorded DE rows — the same source of regional truth
        // PurgePipeline.ResolveSubjectRegion (PII-F4's ratified fix) reads.
        var deCtx = new RequestContext(subject, new RegionCode("DE", null), RegionSource.Signup,
            LawfulBasisVariant.ConservativeGlobalV0, "de", "pii-lens-f3-seed");
        await eventStore.Append(StreamType.Consent, streamId: userId, eventType: "consent.granted",
            payloadJson: "{}", deCtx, ExpectedVersion.AnyVersion);

        // Route through the real router out of the sanctioned test DI surface, as the system actor.
        var services = new ServiceCollection();
        services.AddAimlRouterWithSeedProvider();
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<IConfigRegistry>(new SeedRoutedConfig());
        services.AddSingleton<IQuotaService>(new AllowAllQuota());
        await using var host = services.BuildServiceProvider();
        using var scope = host.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<IAimlRouter>();

        var result = await router.InvokeAsync(
            new AimlRequest(
                Task: AimlTaskKind.ModerateText, Caller: CallerModule.Integrity,
                PayloadClass: PayloadClass.Pseudonymous,
                Subject: subject,
                Payload: AimlPayload.ForUserTurn("lens: T9-style triage of this DE subject's transcript"),
                TargetLocale: null, ExplicitPin: null),
            SystemCtx("pii-lens-f3-route"));
        Assert.IsType<AimlResult.Success>(result); // precondition: a real routed decision.

        var row = await db.EventsFor(StreamType.Audit).SingleAsync(e => e.EventType == "aiml.route_decided");

        Assert.Equal("DE", row.Region); // §1b: the subject's region, inherited — FAILS: 'ZZ'.
        Assert.NotEqual("n/a", row.LawfulBasis); // a subject-bearing decision row is personal-data work — FAILS: 'n/a'.
    }

    // ---------------------------------------------------------------------------------------------
    // helpers (mirrors AimlPurgeCompletenessLensTests: only 9A/10A are faked — neither is the
    // subsystem under adversarial test in F3)
    // ---------------------------------------------------------------------------------------------

    private sealed class SeedRoutedConfig : IConfigRegistry
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
            _ => throw new KeyNotFoundException($"PII lens config fake has no value for \"{key}\"."),
        };

        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("the PII lens F3 path never writes config.");

        public Task<IReadOnlyList<ConfigEntryView>> ListEntries(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConfigEntryView>>(Array.Empty<ConfigEntryView>());
    }

    private sealed class AllowAllQuota : IQuotaService
    {
        public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default) =>
            Task.FromResult<QuotaResult>(new QuotaResult.Ok(new Consumed(Remaining: 9999, ResetsAt: context.Now.AddDays(1))));
    }
}
