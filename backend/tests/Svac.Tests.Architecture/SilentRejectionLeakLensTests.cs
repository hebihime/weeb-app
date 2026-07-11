using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// ADVERSARIAL LENS: silent-rejection leaks. SLICE_S1_CONTRACT.md §8 promises "a deny, a void, an
/// exclusion, or a tier floor must be unobservable: no distinct error code, timing, or state diff",
/// "excluded read ≡ nonexistent read", and (§1b) "DenyStandard is legal only for staff/partner actor
/// kinds; an arch test fails any policy entry mapping a consumer actor to DenyStandard on a read path".
///
/// These tests are written to FAIL against the current running code. Each one demonstrates a concrete
/// leak: a consumer, or an off-platform wire observer, can tell an exclusion apart from genuine absence.
/// </summary>
public sealed class SilentRejectionLeakLensTests
{
    private static ActorRef Consumer() =>
        new(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared), ActorKind.User);

    // ------------------------------------------------------------------------------------------------
    // LEAK 1 — DenyFor() applies the row's declared mode to WHOEVER is denied, ignoring the denied
    // actor's kind. Every DenyStandard row in the shipped PolicyTable therefore hands a consumer an
    // observable DenyStandard (→ HTTP 403 + reason key + correlation id via PolicyResults.Standard),
    // instead of the silent absence §8 requires. PolicyEngine.cs:26-28 → DenyFor → PolicyEngine.cs:43.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task ShippedTable_NeverHandsAConsumerAnObservableDenyStandard()
    {
        var engine = new PolicyEngine(new PolicyTable());
        var consumer = Consumer();

        var leaks = new List<string>();
        foreach (var entry in new PolicyTable().Entries)
        {
            var decision = await engine.Authorize(consumer, entry.Action, TargetRef.ForAction(entry.Action));
            if (decision is PolicyDecision.DenyStandard standard)
            {
                // A consumer just learned this action EXISTS and its policy reason — an observable
                // exclusion. §8: it should have been indistinguishable from a resource that never existed.
                leaks.Add($"{entry.Action} -> DenyStandard(\"{standard.ReasonKey}\")");
            }
        }

        Assert.True(
            leaks.Count == 0,
            "Consumer received an observable DenyStandard for these actions (silent-rejection leak, §8/§1b):\n  - "
                + string.Join("\n  - ", leaks));
    }

    // ------------------------------------------------------------------------------------------------
    // LEAK 2 — the guard that is supposed to enforce "no consumer actor mapped to DenyStandard on a read
    // path" (PolicyEngineTests.FindConsumerDenyStandardViolations) only inspects rows that EXPLICITLY
    // list a consumer actor kind. A staff-only read row (actorKinds={Staff}, DenyStandard) denies
    // consumers by OMISSION — the guard passes it, yet PolicyEngine hands a consumer DenyStandard. This
    // is exactly the mapping §1b says an arch test must fail, undetected.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task StaffOnlyDenyStandardReadRow_DeniesConsumerObservably_AndGuardMissesIt()
    {
        // A plausible future read-path row: a staff-only "reversal preview" style read.
        var readRow = new PolicyTableEntry(
            Action: "core.ledger.reverse.preview",
            ActorKinds: new HashSet<ActorKind> { ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.core_ledger_reverse_preview");

        // (a) The guard's own logic (consumer actor kind LISTED on the row) — it does NOT flag this row.
        var consumerKinds = new HashSet<ActorKind> { ActorKind.User, ActorKind.Anonymous };
        var guardFlagged = readRow.ActorKinds.Any(k => consumerKinds.Contains(k));
        Assert.False(guardFlagged); // guard is blind to deny-by-omission — sanity, this passes.

        // (b) But the engine hands a consumer DenyStandard for that same row → observable exclusion.
        var engine = new PolicyEngine(new SingleRowTable(readRow));
        var decision = await engine.Authorize(Consumer(), readRow.Action, TargetRef.ForAction(readRow.Action));

        Assert.IsNotType<PolicyDecision.DenyStandard>(decision); // FAILS: it IS DenyStandard.
    }

    // ------------------------------------------------------------------------------------------------
    // LEAK 3 — the unmapped-action fail-closed path returns DenyStandard for ANY actor kind, including a
    // consumer (PolicyEngine.cs:23). Reachable on READ endpoints because the boot-refusal check exempts
    // GET/HEAD (StartupPolicyCoverage.cs:41-45): a policy-gated GET whose action was typo'd or later
    // removed from the table boots cleanly, then leaks a 403 to consumers instead of absence.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task UnmappedActionFailClosed_HandsConsumerDenyStandard_NotAbsence()
    {
        var engine = new PolicyEngine(new PolicyTable());
        var decision = await engine.Authorize(Consumer(), "core.some.readpath.typo", TargetRef.ForAction("x"));

        Assert.IsNotType<PolicyDecision.DenyStandard>(decision); // FAILS: unmapped → DenyStandard for a consumer.
    }

    private sealed class SingleRowTable(PolicyTableEntry row) : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[] { row };
        public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);
    }
}

/// <summary>
/// LEAK 4 — RETIRED at S3 (SLICE_S3_CONTRACT.md §3a/§13, deferred finding SilentRej-L4). The original
/// finding: §8 claims silent rejection is "same code path", but a plain ActionScoped DenyAsAbsence row
/// short-circuits in PolicyEnforcementFilter BEFORE any lookup runs, while a genuinely-absent resource's
/// 404 came from a handler that DID run a lookup first — an observer timing "lookup ran vs didn't" could
/// distinguish exclusion from genuine absence for THAT shape of row.
///
/// §3a's OwnedResource mechanism (PHASE_2A_SUBSTRATE.md §1, landed with S3) retires this finding for
/// every OwnedResource-gated route, not by adding a fix INSIDE the filter, but by making the ownership
/// check ITSELF the handler-equivalent work: <c>IResourceOwnershipResolver.OwnerOf</c> IS "the same single
/// indexed fetch the handler would do" (§3a's own words) — it runs for BOTH a foreign resource id
/// (belongs to someone else) and a genuinely nonexistent one (belongs to no one), and PolicyEngine folds
/// both outcomes into the identical DenyAsAbsence branch. There is exactly ONE code path — one resolver
/// call, zero handler executions — for foreign and nonexistent alike; the DELETE /v1/me/sessions/{sessionId}
/// exemplar route (§3, AuthIdorLensTests' third finding) is built on exactly this resolver shape. This
/// test now proves that non-vacuously: a real PolicyEngine + a counting IResourceOwnershipResolver show
/// the resolver is invoked exactly once for each of a foreign id and a missing id, the handler runs for
/// neither, and the two HTTP responses are byte-identical — the timing/side-effect channel the original
/// finding raised is closed for this route shape.
/// </summary>
public sealed class SilentRejectionTimingChannelLensTests : IAsyncLifetime, IDisposable
{
    private WebApplication? _app;
    private HttpClient? _client;
    private int _handlerRuns;
    private CountingWidgetOwnershipResolver? _resolver;

    public void Dispose() => _client?.Dispose();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _resolver = new CountingWidgetOwnershipResolver(ownerOfForeign: OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, new Random(2)));
        var callingActor = new ActorRef(OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, new Random(1)), ActorKind.User);

        builder.Services.AddSingleton<IPolicyTable>(new OwnedWidgetReadTable());
        builder.Services.AddSingleton<IResourceOwnershipResolver>(_resolver);
        builder.Services.AddScoped<IPolicyEngine>(sp => new PolicyEngine(
            sp.GetRequiredService<IPolicyTable>(),
            sp.GetServices<IResourceOwnershipResolver>()));
        builder.Services.AddSingleton<Svac.DomainCore.Contracts.Region.IRegionResolver, Svac.DomainCore.Region.DevSeamsRegionResolver>();
        builder.Services.AddSvacHosting();
        // Overrides AddSvacHosting's AnonymousBearerAuthenticator default (last registration wins for
        // GetRequiredService<T>()) — RequestContextMiddleware is the one real place actor identity gets
        // set into the AMBIENT accessor AddSvacHosting registers; a directly-registered IRequestContextAccessor
        // singleton here would be shadowed by that same ambient registration, exactly the mistake this
        // fixes (a User actor, never Anonymous, is required to reach the OwnedResource axis at all).
        builder.Services.AddSingleton<IBearerAuthenticator>(new FixedBearerAuthenticator(callingActor));

        _app = builder.Build();
        _app.UseSvacRequestContext();

        // Same handler body, same action, same OwnedResource("widget") binding on BOTH routes — only the
        // route id differs (a foreign owner's id vs an id nobody owns). Neither ever increments
        // _handlerRuns if the ownership check denies first, which it does in both cases.
        _app.MapGet("/read/{id}", () =>
        {
            Interlocked.Increment(ref _handlerRuns);
            return Results.NotFound();
        }).RequirePolicyAction("lens.read.owned", PolicyTargetBinding.FromRoute("id", "widget"));

        await _app.StartAsync();
        var addressFeature = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("no server address feature available.");
        _client = new HttpClient { BaseAddress = new Uri(addressFeature.Addresses.First()) };
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
        }
    }

    [Fact]
    public async Task ExcludedRead_AndGenuineAbsentRead_TraverseTheSameCodePath()
    {
        var foreign = await _client!.GetAsync("/read/widget-foreign");
        var missing = await _client!.GetAsync("/read/widget-missing");

        // Byte-identical wire response — the pre-existing guarantee, still holds.
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());

        // §3a's actual retirement of SilentRej-L4: the ownership resolver — the handler-equivalent work
        // — ran exactly once for EACH request (foreign owner mismatch and genuinely-unknown id alike),
        // and the real handler body ran for NEITHER. One code path, not two.
        Assert.Equal(2, _resolver!.CallCount);
        Assert.Equal(0, _handlerRuns);
    }

    private sealed class OwnedWidgetReadTable : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[]
        {
            new PolicyTableEntry(
                "lens.read.owned",
                new HashSet<ActorKind> { ActorKind.User },
                PolicyAxis.None,
                PolicyDenyMode.DenyAsAbsence,
                RequiresReason: false,
                ReasonKey: "lens.none",
                TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("widget")),
        };

        public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);
    }

    /// <summary>Resolves "widget-foreign" to a DIFFERENT owner than the calling actor, and "widget-missing" to no owner at all — both outcomes fold into the SAME DenyAsAbsence branch inside PolicyEngine, but only after this resolver call runs exactly once per request.</summary>
    private sealed class CountingWidgetOwnershipResolver(OpaqueId ownerOfForeign) : IResourceOwnershipResolver
    {
        private int _callCount;
        public int CallCount => _callCount;
        public string ResourceType => "widget";

        public Task<OpaqueId?> OwnerOf(string resourceId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(resourceId == "widget-foreign" ? (OpaqueId?)ownerOfForeign : null);
        }
    }

    private sealed class FixedBearerAuthenticator(ActorRef actor) : IBearerAuthenticator
    {
        public Task<AuthenticatedActor?> Authenticate(HttpContext httpContext, CancellationToken ct = default) =>
            Task.FromResult<AuthenticatedActor?>(new AuthenticatedActor(actor, AccountState: null, Region: null, Locale: null));
    }
}
