using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Persistence;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_PLAYBOOK's deferred-finding discipline — the S5 DEFER rows that need a LIVE HTTP round-trip
/// against the real <see cref="AdminHostFixture"/> composition to prove (never provable at the direct-
/// call level, unlike DeferredFindingsProofTests.cs's own rows). Each is a Skip-annotated proof test
/// documenting the exact finding + the shape that would fail the moment someone un-Skips it — none are
/// fixed in this pass, see SECURITY_REVIEW_S5.md's DEFER table.
/// </summary>
[Collection("AdminHostHttp")]
public sealed class DeferredFindingsHttpProofTests(AdminHostFixture fixture)
{
    private HttpClient NewClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = fixture.Client.BaseAddress };

    private AdminDbContext NewAdminDb() => new(
        new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(fixture.ConnectionString).Options);

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

    // ------------------------------------------------------------------------------------------------
    // S5-11 (LOW, Lens5 F1): every config/staff mutation endpoint reads its antiforgery token off
    // Request.Form MANUALLY (never a [FromForm]-bound parameter, which is what would let ASP.NET Core's
    // built-in automatic antiforgery validation opt in) and never calls IAntiforgery.ValidateRequestAsync
    // itself. app.UseAntiforgery() alone does not retroactively validate a minimal-API handler that reads
    // the form by hand — the token minted by <AntiforgeryToken /> is decorative unless SOMETHING calls
    // ValidateRequestAsync. Mitigated today by SameSite=Lax (a genuine cross-site POST never carries the
    // cookie at all), but that is the ONLY thing standing between a same-site CSRF vector (an XSS
    // elsewhere on the same origin, an open redirect, a misconfigured subdomain) and a real mutation.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-11 (config/staff mutation endpoints manually bind Request.Form and never call IAntiforgery.ValidateRequestAsync -- mitigated only by SameSite=Lax) -> call ValidateRequestAsync on every mutation POST")]
    public async Task ConfigEditorAntiforgery_TokenlessPost_Rejected()
    {
        var client = await SignInAs("superadmin");

        // A real ops-scope key, posted with NO antiforgery token field at all.
        var res = await PostForm(client, "/config/admin.session_lifetime_hours/edit", new Dictionary<string, string>
        {
            ["newValue"] = "5",
            ["reason"] = "s5-11 tokenless drill",
        });

        // Desired: a tokenless mutation POST is REJECTED (400) once ValidateRequestAsync is actually
        // called. Today it silently proceeds (a 302 redirect on success, since nothing validates the
        // token's presence at all) -- SameSite=Lax is the only real protection in place.
        Assert.Equal(HttpStatusCode.BadRequest, res.Status);
    }

    // ------------------------------------------------------------------------------------------------
    // S5-14 (LOW, Lens6): UserSearch.razor.cs's OnInitializedAsync returns BEFORE ever calling
    // UserSearchExecutionService.Execute when Query is empty/whitespace or QueryClassRaw fails to parse
    // -- so that GET request is neither audited (admin.user_search.executed) nor quota-consumed
    // (admin.user_search_daily_cap), deviating from SLICE_S5_CONTRACT.md §0's own "EVERY query (even
    // empty) is audited ... and quota-consumed." Not an enumeration vector (the page renders identically
    // either way) -- a detection-completeness gap: a scripted zero-length-query flood never shows up in
    // the audit trail or against the daily cap at all.
    // ------------------------------------------------------------------------------------------------
    [Fact(Skip = "deferred: SECURITY_REVIEW_S5.md S5-14 (UserSearch.razor.cs returns on an empty/malformed query BEFORE calling UserSearchExecutionService.Execute -- that request is neither audited nor quota-consumed) -> move the empty-term decision into the service as a typed outcome")]
    public async Task Execute_EmptyTerm_StillAuditsAndConsumes()
    {
        var client = await SignInAs("superadmin");

        using var adminDb = NewAdminDb();
        var staffId = await adminDb.StaffAccounts.Where(s => s.ExternalSubject == "devseams:superadmin").Select(s => s.Id).SingleAsync();

        async Task<int> CountExecutedEvents()
        {
            using var coreDb = NewCoreDb();
            return await coreDb.EventsFor(Svac.DomainCore.Contracts.Streams.StreamType.Audit)
                .Where(e => e.StreamId == staffId && e.EventType == "admin.user_search.executed")
                .CountAsync();
        }

        var before = await CountExecutedEvents();

        // An explicitly EMPTY (not merely absent) query term with a real queryClass -- the exact shape
        // UserSearch.razor.cs's own OnInitializedAsync short-circuits on before ever calling the service.
        await Get(client, "/user-search?query=&queryClass=ByHandle");

        var after = await CountExecutedEvents();

        // Desired: §0's "EVERY query (even empty) is audited" holds even for this page's own GET-driven
        // empty-term case. Today `after == before` (the request never reaches the audited service at all).
        Assert.Equal(before + 1, after);
    }
}
