using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.AdminHost.ConfigRegistry;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Persistence;
using Xunit;

namespace Svac.Tests.AdminHost;

/// <summary>
/// SLICE_PLAYBOOK's deferred-finding discipline — S5 DEFER/fixNow rows that need a LIVE HTTP round-trip
/// against the real <see cref="AdminHostFixture"/> composition to prove (never provable at the direct-
/// call level, unlike DeferredFindingsProofTests.cs's own rows). SECURITY_REVIEW_S5.md's "Round 2
/// (orchestrator pull-forward)" moved S5-11 and S5-14 (and S5-12's admin-host half, added below) from
/// DEFER to FIX NOW — their tests are real, green, un-Skipped regression tests now. Every OTHER test in
/// this file stays a Skip-annotated proof test documenting a finding still on the DEFER table.
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

    private static string ExtractAntiforgeryToken(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"");
        Assert.True(m.Success, "no <input name=\"__RequestVerificationToken\" ...> found on the page");
        return m.Groups[1].Value;
    }

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
    // S5-11 (LOW, Lens5 F1) — FIXED (Round 2 pull-forward): every config/staff mutation endpoint used to
    // read its antiforgery token off Request.Form MANUALLY (never a [FromForm]-bound parameter, which is
    // what would let ASP.NET Core's built-in automatic antiforgery validation opt in) and NEVER called
    // IAntiforgery.ValidateRequestAsync itself. app.UseAntiforgery() alone does not retroactively validate
    // a minimal-API handler that reads the form by hand — the token minted by <AntiforgeryToken /> was
    // decorative unless SOMETHING calls ValidateRequestAsync. Fixed: every mutation handler now calls
    // AntiforgeryGate.IsValid (Svac.AdminHost.AntiforgeryGate) as the very first thing it does, before any
    // mutation/executor call, and returns its OWN existing denied-refusal shape on failure (never a new
    // shape) — for the config editor that is RedirectToConfigPage(..., errorKind: "denied", ...), never a
    // raw 400 (there is no pre-existing 400 shape in this handler family to reuse, and inventing one would
    // violate "match the existing pattern, do not invent a new one"). This test proves the REJECTION
    // itself: a tokenless mutation POST no longer commits — the stored value stays byte-identical and the
    // response is the standard denied redirect, never the silent 302-on-success it used to be.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task ConfigEditorAntiforgery_TokenlessPost_Rejected()
    {
        const string key = "admin.session_lifetime_hours"; // real v0-batch key: bounds [1,24] (SLICE_S5_CONTRACT.md §4)
        using (var coreDbSeed = NewCoreDb())
        {
            // This fixture never runs the v0-batch manifest loader (it migrates only, mirrors every other
            // HTTP test class in this collection) -- seed the real key by hand, exactly like
            // ConfigEditorBoundsTests.cs does for the same key.
            await AdminTestSupport.SeedConfigEntry(coreDbSeed, key, "ops", "int", "8", requiresReason: false, boundsJson: "[1,24]");
        }

        var client = await SignInAs("superadmin");

        using var coreDbBefore = NewCoreDb();
        var before = await coreDbBefore.ConfigEntries.Where(e => e.Key == key).Select(e => e.ValueJson).SingleAsync();

        // A real ops-scope key, posted with NO antiforgery token field at all.
        var res = await PostForm(client, $"/config/{key}/edit", new Dictionary<string, string>
        {
            ["newValue"] = "5",
            ["reason"] = "s5-11 tokenless drill",
        });

        // Desired (as actually implemented): a tokenless mutation POST is REJECTED — the handler's own
        // denied-redirect shape, never the 302-on-success it used to silently return. Today (pre-fix) this
        // would have been a 302 redirect straight to the saved-notice with the value actually changed.
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        Assert.Contains("errorKind=denied", res.Location ?? "", StringComparison.Ordinal);

        using var coreDbAfter = NewCoreDb();
        var after = await coreDbAfter.ConfigEntries.Where(e => e.Key == key).Select(e => e.ValueJson).SingleAsync();
        Assert.Equal(before, after); // byte-identical -- the tokenless POST never committed.
    }

    // ------------------------------------------------------------------------------------------------
    // S5-12 (LOW, Lens5 F2) admin-host half — FIXED (Round 2 pull-forward): HandleConfirm now re-reads the
    // entry and refuses a set-scope key itself (mirrors HandlePropose's own guard), so the write-refusal
    // no longer rests on HandlePropose's single line alone. This drills the confirm endpoint DIRECTLY with
    // a hand-minted confirmToken for a set-scope key — the exact bypass HandlePropose's own check cannot
    // reach (HandlePropose never even mints a token for a set-scope key; a hand-crafted POST straight at
    // /confirm is the only way to exercise this line). The domain-core half (ConfigRegistry.SetValue's own
    // scope assert) stays DEFERRED — see DeferredFindingsProofTests.SetValue_SetScopeKey_Refused (Skip).
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task ConfigConfirm_SetScopeKey_RefusedEvenWithAValidHandCraftedToken()
    {
        const string key = "test.admin.s5_12_http.set_scope_key";
        using (var coreDb = NewCoreDb())
        {
            await AdminTestSupport.SeedConfigEntry(coreDb, key, "set", "int", "8", requiresReason: false);
        }

        var client = await SignInAs("superadmin");

        // A valid antiforgery token from THIS SAME session (S5-11 is enforced now too) — isolating the
        // scope-recheck specifically, never conflating it with an antiforgery refusal.
        var page = await Get(client, "/config");
        var antiforgeryToken = ExtractAntiforgeryToken(page);

        // Mint the EXACT confirmToken HandlePropose would have minted, straight from the DI container —
        // HandlePropose itself never reaches this key (its own scope guard refuses first), so this is the
        // only way to exercise HandleConfirm's own independent recheck.
        using var scope = fixture.NewScope();
        var confirmToken = scope.ServiceProvider.GetRequiredService<ConfigConfirmToken>();
        const string newValueJson = "999";
        const string reason = "s5-12 http-half drill";
        var sealedToken = confirmToken.Mint(key, newValueJson, reason);

        var res = await PostForm(client, $"/config/{Uri.EscapeDataString(key)}/confirm", new Dictionary<string, string>
        {
            ["newValue"] = newValueJson,
            ["reason"] = reason,
            ["confirmToken"] = sealedToken,
            ["__RequestVerificationToken"] = antiforgeryToken,
        });

        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        Assert.Contains("errorKind=denied", res.Location ?? "", StringComparison.Ordinal);

        using var coreDbAfter = NewCoreDb();
        var unchanged = await coreDbAfter.ConfigEntries.Where(e => e.Key == key).Select(e => e.ValueJson).SingleAsync();
        Assert.Equal("8", unchanged); // byte-identical -- HandleConfirm's own recheck refused it, never committed.
    }

    // ------------------------------------------------------------------------------------------------
    // S5-14 (LOW, Lens6) — FIXED (Round 2 pull-forward): UserSearch.razor.cs's OnInitializedAsync used to
    // return BEFORE ever calling UserSearchExecutionService.Execute when Query was empty/whitespace or
    // QueryClassRaw failed to parse -- so that GET request was neither audited (admin.user_search.executed)
    // nor quota-consumed, deviating from SLICE_S5_CONTRACT.md §0's own "EVERY query (even empty) is
    // audited ... and quota-consumed." Fixed: the empty/invalid-term decision moved INTO
    // UserSearchExecutionService.Execute (a new raw-string overload) as a typed outcome, so the audited
    // executor call — and the quota Consume inside its own work delegate — now runs regardless of query
    // content; the page still renders honest-dark (zero fabricated rows) exactly as before.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task Execute_EmptyTerm_StillAuditsAndConsumes()
    {
        using (var coreDbSeed = NewCoreDb())
        {
            // This fixture never runs the v0-batch manifest loader -- seed the 10A cap QuotaService.Consume
            // actually reads (the DERIVED "quota.<key>.cap" key, Svac.AdminHost.Domain.Search.AdminQuotaKeys'
            // own doc comment), exactly like UserSearchExecutionServiceTests.SeedQuotaCap does. Without it,
            // ConfigRegistry.GetValue throws inside the executor's work delegate, the WHOLE transaction (audit
            // event included) rolls back, and this test would fail for an infra reason, not the S5-14 one.
            await AdminTestSupport.SeedConfigEntry(coreDbSeed, $"quota.{Svac.AdminHost.Domain.Search.AdminQuotaKeys.UserSearchDaily}.cap", "ops", "int", "500", requiresReason: false);
        }

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
