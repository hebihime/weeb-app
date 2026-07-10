using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Hosting;
using Svac.DomainCore.Policy;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// B1's boot-refusal proof (SLICE_S1_CONTRACT.md §3, §10.3): the host refuses to boot if any non-GET
/// endpoint lacks a [PolicyAction] mapping to a table row. The canary POST endpoint lives ONLY here —
/// "never shipped in the contract" (§1c) — so the real Svac.PublicApi never carries a test-only route.
/// </summary>
public sealed class PolicyBootRefusalTests
{
    [Fact]
    public void UnmappedMutationEndpoint_RefusesToBoot()
    {
        var app = BuildMinimalApp();
        app.MapPost("/canary/unmapped", () => Results.Ok()); // deliberately NO .RequirePolicyAction(...)

        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireMutationsPolicyMapped());
        Assert.Contains("4A boot refusal", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/canary/unmapped", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MappedMutationEndpoint_BootsCleanly()
    {
        var app = BuildMinimalApp();
        app.MapPost("/canary/mapped", () => Results.Ok())
            .RequirePolicyAction("core.ledger.append"); // a real row from PolicyTable

        // Does not throw.
        app.RequireMutationsPolicyMapped();
    }

    [Fact]
    public void MappedButUnregisteredAction_RefusesToBoot()
    {
        var app = BuildMinimalApp();
        app.MapPost("/canary/ghost-action", () => Results.Ok())
            .RequirePolicyAction("core.this.action.does.not.exist");

        var ex = Assert.Throws<InvalidOperationException>(() => app.RequireMutationsPolicyMapped());
        Assert.Contains("has no PolicyTable row", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetEndpoints_NeverRequirePolicyMapping()
    {
        var app = BuildMinimalApp();
        app.MapGet("/canary/read", () => Results.Ok()); // no policy — GET is exempt from the boot check

        // Does not throw — GET/HEAD endpoints are never mutation-class.
        app.RequireMutationsPolicyMapped();
    }

    private static WebApplication BuildMinimalApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPolicyTable, PolicyTable>();
        return builder.Build();
    }
}
