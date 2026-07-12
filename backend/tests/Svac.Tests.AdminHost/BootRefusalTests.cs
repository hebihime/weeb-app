using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_S5_CONTRACT.md §1c/§10.1: the admin host's own boot-refusal proofs, mirroring
/// Svac.Tests.Architecture.PolicyBootRefusalTests's exact minimal-composition shape (no Testcontainers —
/// deterministic, &lt;2s gate lane): RequireMutationsPolicyMapped, RequireAdminActionsCovered, and the
/// policy-source duplicate-key union check, each proven BOTH directions (red fixture fails to boot;
/// the real composition boots cleanly).
/// </summary>
public sealed class BootRefusalTests
{
    [Fact]
    public void UnmappedMutationEndpoint_OnTheAdminHost_RefusesToBoot()
    {
        var app = BuildMinimalAdminApp();
        app.MapPost("/canary/unmapped", () => Results.Ok()); // deliberately NO .RequirePolicyAction(...)

        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireMutationsPolicyMapped());
        Assert.Contains("4A boot refusal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/canary/unmapped", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminHostTransportRow_MapsCleanly()
    {
        var app = BuildMinimalAdminApp();
        app.MapPost("/canary/transport", () => Results.Ok())
            .RequirePolicyAction("admin.host.transport");

        // Does not throw — the real §3 row exists.
        app.RequireMutationsPolicyMapped();
    }

    [Fact]
    public void EveryAdminActionKey_ResolvesAgainstTheRealPolicyTable_RequireAdminActionsCovered_BootsCleanly()
    {
        var app = BuildMinimalAdminApp();

        // Does not throw — every AdminActionKeys.All entry has a real AdminPolicyTableSource row.
        app.RequireAdminActionsCovered();
    }

    [Fact]
    public void RedFixture_AnAdminActionKeyWithNoPolicyRow_RefusesToBoot()
    {
        // Builds a composition that registers CorePolicyTableSource ONLY (no AdminPolicyTableSource) —
        // every AdminActionKeys.All entry is therefore unmapped, exactly the state a desk slice would be
        // in if it ever forgot to register its own admin verbs.
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPolicyTableSource, CorePolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTable, PolicyTable>();
        var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireAdminActionsCovered());
        Assert.Contains("4A boot refusal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("admin.staff.provision", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RedFixture_ADuplicateActionKeyAcrossTwoSources_IsABootRefusal()
    {
        // The union's own duplicate-ownership guard (PHASE_2A_SUBSTRATE.md §1: "a duplicate action key
        // across sources ⇒ boot refusal") — proven with a fixture source colliding on a real admin
        // action key, mirroring PolicyTable's own duplicate-key throw tests at S1/S3.
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPolicyTableSource, CorePolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTableSource, AdminPolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTableSource, FixtureCollidingPolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTable, PolicyTable>();

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build().Services.GetRequiredService<IPolicyTable>());
        Assert.Contains("admin.host.transport", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetHealthEndpoint_NeverRequiresPolicyMapping()
    {
        var app = BuildMinimalAdminApp();
        app.MapGet("/health", () => Results.Ok());

        // Does not throw — GET/HEAD endpoints are never mutation-class.
        app.RequireMutationsPolicyMapped();
    }

    private static WebApplication BuildMinimalAdminApp()
    {
        var builder = WebApplication.CreateBuilder();
        // The REAL boot-time union this host actually assembles (AddDomainCore + AddAdminHostModule) —
        // CorePolicyTableSource + AdminPolicyTableSource, exactly like Svac.Tests.Architecture.
        // PolicyBootRefusalTests's own BuildMinimalApp does for CorePolicyTableSource alone.
        builder.Services.AddSingleton<IPolicyTableSource, CorePolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTableSource, AdminPolicyTableSource>();
        builder.Services.AddSingleton<IPolicyTable, PolicyTable>();
        return builder.Build();
    }

    private sealed class FixtureCollidingPolicyTableSource : IPolicyTableSource
    {
        public IReadOnlyList<PolicyTableEntry> Entries { get; } = new[]
        {
            new PolicyTableEntry(
                Action: "admin.host.transport",
                ActorKinds: new HashSet<Svac.DomainCore.Contracts.Ids.ActorKind> { Svac.DomainCore.Contracts.Ids.ActorKind.Staff },
                Axes: PolicyAxis.None,
                DenyMode: PolicyDenyMode.DenyAsAbsence,
                RequiresReason: false,
                ReasonKey: "n/a").Validate(),
        };
    }
}
