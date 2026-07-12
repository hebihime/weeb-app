using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AimlRouter.Config;
using Svac.AimlRouter.DependencyInjection;
using Svac.AimlRouter.Providers;
using Svac.AimlRouter.Routing;
using Svac.DomainCore.Config;
using Svac.DomainCore.Contracts;
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
/// ADVERSARIAL trust-boundary lens, S2 pass (money-doors fail closed · never-pay-to-rank · DTO trust
/// absence) — the S2 sibling of <see cref="TrustBoundaryLensTests"/> (the S1 pass, whose BREAK 1/BREAK 2
/// have since been fixed: ProdFieldKeyVaultGuard now allowlists Development by name, and
/// L19NeverPayToRankArchTest exists). Every [Fact] here asserts the SECURE behavior SLICE_S2_CONTRACT.md
/// itself promises, and FAILS against the shipped S2 code — each one demonstrates a hole, not a
/// hypothetical.
///
/// Run just this file:
///   dotnet test backend/tests/Svac.Tests.Architecture \
///     --filter "FullyQualifiedName~TrustBoundaryLensS2"
/// </summary>
public sealed class TrustBoundaryLensS2Tests
{
    // -----------------------------------------------------------------------------------------------
    // BREAK 2 — MODEL-DOOR GUARD REGRESSES TRUST-F1: AnthropicApiKeyGuard trusts a bare DevSeams
    // boolean and cannot see the environment at all.
    //
    // SECURITY_REVIEW_S1.md Trust-F1 (fixed in ProdFieldKeyVaultGuard.cs:14-27) ratified the law:
    // "fail-closed means ALLOWLIST the one safe environment (Development), never BLOCKLIST / never
    // trust a flag" — the fixed S1 guard takes `environmentName` and allowlists exactly "Development".
    // S2's brand-new model-door guard (AnthropicApiTransport.cs, AnthropicApiKeyGuard.Enforce) shipped
    // THE SAME DAY with the pre-fix shape: `Enforce(bool policyCanReachApiTransport, bool
    // devSeamsEnabled, bool apiKeyConfigured)` and `if (devSeamsEnabled) return;`.
    //
    // Inputs -> wrong result: a Production or Staging host that (mis)sets SVAC_DEVSEAMS_ENABLED=true
    // calls Enforce(true, devSeamsEnabled: true, apiKeyConfigured: false) -> returns normally, and
    // AddAimlRouter(devSeamsEnabled: true) (AimlRouterServiceCollectionExtensions.cs:27-30) registers
    // the keyless local-process AnthropicLocalTransport into that environment's DI. The guard cannot
    // refuse it because it cannot even see which environment it is in — exactly the boolean collapse
    // Trust-F1 outlawed. (DevSeamsNotInProdDiTests.cs covers only AddDomainCore's IPaymentService
    // family, never IModelProvider, so no arch rule catches this either — the §1b claim "arch-tested
    // never-in-prod (IPaymentService family)" is unfulfilled for the router's transports.)
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public void ModelDoorGuard_MustSeeTheEnvironmentName_TrustF1Parity()
    {
        var enforce = typeof(AnthropicApiKeyGuard).GetMethod(nameof(AnthropicApiKeyGuard.Enforce), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(enforce);

        var takesEnvironmentName = enforce!.GetParameters().Any(p =>
            p.ParameterType == typeof(string) &&
            p.Name is not null &&
            p.Name.Contains("environment", StringComparison.OrdinalIgnoreCase));

        Assert.True(
            takesEnvironmentName,
            "AnthropicApiKeyGuard.Enforce takes only booleans (policyCanReachApiTransport, devSeamsEnabled, " +
            "apiKeyConfigured) — it cannot allowlist the one safe environment (Development) by NAME the way " +
            "SECURITY_REVIEW_S1.md Trust-F1 required of ProdFieldKeyVaultGuard. `if (devSeamsEnabled) return;` " +
            "means Production with SVAC_DEVSEAMS_ENABLED=true skips the key check entirely and " +
            "AddAimlRouter(devSeamsEnabled:true) wires the keyless local-process transport into prod DI. " +
            "The model door must be fail-closed by environment allowlist, not by trusting a flag.");
    }

    // -----------------------------------------------------------------------------------------------
    // BREAK 3 — FAIL-CLOSED KEY LAW FIRES AT FIRST RESOLUTION, NOT AT STARTUP.
    //
    // SLICE_S2_CONTRACT.md §1b: "in prod, if the resolved policy can reach the `anthropic` API transport
    // and no key is configured, the host THROWS AT STARTUP." §8 seam row: "prod + policy-reachable API
    // transport + no Key Vault key = startup throw; ... tested." The shipped wiring
    // (AimlRouterServiceCollectionExtensions.cs:32-37) calls AnthropicApiKeyGuard.Enforce inside the
    // IModelProvider SINGLETON FACTORY LAMBDA — DI never executes factory lambdas at build/validate
    // time, so a keyless production composition boots clean and the throw surfaces as a per-request
    // 500 on the first InvokeAsync instead of a loud boot failure.
    //
    // Inputs -> wrong result: AddAimlRouter(devSeamsEnabled: false, anthropicApiKey: null) +
    // BuildServiceProvider(ValidateOnBuild: true) -> NO exception (host is "up", door swings open into
    // a runtime crash later) where the contract demands the composition itself refuses to boot.
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public void ModelDoor_KeylessProdComposition_MustFailAtBuild_NeverAtFirstResolution()
    {
        var services = new ServiceCollection();
        // The three domain-core seams AimlRouterService needs; stubs, never invoked during build.
        services.AddSingleton<IConfigRegistry, StubConfigRegistry>();
        services.AddSingleton<IQuotaService, StubQuotaService>();
        services.AddSingleton<IEventStore, StubEventStore>();

        services.AddAimlRouter(devSeamsEnabled: false, anthropicApiKey: null);

        var ex = Record.Exception(() =>
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        });

        Assert.NotNull(ex); // FAILS today: the guard lives inside a factory lambda, so a keyless prod
                            // composition builds clean and the L18-analog "startup throw" (§1b/§8) never
                            // happens at startup — it becomes the first consumer request's 500.
    }

    // -----------------------------------------------------------------------------------------------
    // BREAK 4 — DTO TRUST SCAN NOT EXTENDED: provider*/model*/payload_class patterns are missing.
    //
    // SLICE_S2_CONTRACT.md §1b: "an arch scan (extends S1's L20 trust-DTO rule with provider*/model*/
    // payload_class patterns) proves neither the receipt nor provider identity ever serializes into a
    // user-bound DTO. A reported user can never probe moderation-provider health." §8 repeats it
    // ("trust-DTO arch scan extended with provider*/model*/payload_class") and §10.2 lists "DTO trust
    // scan" as sign-off evidence. TrustDtoArchTest.cs:22-25 still carries the S1 pattern verbatim
    // (verification|reputation|premium|moderation_state|age*|trust|tier) — zero coverage of Provider,
    // Model, or PayloadClass. The first future response DTO that echoes RoutingReceipt.Provider back to
    // a client ships with no gate firing.
    //
    // Inputs -> wrong result: a DTO property named "Provider"/"Model"/"PayloadClass" -> TrustFieldPattern
    // does not match -> zero violations reported, where the S2-extended scan must flag all three.
    // -----------------------------------------------------------------------------------------------
    [Fact]
    public void TrustDtoScan_MustCoverProviderModelPayloadClass_PerS2Contract1b()
    {
        var patternField = typeof(TrustDtoArchTest).GetField("TrustFieldPattern", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(patternField);
        var pattern = Assert.IsType<Regex>(patternField!.GetValue(null));

        // The §1b-promised extension, checked against both wire-shape and C# property spellings the
        // existing scan already normalizes for (TrustDtoArchTest's own "_?" convention).
        string[] receiptIdentityFields = { "Provider", "Model", "PayloadClass", "payload_class", "provider_id", "model_name" };

        var uncovered = receiptIdentityFields.Where(f => !pattern.IsMatch(f)).ToList();

        Assert.True(
            uncovered.Count == 0,
            "TrustDtoArchTest.TrustFieldPattern was never extended with the provider*/model*/payload_class " +
            $"patterns SLICE_S2_CONTRACT.md §1b/§8/§10.2 promise — uncovered: [{string.Join(", ", uncovered)}]. " +
            "RoutingReceipt/provider identity leaking into a user-bound DTO (the 'reported user probes " +
            "moderation-provider health' vector) has no gate today.");
    }

    private sealed class StubConfigRegistry : IConfigRegistry
    {
        public Task<T> GetValue<T>(string key, CancellationToken ct = default) => throw new NotSupportedException("never invoked at composition time");
        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ConfigEntryView>> ListEntries(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubQuotaService : IQuotaService
    {
        public Task<QuotaResult> Consume(ActorRef actor, string quotaKey, QuotaContext context, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubEventStore : IEventStore
    {
        public Task<RecordedEvent> Append(StreamType stream, string streamId, string eventType, string? payloadJson, RequestContext ctx, ExpectedVersion expectedVersion, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RecordedEvent> Reverse(StreamType stream, string eventId, string reason, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Tombstone(StreamType stream, string eventId, string purgeClass, RequestContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<RecordedEvent> ReadStream(StreamType stream, string streamId, long fromSeq = 0, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Replay(StreamType stream, string consumerId, IProjection projection, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

/// <summary>
/// BREAK 1 — THE §4 SET-TIME BOUNDS MECHANISM DOES NOT EXIST: ConfigRegistry.SetValue saves ANYTHING.
///
/// SLICE_S2_CONTRACT.md §4 is explicit that this is S2's adopted mechanism, not a nice-to-have:
/// "Set-time bounds validation does the ruling's work structurally: a `routing_policy` naming a provider
/// absent from the allowlist, or a model absent from that provider's declared `models` list, or routing
/// a payload class above a provider's ceiling, or whose `default_chain[0]` resolves to `family !=
/// \"claude\"` FAILS BOUNDS AT SetValue — an unlawful or non-Claude-default policy cannot be *saved*,
/// not merely not-served. ... A second bounds rule refuses saving any allowlist entry with
/// `special_category_ok: true` until S17 exists (two locks with the refuse-all authorizer)." §10.2
/// lists "9A bounds rejections (unregistered provider, undeclared model, ceiling violation, non-Claude
/// default, special_category_ok)" as gate-suite sign-off evidence.
///
/// Shipped reality: ConfigRegistry.SetValue (backend/domain-core/Svac.DomainCore/Config/
/// ConfigRegistry.cs:27-47) checks key existence and the reason string, then serializes whatever it was
/// handed. The config_entries `bounds` column exists but is ALWAYS null (ConfigSeedLoader.cs:61) and no
/// code path reads it. No type anywhere in Svac.DomainCore or Svac.AimlRouter implements a bounds rule.
/// So: one of the two "independent locks" on special-category egress (§1b) does not exist, the
/// never-non-Claude-default CHECK does not exist, and `aiml.invoke_timeout_seconds`'s declared bounds
/// [5, 300] bind nothing. These Facts run the exact ops-desk Set the contract says "cannot be saved"
/// and assert the save throws. Every one FAILS today: the save succeeds and is audited as legitimate.
/// </summary>
public sealed class TrustBoundaryLensS2ConfigDoorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = NewDb();
        await db.Database.MigrateAsync();

        var eventStore = new PostgresEventStore(db);
        await new ConfigSeedLoader(db, eventStore).SeedFromFile(AimlManifestPath(), SystemCtx("aiml-seed"));
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private static RequestContext SystemCtx(string correlationId) =>
        RequestContext.System(new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System), correlationId);

    private static ActorRef StaffActor() =>
        new(OpaqueId.New(IdPrefixes.Staff, DateTimeOffset.UtcNow, Random.Shared), ActorKind.Staff);

    private static readonly string[] LlmKinds = { "llm" };
    private static readonly string[] OpusModels = { "claude-opus-4-8" };

    private static string AimlManifestPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docker-compose.yml")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!, "backend", "modules", "AimlRouter", "config", "aiml-router.config.json");
    }

    [Fact]
    public async Task SetValue_RoutingPolicyWithNonClaudeDefaultChain_MustFailBounds_NeverBeSaved()
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        // The exact §4 forbidden save: default_chain[0] resolves to family != "claude" AND names a
        // provider absent from the allowlist. CORRECTION 1 (§13) doubles down: "The §4 bounds rule
        // already enforces family==\"claude\" on default_chain[0]".
        var unlawful = new RoutingPolicy(
            Version: 2,
            DefaultChain: new[] { new TaskChainLink("openai", "gpt-5") },
            TaskChains: new Dictionary<string, IReadOnlyList<TaskChainLink>>(),
            ResidencyOverrides: Array.Empty<ResidencyOverride>());

        var ex = await Record.ExceptionAsync(() => registry.SetValue(
            AimlRouterConfigKeys.RoutingPolicy, unlawful, "ops-desk edit", StaffActor(), SystemCtx("ops-edit")));

        Assert.NotNull(ex); // FAILS today: the non-Claude, non-allowlisted default policy SAVES fine and
                            // is audited as a legitimate config change. §4: it "cannot be *saved*".
    }

    [Fact]
    public async Task SetValue_AllowlistEntryWithSpecialCategoryOkTrue_MustBeRefusedUntilS17()
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        var unlawful = new[]
        {
            new ProviderAllowlistEntry(
                Name: "anthropic", Family: "claude", Kinds: LlmKinds,
                PayloadClassCeiling: Svac.AimlRouter.Contracts.PayloadClass.SpecialCategory,
                DpaSigned: false,
                SpecialCategoryOk: true, // §1b/§4: refuse saving this until S17's consent ledger exists.
                Residency: "global", Models: OpusModels),
        };

        var ex = await Record.ExceptionAsync(() => registry.SetValue(
            AimlRouterConfigKeys.ProviderAllowlist, unlawful, "founder edit", StaffActor(), SystemCtx("founder-edit")));

        Assert.NotNull(ex); // FAILS today: special_category_ok:true saves fine — the SECOND of the "two
                            // independent locks" (§1b) on special-category egress does not exist; only
                            // the RefuseAllSpecialCategoryAuthorizer lock is real.
    }

    [Fact]
    public async Task SetValue_TimeoutOutsideDeclaredBounds_MustFailBounds()
    {
        using var db = NewDb();
        var registry = new ConfigRegistry(db, new PostgresEventStore(db));

        // §4 declares bounds [5, 300] for aiml.invoke_timeout_seconds; the manifest's consumer note
        // repeats them. 0 is outside them (and turns every provider call into an instant cancellation).
        var ex = await Record.ExceptionAsync(() => registry.SetValue(
            AimlRouterConfigKeys.InvokeTimeoutSeconds, 0, "ops edit", StaffActor(), SystemCtx("ops-edit")));

        Assert.NotNull(ex); // FAILS today: the bounds column is seeded null (ConfigSeedLoader.cs:61),
                            // never read, and SetValue validates nothing numeric — [5, 300] binds nothing.
    }
}
