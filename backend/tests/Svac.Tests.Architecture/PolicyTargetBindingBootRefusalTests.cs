using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// PHASE_2A_SUBSTRATE.md §1/§3a's extraction-point proof, fail-closed BOTH directions, red-fixture-proven,
/// using the exact TestHost pattern <see cref="PolicyBootRefusalTests"/> already established: a
/// resource-scoped row whose endpoint conveys no target, or an action-scoped row whose endpoint conveys
/// one it shouldn't, refuses to boot — forever, for every future slice, not by convention.
/// </summary>
public sealed class PolicyTargetBindingBootRefusalTests
{
    [Fact]
    public void SelfOnlyRow_EndpointBindsNone_RefusesToBoot()
    {
        var app = BuildApp(new[] { SelfOnlyFixtureRow() });
        app.MapPost("/canary/self-only-unbound", () => Results.Ok())
            .RequirePolicyAction("fixture.self.only", PolicyTargetBinding.None); // WRONG: row demands SelfOnly.

        app.RequireMutationsPolicyMapped();
        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireTargetBindingConsistent());
        Assert.Contains("4A boot refusal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("binds PolicyTargetBinding.None", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedResourceRow_EndpointBindsNone_RefusesToBoot()
    {
        var app = BuildApp(new[] { OwnedResourceFixtureRow() });
        app.MapDelete("/canary/owned-unbound", () => Results.Ok())
            .RequirePolicyAction("fixture.owned.resource", PolicyTargetBinding.None); // WRONG.

        app.RequireMutationsPolicyMapped();
        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireTargetBindingConsistent());
        Assert.Contains("binds PolicyTargetBinding.None", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionScopedRow_EndpointBindsSelfAccount_RefusesToBoot_TheReverseDirection()
    {
        var app = BuildApp(new[] { ActionScopedFixtureRow() });
        app.MapPost("/canary/action-scoped-overbound", () => Results.Ok())
            .RequirePolicyAction("fixture.action.scoped", PolicyTargetBinding.SelfAccount); // WRONG: row is ActionScoped.

        app.RequireMutationsPolicyMapped();
        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireTargetBindingConsistent());
        Assert.Contains("is ActionScoped/unset but the endpoint conveys", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromRoute_ParamAbsentFromRoutePattern_RefusesToBoot()
    {
        var app = BuildApp(new[] { OwnedResourceFixtureRow() });
        // Route pattern names "{deviceId}", but the binding names "sessionId" — mismatch.
        app.MapDelete("/canary/owned/{deviceId}", () => Results.Ok())
            .RequirePolicyAction("fixture.owned.resource", PolicyTargetBinding.FromRoute("sessionId", "session"));

        app.RequireMutationsPolicyMapped();
        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireTargetBindingConsistent());
        Assert.Contains("absent from the endpoint's own route pattern", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromRoute_ParamPresentInRoutePattern_And_MatchingTargetRule_BootsCleanly()
    {
        var app = BuildApp(new[] { OwnedResourceFixtureRow() });
        app.MapDelete("/canary/owned/{sessionId}", () => Results.Ok())
            .RequirePolicyAction("fixture.owned.resource", PolicyTargetBinding.FromRoute("sessionId", "session"));

        app.RequireMutationsPolicyMapped();
        app.RequireTargetBindingConsistent(); // does not throw.
    }

    [Fact]
    public void SelfOnlyRow_EndpointBindsSelfAccount_BootsCleanly()
    {
        var app = BuildApp(new[] { SelfOnlyFixtureRow() });
        app.MapPost("/canary/self-only-bound", () => Results.Ok())
            .RequirePolicyAction("fixture.self.only", PolicyTargetBinding.SelfAccount);

        app.RequireMutationsPolicyMapped();
        app.RequireTargetBindingConsistent(); // does not throw.
    }

    [Fact]
    public void ActionScopedRow_EndpointBindsNone_BootsCleanly_TheS1S2Shape()
    {
        var app = BuildApp(new[] { ActionScopedFixtureRow() });
        app.MapPost("/canary/action-scoped-bound-none", () => Results.Ok())
            .RequirePolicyAction("fixture.action.scoped"); // the existing no-arg overload = None.

        app.RequireMutationsPolicyMapped();
        app.RequireTargetBindingConsistent(); // does not throw — exactly what every S1/S2 row does today.
    }

    [Fact]
    public void RealCorePolicyTableSource_EveryRowIsActionScopedAndBindsNone_ByConstruction()
    {
        // No endpoints map the real core rows in this TestHost, so RequireTargetBindingConsistent has
        // nothing to check here directly — this instead pins the DATA fact the byte-identical proof rests
        // on: every S1/S2 row is ActionScoped (TargetRule null), so the boot-refusal logic above is a
        // structural no-op against the real table until a resource-scoped row exists (S3).
        var table = new PolicyTable();
        Assert.All(table.Entries, e => Assert.True(e.TargetRule is null or TargetRule.ActionScopedRule));
    }

    private static PolicyTableEntry SelfOnlyFixtureRow() => new(
        Action: "fixture.self.only",
        ActorKinds: new HashSet<ActorKind> { ActorKind.User },
        Axes: PolicyAxis.None,
        DenyMode: PolicyDenyMode.DenyAsAbsence,
        RequiresReason: false,
        ReasonKey: "fixture.none",
        TargetRule: TargetRule.SelfOnly);

    private static PolicyTableEntry OwnedResourceFixtureRow() => new(
        Action: "fixture.owned.resource",
        ActorKinds: new HashSet<ActorKind> { ActorKind.User },
        Axes: PolicyAxis.None,
        DenyMode: PolicyDenyMode.DenyAsAbsence,
        RequiresReason: false,
        ReasonKey: "fixture.none",
        TargetRule: TargetRule.OwnedResource("session"));

    private static PolicyTableEntry ActionScopedFixtureRow() => new(
        Action: "fixture.action.scoped",
        ActorKinds: new HashSet<ActorKind> { ActorKind.User },
        Axes: PolicyAxis.None,
        DenyMode: PolicyDenyMode.DenyAsAbsence,
        RequiresReason: false,
        ReasonKey: "fixture.none");

    private static WebApplication BuildApp(IReadOnlyList<PolicyTableEntry> rows)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPolicyTable>(new FixtureTable(rows));
        return builder.Build();
    }

    private sealed class FixtureTable(IReadOnlyList<PolicyTableEntry> entries) : IPolicyTable
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = entries;
        public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);
    }
}
