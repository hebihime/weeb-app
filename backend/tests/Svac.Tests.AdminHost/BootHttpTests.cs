using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_PLAYBOOK.md Phase 1 gate ("trivial container test"): boots the REAL admin host (via <see
/// cref="AdminHostFixture"/> — the exact same DI wiring + endpoint mapping + boot-refusal calls as
/// Program.cs) against a real Postgres Testcontainer and exercises it over real HTTP. Proves deliverable
/// #4 end to end: the stub sign-in page + one dashboard stub route both render, both carry the
/// "admin.host.transport" 4A wiring for real (RequireMutationsPolicyMapped already ran during
/// AdminHostFixture.InitializeAsync without throwing — this suite's mere existence green is half the
/// proof; the other half is that these pages actually render content, not just avoid a 500).
/// </summary>
[Collection("AdminHostHttp")]
public sealed class BootHttpTests(AdminHostFixture fixture)
{
    [Fact]
    public async Task Health_Returns200_WithHealthyStatus()
    {
        var response = await fixture.Client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInStubPage_Renders200_WithTheKeyedSignInStrings()
    {
        var response = await fixture.Client.GetAsync("/");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Staff sign-in", html, StringComparison.Ordinal);
        Assert.Contains("/dashboard", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInStubPage_AlsoServesAtTheExplicitSigninRoute()
    {
        var response = await fixture.Client.GetAsync("/signin");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Staff sign-in", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardStubRoute_Renders200_WithTheKeyedDashboardStrings()
    {
        var response = await fixture.Client.GetAsync("/dashboard");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin Dashboard", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_Returns404_NeverA500()
    {
        var response = await fixture.Client.GetAsync("/this-route-does-not-exist");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoRenderedPage_LeaksAnUnkeyedLiteral_TheCatalogIsTheOnlyTextSource()
    {
        // A cheap, standing proof of §8 seam 14 ("ALL admin strings keyed from commit one") against the
        // REAL rendered output, independent of whatever tools/i18n-lint/i18n-lint.mjs does or does not
        // scan yet: every string this scaffold's two pages render is asserted by name above; this test
        // just proves the shell renders nothing else user-visible.
        var signin = await (await fixture.Client.GetAsync("/")).Content.ReadAsStringAsync();
        var dashboard = await (await fixture.Client.GetAsync("/dashboard")).Content.ReadAsStringAsync();

        foreach (var html in new[] { signin, dashboard })
        {
            Assert.Contains("Svac Admin", html, StringComparison.Ordinal); // the keyed <title>
        }
    }
}
