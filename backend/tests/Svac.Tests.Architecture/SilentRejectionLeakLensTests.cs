using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
    public void ShippedTable_NeverHandsAConsumerAnObservableDenyStandard()
    {
        var engine = new PolicyEngine(new PolicyTable());
        var consumer = Consumer();

        var leaks = new List<string>();
        foreach (var entry in new PolicyTable().Entries)
        {
            var decision = engine.Authorize(consumer, entry.Action, TargetRef.ForAction(entry.Action));
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
    public void StaffOnlyDenyStandardReadRow_DeniesConsumerObservably_AndGuardMissesIt()
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
        var decision = engine.Authorize(Consumer(), readRow.Action, TargetRef.ForAction(readRow.Action));

        Assert.IsNotType<PolicyDecision.DenyStandard>(decision); // FAILS: it IS DenyStandard.
    }

    // ------------------------------------------------------------------------------------------------
    // LEAK 3 — the unmapped-action fail-closed path returns DenyStandard for ANY actor kind, including a
    // consumer (PolicyEngine.cs:23). Reachable on READ endpoints because the boot-refusal check exempts
    // GET/HEAD (StartupPolicyCoverage.cs:41-45): a policy-gated GET whose action was typo'd or later
    // removed from the table boots cleanly, then leaks a 403 to consumers instead of absence.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public void UnmappedActionFailClosed_HandsConsumerDenyStandard_NotAbsence()
    {
        var engine = new PolicyEngine(new PolicyTable());
        var decision = engine.Authorize(Consumer(), "core.some.readpath.typo", TargetRef.ForAction("x"));

        Assert.IsNotType<PolicyDecision.DenyStandard>(decision); // FAILS: unmapped → DenyStandard for a consumer.
    }

    private sealed class SingleRowTable(PolicyTableEntry row) : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[] { row };
        public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);
    }
}

/// <summary>
/// LEAK 4 — timing / code-path channel. §8 claims silent rejection is "same code path (timing-channel
/// mitigation is structural single-path, asserted by test)". It is not. A DenyAsAbsence short-circuits
/// in PolicyEnforcementFilter BEFORE the endpoint delegate runs (PolicyEnforcementFilter.cs:28), while a
/// genuinely-absent resource returns its 404 from INSIDE the handler after the handler's work (a DB miss
/// at later slices). The two 404s are byte-identical on the wire (so the existing test is satisfied) but
/// traverse different code paths — an observer timing "handler ran vs didn't" distinguishes exclusion
/// from genuine absence. No shipped test asserts timing or single-path; this one proves the divergence
/// deterministically via a handler-execution counter (a stand-in for the timing signal).
/// </summary>
public sealed class SilentRejectionTimingChannelLensTests : IAsyncLifetime, IDisposable
{
    private WebApplication? _app;
    private HttpClient? _client;
    private int _genuineHandlerRuns;
    private int _deniedHandlerRuns;

    public void Dispose() => _client?.Dispose();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IPolicyTable>(new AbsenceReadTable());
        builder.Services.AddScoped<IPolicyEngine, AlwaysAbsenceEngine>();
        builder.Services.AddSingleton<Svac.DomainCore.Contracts.Region.IRegionResolver, Svac.DomainCore.Region.DevSeamsRegionResolver>();
        builder.Services.AddSvacHosting();

        _app = builder.Build();
        _app.UseSvacRequestContext();

        // A genuinely-absent read: the handler RUNS (does its lookup work) and returns the same 404 shape.
        _app.MapGet("/read/genuine-missing", () =>
        {
            Interlocked.Increment(ref _genuineHandlerRuns);
            return Results.NotFound();
        });

        // A policy-excluded read: DenyAsAbsence short-circuits in the filter; this handler never runs.
        _app.MapGet("/read/excluded", () =>
        {
            Interlocked.Increment(ref _deniedHandlerRuns);
            return Results.NotFound();
        }).RequirePolicyAction("lens.read.absence");

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

    [Fact(Skip = "deferred: SECURITY_REVIEW_S1.md SilentRej-L4 — excluded reads short-circuit before the handler while genuine 404s run it (PolicyEnforcementFilter.cs:28); fix requires restructuring the filter to traverse handler-equivalent work (constant-path denial), a real design change.")]
    public async Task ExcludedRead_AndGenuineAbsentRead_TraverseTheSameCodePath()
    {
        var excluded = await _client!.GetAsync("/read/excluded");
        var genuine = await _client!.GetAsync("/read/genuine-missing");

        // Payload hiding works — this part is fine and documents the existing guarantee.
        Assert.Equal(genuine.StatusCode, excluded.StatusCode);
        Assert.Equal(await genuine.Content.ReadAsStringAsync(), await excluded.Content.ReadAsStringAsync());

        // §8's actual promise: "same code path". If the handler runs for a genuine-absent read but is
        // short-circuited for an excluded read, the two are timing/side-effect distinguishable.
        Assert.Equal(_genuineHandlerRuns, _deniedHandlerRuns); // FAILS: 1 vs 0 — not the same code path.
    }

    private sealed class AbsenceReadTable : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[]
        {
            new PolicyTableEntry("lens.read.absence", new HashSet<ActorKind> { ActorKind.Staff }, PolicyAxis.None, PolicyDenyMode.DenyAsAbsence, RequiresReason: false, ReasonKey: "lens.none"),
        };

        public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);
    }

    private sealed class AlwaysAbsenceEngine : IPolicyEngine
    {
        public PolicyDecision Authorize(ActorRef actor, string action, TargetRef target) => PolicyDecision.AsAbsence;
    }
}
