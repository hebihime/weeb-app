using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Persistence;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SECURITY_REVIEW_S5.md S5-01 (fixNow): before this fix, GET /config called
/// <c>IConfigRegistry.ListEntries()</c> unconditionally — no <c>ActorKind.Staff</c> check, no
/// <c>admin.config.read</c> policy row (none existed), no <c>@if (_canView)</c> guard on the QuickGrid.
/// The entire 9A registry (every founder/ops/set-scope value) was reachable by ANY request that hit the
/// route. This is the RED->GREEN regression test: a live HTTP round-trip against the REAL
/// <see cref="AdminHostFixture"/> composition (the actual policy row + the actual page code, never a
/// unit-level stand-in) proving a non-qualifying staff actor sees NEITHER the row data NOR any
/// data-testid the page renders only for a real row, while a qualifying actor (SuperAdmin) is completely
/// unaffected.
/// </summary>
[Collection("AdminHostHttp")]
public sealed class ConfigRegistryReadGateHttpTests(AdminHostFixture fixture)
{
    private HttpClient NewClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = fixture.Client.BaseAddress };

    private CoreDbContext NewCoreDb() => new(
        new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private static async Task<string> Get(HttpClient client, string path) =>
        await (await client.GetAsync(path)).Content.ReadAsStringAsync();

    private static async Task<(HttpStatusCode Status, string? Location, string Html)> PostForm(
        HttpClient client, string path, IReadOnlyDictionary<string, string> fields)
    {
        var content = new FormUrlEncodedContent(fields);
        var response = await client.PostAsync(path, content);
        var html = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, response.Headers.Location?.ToString(), html);
    }

    private static string? ExtractFormAction(string html, string testId)
    {
        var m = Regex.Match(html, $"data-testid=\"{Regex.Escape(testId)}\"[^>]*action=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static bool HasTestId(string html, string testId) => html.Contains($"data-testid=\"{testId}\"", StringComparison.Ordinal);

    private async Task<HttpClient> SignInAs(string devSeamsFixtureKey)
    {
        var client = NewClient();
        var signInPage = await Get(client, "/signin");
        var action = ExtractFormAction(signInPage, $"devseams-fixture-{devSeamsFixtureKey}");
        Assert.True(action is not null, $"no <form data-testid=\"devseams-fixture-{devSeamsFixtureKey}\"> on /signin");
        var tokenMatch = Regex.Match(signInPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"");
        Assert.True(tokenMatch.Success);

        var res = await PostForm(client, action!, new Dictionary<string, string>
        {
            ["fixture"] = devSeamsFixtureKey,
            ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value,
        });
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        return client;
    }

    [Fact]
    public async Task NonQualifyingStaff_CannotViewTheConfigRegistry_NoRowDataLeaks()
    {
        const string key = "test.admin.s5_01.leak_probe_key";
        using (var coreDb = NewCoreDb())
        {
            await AdminTestSupport.SeedConfigEntry(coreDb, key, "founder", "int", "424242", requiresReason: true);
        }

        // SafetyAgent holds none of admin.config.read's allowlisted roles (SuperAdmin, EconomyOps) —
        // §S5-01's own StaffRoleAllowlistNote.
        var client = await SignInAs("safetyagent");

        var configPage = await Get(client, "/config");
        Assert.False(HasTestId(configPage, $"config-row-{key}"), "a SafetyAgent must never see a config-row-* testid — the registry read gate must refuse before ListEntries() is ever called");
        Assert.False(HasTestId(configPage, $"config-value-{key}"), "a SafetyAgent must never see the raw value of a 9A entry");
        Assert.DoesNotContain("424242", configPage, StringComparison.Ordinal); // the value itself never leaks into the response body
        Assert.False(HasTestId(configPage, $"config-edit-form-{key}"), "no edit form for a role that cannot even view the row");
    }

    [Fact]
    public async Task Anonymous_CannotViewTheConfigRegistry_NoRowDataLeaks()
    {
        const string key = "test.admin.s5_01.anon_leak_probe_key";
        using (var coreDb = NewCoreDb())
        {
            await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "131313", requiresReason: false);
        }

        var client = NewClient(); // never signed in
        var configPage = await Get(client, "/config");

        Assert.False(HasTestId(configPage, $"config-row-{key}"), "an anonymous request must never see any config-row-* testid");
        Assert.DoesNotContain("131313", configPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuperAdmin_StillViewsTheConfigRegistry_UnaffectedByTheNewGate()
    {
        const string key = "test.admin.s5_01.superadmin_visible_key";
        using (var coreDb = NewCoreDb())
        {
            await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "77", requiresReason: false);
        }

        var client = await SignInAs("superadmin");
        var configPage = await Get(client, "/config");

        Assert.True(HasTestId(configPage, $"config-row-{key}"), "SuperAdmin (admin.config.read's own allowlist) must still see the row after the S5-01 gate lands");
        Assert.True(HasTestId(configPage, $"config-value-{key}"), "SuperAdmin must still see the value");
        Assert.Contains("77", configPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EconomyOps_StillViewsTheConfigRegistry_NeededToUseItsOwnOpsScopeEditForm()
    {
        const string key = "test.admin.s5_01.economyops_visible_key";
        using (var coreDb = NewCoreDb())
        {
            await AdminTestSupport.SeedConfigEntry(coreDb, key, "ops", "int", "88", requiresReason: false);
        }

        var client = await SignInAs("economyops");
        var configPage = await Get(client, "/config");

        Assert.True(HasTestId(configPage, $"config-row-{key}"), "EconomyOps can commit core.config.set.ops -- it must be able to SEE the desk to use its own edit form");
        Assert.True(HasTestId(configPage, $"config-edit-form-{key}"), "an ops-scope key's edit form must still render for EconomyOps");
    }
}
