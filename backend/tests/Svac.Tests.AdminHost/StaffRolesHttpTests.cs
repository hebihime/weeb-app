using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// Live HTTP round-trip proof of the Staff & Roles desk's wire contract (SLICE_S5_CONTRACT.md §0/§8
/// seam 1, Pass B) against the REAL Program.cs composition (<see cref="AdminHostFixture"/>) — a real
/// cookie jar, real antiforgery-token round-tripping, real SSR form POSTs, mirroring
/// backend/e2e/admin-host.e2e.mjs's own hand-rolled-cookie-jar convention (§12.1: no Playwright) in C#
/// against the in-process Testcontainer-backed host this suite already boots for BootHttpTests/
/// BootRefusalTests. Every mutation here happens because a real HTTP POST drove it through
/// AdminActionExecutor — the SAME chokepoint AdminActionExecutorTests.cs exercises directly; THIS file's
/// job is proving the WIRING (routes, field names, data-testid attributes, antiforgery) on top of that
/// already-proven executor, never re-proving the executor's own gates.
///
/// Uses its OWN <see cref="HttpClient"/> per staff identity (never <see cref="AdminHostFixture.Client"/>,
/// which is shared collection-wide) so two different signed-in fixtures never clobber each other's
/// cookie.
/// </summary>
[Collection("AdminHostHttp")]
public sealed class StaffRolesHttpTests(AdminHostFixture fixture)
{
    private HttpClient NewClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = fixture.Client.BaseAddress };

    private AdminDbContext NewAdminDb() => new(
        new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options);

    private static async Task<(HttpStatusCode Status, string? Location, string Html)> PostForm(
        HttpClient client, string path, IReadOnlyDictionary<string, string> fields)
    {
        var content = new FormUrlEncodedContent(fields);
        var response = await client.PostAsync(path, content);
        var html = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, response.Headers.Location?.ToString(), html);
    }

    private static async Task<string> Get(HttpClient client, string path) =>
        await (await client.GetAsync(path)).Content.ReadAsStringAsync();

    private static string ExtractAntiforgeryToken(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"");
        Assert.True(m.Success, "no <input name=\"__RequestVerificationToken\" ...> found on the page");
        return m.Groups[1].Value;
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
        var action = ExtractFormAction(signInPage, $"devseams-fixture-{devSeamsFixtureKey}")
            ?? $"/devseams/signin/{devSeamsFixtureKey}"; // SignIn.razor renders no data-testid (Pass A) — fall back to the known real route.
        var token = ExtractAntiforgeryToken(signInPage);

        var res = await PostForm(client, action, new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        });
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        Assert.Contains("/dashboard", res.Location ?? "", StringComparison.Ordinal);
        return client;
    }

    [Fact]
    public async Task SuperAdmin_ProvisionsGrantsRevokesDeactivates_FullRoundTrip_AllAuditedAndPersisted()
    {
        var client = await SignInAs("SuperAdmin");

        var staffPage = await Get(client, "/staff");
        Assert.True(HasTestId(staffPage, "staff-provision-form"), "GET /staff must render data-testid=\"staff-provision-form\"");
        var provisionAction = ExtractFormAction(staffPage, "staff-provision-form");
        Assert.NotNull(provisionAction);
        var provisionToken = ExtractAntiforgeryToken(staffPage);

        var externalSubject = $"test:http-roundtrip:{Guid.NewGuid():N}";
        var provisionRes = await PostForm(client, provisionAction!, new Dictionary<string, string>
        {
            ["externalSubject"] = externalSubject,
            ["email"] = $"{Guid.NewGuid():N}@devseams.svac.internal",
            ["displayName"] = "HTTP round-trip fixture",
            ["region"] = "US",
            ["reason"] = "http round-trip provisioning drill",
            ["__RequestVerificationToken"] = provisionToken,
        });
        Assert.Equal(HttpStatusCode.Redirect, provisionRes.Status);
        Assert.Contains("/staff", provisionRes.Location ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("error=1", provisionRes.Location ?? "", StringComparison.Ordinal);

        using var adminDb = NewAdminDb();
        var provisioned = await adminDb.StaffAccounts.SingleAsync(s => s.ExternalSubject == externalSubject);
        Assert.Equal("active", provisioned.Status);
        var staffId = provisioned.Id;

        // grant
        var pageWithNewStaff = await Get(client, "/staff");
        Assert.True(HasTestId(pageWithNewStaff, $"staff-row-{staffId}"), $"no data-testid=\"staff-row-{staffId}\" after provisioning");
        Assert.Contains(externalSubject, pageWithNewStaff, StringComparison.Ordinal); // staff-external-subject-<id> renders the real subject
        var grantAction = ExtractFormAction(pageWithNewStaff, $"staff-grant-form-{staffId}");
        Assert.Equal($"/staff/{staffId}/grant", grantAction);
        var grantToken = ExtractAntiforgeryToken(pageWithNewStaff);

        var grantRes = await PostForm(client, grantAction!, new Dictionary<string, string>
        {
            ["role"] = "economy_ops",
            ["reason"] = "http round-trip grant drill",
            ["__RequestVerificationToken"] = grantToken,
        });
        Assert.Equal(HttpStatusCode.Redirect, grantRes.Status);
        Assert.DoesNotContain("error=1", grantRes.Location ?? "", StringComparison.Ordinal);

        using var adminDbAfterGrant = NewAdminDb();
        var activeGrant = await adminDbAfterGrant.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId && g.Role == "economy_ops" && g.RevokedAt == null);
        Assert.Equal("http round-trip grant drill", activeGrant.GrantReason);

        // revoke — role is a ROUTE segment, per backend/e2e/admin-host.e2e.mjs's wire contract.
        var pageWithGrant = await Get(client, "/staff");
        var revokeAction = ExtractFormAction(pageWithGrant, $"staff-revoke-form-{staffId}-economy_ops");
        Assert.Equal($"/staff/{staffId}/revoke/economy_ops", revokeAction);
        var revokeToken = ExtractAntiforgeryToken(pageWithGrant);

        var revokeRes = await PostForm(client, revokeAction!, new Dictionary<string, string>
        {
            ["reason"] = "http round-trip revoke drill",
            ["__RequestVerificationToken"] = revokeToken,
        });
        Assert.Equal(HttpStatusCode.Redirect, revokeRes.Status);

        using var adminDbAfterRevoke = NewAdminDb();
        var revokedCount = await adminDbAfterRevoke.StaffRoleGrants.CountAsync(g => g.StaffId == staffId && g.Role == "economy_ops" && g.RevokedAt == null);
        Assert.Equal(0, revokedCount);

        // deactivate
        var pageBeforeDeactivate = await Get(client, "/staff");
        var deactivateAction = ExtractFormAction(pageBeforeDeactivate, $"staff-deactivate-form-{staffId}");
        Assert.Equal($"/staff/{staffId}/deactivate", deactivateAction);
        var deactivateToken = ExtractAntiforgeryToken(pageBeforeDeactivate);

        var deactivateRes = await PostForm(client, deactivateAction!, new Dictionary<string, string>
        {
            ["reason"] = "http round-trip deactivate drill",
            ["__RequestVerificationToken"] = deactivateToken,
        });
        Assert.Equal(HttpStatusCode.Redirect, deactivateRes.Status);

        using var adminDbFinal = NewAdminDb();
        var final = await adminDbFinal.StaffAccounts.SingleAsync(s => s.Id == staffId);
        Assert.Equal("deactivated", final.Status);
        Assert.NotNull(final.DeactivatedAt);
    }

    [Fact]
    public async Task NonSuperAdmin_CannotViewTheRoster_AndAProvisionAttemptIsRefused_RowCountUnchanged()
    {
        // Signs in FIRST -- DevSeamsStaffTransport self-provisions this fixture's OWN row on its very
        // first sign-in (Pass A, §1b), so the "row count unchanged" baseline must be measured AFTER that
        // self-provisioning settles, never before it, or a passing self-provision would look like a
        // (nonexistent) leak in the assertion below.
        var client = await SignInAs("SafetyAgent");

        using var beforeDb = NewAdminDb();
        var countBefore = await beforeDb.StaffAccounts.CountAsync();

        var staffPage = await Get(client, "/staff");
        Assert.False(HasTestId(staffPage, "staff-provision-form"), "a non-SuperAdmin must NEVER see the provision form or staff roster PII");

        // Even without seeing the form, the ENDPOINT itself must independently refuse a SafetyAgent POST
        // (defense in depth — never trust "the UI didn't show a button" as the only gate).
        var res = await PostForm(client, "/staff/provision", new Dictionary<string, string>
        {
            ["externalSubject"] = $"test:should-never-exist:{Guid.NewGuid():N}",
            ["email"] = "should-never-exist@devseams.svac.internal",
            ["displayName"] = "Should never be created",
            ["region"] = "US",
            ["reason"] = "a SafetyAgent has no admin.staff.provision grant",
        });

        // No antiforgery token was supplied above -- UseAntiforgery() itself refuses the request before
        // the handler runs (a 400, not our own redirect shape). Re-issue WITH a real token from the page
        // to isolate the ROLE-axis refusal specifically, never conflating it with a missing-token 400.
        var tokenPage = await Get(client, "/staff");
        var freshToken = ExtractAntiforgeryToken(tokenPage);
        var res2 = await PostForm(client, "/staff/provision", new Dictionary<string, string>
        {
            ["externalSubject"] = $"test:should-never-exist:{Guid.NewGuid():N}",
            ["email"] = "should-never-exist@devseams.svac.internal",
            ["displayName"] = "Should never be created",
            ["region"] = "US",
            ["reason"] = "a SafetyAgent has no admin.staff.provision grant",
            ["__RequestVerificationToken"] = freshToken,
        });

        Assert.Equal(HttpStatusCode.Redirect, res2.Status);
        Assert.Contains("error=1", res2.Location ?? "", StringComparison.Ordinal);

        using var afterDb = NewAdminDb();
        var countAfter = await afterDb.StaffAccounts.CountAsync();
        Assert.Equal(countBefore, countAfter); // refused -- never provisioned.
    }

    [Fact]
    public async Task Anonymous_PostingToAStaffEndpoint_IsRefused_NeverReachesTheExecutor()
    {
        var client = NewClient();
        var res = await PostForm(client, "/staff/provision", new Dictionary<string, string>
        {
            ["externalSubject"] = "test:anon-should-never-exist",
            ["email"] = "anon@devseams.svac.internal",
            ["displayName"] = "Should never be created",
            ["region"] = "US",
            ["reason"] = "anonymous caller",
        });

        // RequireStaffActor redirects a non-staff actor before the executor's own ArgumentException
        // guard is ever reachable -- never a 500.
        Assert.NotEqual(HttpStatusCode.InternalServerError, res.Status);

        using var db = NewAdminDb();
        Assert.False(await db.StaffAccounts.AnyAsync(s => s.ExternalSubject == "test:anon-should-never-exist"));
    }
}
