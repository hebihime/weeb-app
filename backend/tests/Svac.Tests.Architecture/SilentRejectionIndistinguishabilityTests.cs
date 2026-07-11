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
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The negative proof BUILD.md §9 and SLICE_PLAYBOOK.md's Phase-3 lens both name: "a deny, a void, an
/// exclusion, or a tier floor must be unobservable: no distinct error code, timing, or state diff"
/// (SLICE_S1_CONTRACT.md §8: "Silent rejection unobservable ... identical status, identical body, same
/// code path"). This drives REAL HTTP requests (ephemeral loopback port, same pattern as
/// OpenApiContractEmitter.Run — never a canary shipped in Svac.PublicApi itself, per §1c) at two
/// deliberately different PolicyDecision deny modes (DenyAsAbsence vs DenySilentAs404) that the contract
/// says must render byte-identically, and proves they do — not by code inspection, by an actual diff of
/// two real response bytes.
/// </summary>
public sealed class SilentRejectionIndistinguishabilityTests : IAsyncLifetime, IDisposable
{
    private WebApplication? _app;
    private HttpClient? _client;

    public void Dispose() => _client?.Dispose();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IPolicyTable>(new FixturePolicyTable());
        builder.Services.AddScoped<IPolicyEngine, FixturePolicyEngine>();
        // RequestContextMiddleware (mounted below) needs a real IRegionResolver — the DevSeams
        // deterministic seam is the correct real dependency here (§9), not a hand-rolled fixture double.
        builder.Services.AddSingleton<Svac.DomainCore.Contracts.Region.IRegionResolver, Svac.DomainCore.Region.DevSeamsRegionResolver>();
        builder.Services.AddSvacHosting();

        _app = builder.Build();
        _app.UseSvacRequestContext();
        // Canary-only routes (never shipped in the real host, §1c) exercising the three closed-union
        // deny modes that must render through PolicyEnforcementFilter's shared shapes.
        _app.MapPost("/canary/absence", () => Results.Ok()).RequirePolicyAction("fixture.absence");
        _app.MapPost("/canary/silent404", () => Results.Ok()).RequirePolicyAction("fixture.silent404");
        _app.MapPost("/canary/limit", () => Results.Ok()).RequirePolicyAction("fixture.limit");
        _app.MapPost("/canary/allowed", () => Results.Ok()).RequirePolicyAction("fixture.allowed");

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
    public async Task DenyAsAbsence_AndDenySilentAs404_RenderByteIdenticalResponses()
    {
        var absenceResponse = await _client!.PostAsync("/canary/absence", content: null);
        var silent404Response = await _client!.PostAsync("/canary/silent404", content: null);

        Assert.Equal(absenceResponse.StatusCode, silent404Response.StatusCode);
        var absenceBody = await absenceResponse.Content.ReadAsStringAsync();
        var silent404Body = await silent404Response.Content.ReadAsStringAsync();
        Assert.Equal(absenceBody, silent404Body); // byte-identical (empty) body — no distinguishing field ever leaks.
        Assert.Equal(
            absenceResponse.Content.Headers.ContentType?.ToString(),
            silent404Response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task DenyAsAbsence_IsIndistinguishableFromANonexistentRoute()
    {
        // Token law 3 (absence, not disablement): a policy-excluded resource and a resource that never
        // existed must be the SAME response — this compares the canary's deny response against ASP.NET's
        // own 404 for a route that was simply never mapped.
        var deniedResponse = await _client!.PostAsync("/canary/absence", content: null);
        var trulyMissingResponse = await _client!.PostAsync("/this-route-was-never-mapped-anywhere", content: null);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, deniedResponse.StatusCode);
        Assert.Equal(trulyMissingResponse.StatusCode, deniedResponse.StatusCode);
        Assert.Equal(await trulyMissingResponse.Content.ReadAsStringAsync(), await deniedResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DenyAsLimit_RendersTheOneLimitReachedShape_Never404()
    {
        // The one exception to "renders like absence": DenyAsLimit is a DIFFERENT, deliberately visible
        // shape (token law 4 — the single LimitReached surface a user IS meant to see) — proving it does
        // NOT collapse into the same 404 path as the silent denies confirms PolicyEnforcementFilter
        // dispatches on the closed union correctly, not by accident.
        var response = await _client!.PostAsync("/canary/limit", content: null);
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fixture.limit.key", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allow_ExecutesTheRealHandler_NeverShortCircuited()
    {
        var response = await _client!.PostAsync("/canary/allowed", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Fixture-only IPolicyTable (never referenced outside this test) — an empty real table would make RequireMutationsPolicyMapped's boot check pass vacuously for these canary routes without covering the deny-mode dispatch this file actually tests.</summary>
    private sealed class FixturePolicyTable : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[]
        {
            new PolicyTableEntry("fixture.absence", new HashSet<ActorKind> { ActorKind.Anonymous }, PolicyAxis.None, PolicyDenyMode.DenyAsAbsence, RequiresReason: false, ReasonKey: "fixture.none"),
            new PolicyTableEntry("fixture.silent404", new HashSet<ActorKind> { ActorKind.Anonymous }, PolicyAxis.None, PolicyDenyMode.DenySilentAs404, RequiresReason: false, ReasonKey: "fixture.none"),
            new PolicyTableEntry("fixture.limit", new HashSet<ActorKind> { ActorKind.Anonymous }, PolicyAxis.None, PolicyDenyMode.DenyAsLimit, RequiresReason: false, ReasonKey: "fixture.none", QuotaKeyForLimit: "fixture.limit.key"),
            new PolicyTableEntry("fixture.allowed", new HashSet<ActorKind> { ActorKind.Anonymous, ActorKind.User, ActorKind.System, ActorKind.Staff, ActorKind.Partner }, PolicyAxis.None, PolicyDenyMode.DenyAsAbsence, RequiresReason: false, ReasonKey: "fixture.none"),
        };

        public PolicyTableEntry? Find(string action) => Entries.SingleOrDefault(e => e.Action == action);
    }

    /// <summary>
    /// Fixture-only IPolicyEngine: every fixture action is DENIED per its declared mode except
    /// "fixture.allowed", which the real PolicyEnforcementFilter must actually execute rather than deny —
    /// this is a deny-MODE dispatch test, not a re-test of PolicyEngineTests' actor-kind matching.
    /// </summary>
    private sealed class FixturePolicyEngine : IPolicyEngine
    {
        public Task<PolicyDecision> Authorize(ActorRef actor, string action, TargetRef target, CancellationToken ct = default) => Task.FromResult(action switch
        {
            "fixture.absence" => PolicyDecision.AsAbsence,
            "fixture.silent404" => PolicyDecision.Silent404,
            "fixture.limit" => PolicyDecision.AsLimit("fixture.limit.key"),
            "fixture.allowed" => PolicyDecision.Allowed,
            _ => PolicyDecision.Standard("fixture.unmapped"),
        });
    }
}
