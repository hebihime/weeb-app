using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// HTTP-level `/v1/me/*` proofs against a REAL host (SLICE_S3_CONTRACT.md §1c/§3/§8/§10.3): the IDOR live
/// fixture (session + device cross-account DELETE), category-8 absence (byte-identical to a genuinely
/// nonexistent category), the handle-cooldown LimitReached shape, and GET /v1/me's full AccountSelf shape
/// with the birthdate-never-in-response assertion. These exercise the ACTUAL policy chokepoint + ownership
/// resolvers + SessionBearerAuthenticator, not a DB-service-layer stand-in (that is HandleChangeTests/
/// EmailChangeTests/PushConsentWriteTests' job).
/// </summary>
[Collection(IdentityHttpCollectionDefinition.Name)]
public sealed class MeEndpointsHttpTests(IdentityHttpFixture fixture)
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------------------------------------
    // IDOR live fixture — DELETE /v1/me/sessions/{sessionId} (THE Auth-F3 exemplar route, §3).
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DeleteSession_ForAForeignAccountsSessionId_Renders404ByteIdenticalToARandomId_AndNeverTouchesTheVictimRow()
    {
        var (accountA, tokenA) = await fixture.SeedActiveAccountWithSession();
        var (_, tokenB) = await fixture.SeedActiveAccountWithSession();
        var victimSessionId = await CurrentSessionId(tokenB);

        var foreignResponse = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Delete, $"/v1/me/sessions/{victimSessionId}", tokenA));
        var randomId = "ses_01HZZZZZZZZZZZZZZZZZZZZZZZ";
        var randomResponse = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Delete, $"/v1/me/sessions/{randomId}", tokenA));

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(randomResponse.StatusCode, foreignResponse.StatusCode);
        Assert.Equal(await randomResponse.Content.ReadAsStringAsync(), await foreignResponse.Content.ReadAsStringAsync());

        // The victim's OWN session is provably untouched — deny-as-absence never revokes what it denies.
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var victimRow = await db.Sessions.SingleAsync(s => s.SessionId == victimSessionId);
        Assert.Null(victimRow.RevokedAt);
        Assert.NotEqual(accountA, victimRow.AccountId);
    }

    [Fact]
    public async Task DeleteSession_ForTheOwningCaller_Revokes_AndRendersNoContent()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();
        var sessionId = await CurrentSessionId(token);

        var response = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Delete, $"/v1/me/sessions/{sessionId}", token));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.Sessions.SingleAsync(s => s.SessionId == sessionId);
        Assert.NotNull(row.RevokedAt);
        Assert.Equal("user_revoked", row.RevokeReason);
    }

    // ------------------------------------------------------------------------------------------------
    // IDOR live fixture — DELETE /v1/me/devices/{deviceId}.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DeleteDevice_ForAForeignAccountsDeviceId_Renders404ByteIdenticalToARandomId_AndNeverTouchesTheVictimRow()
    {
        var (_, tokenA) = await fixture.SeedActiveAccountWithSession();
        var (_, tokenB) = await fixture.SeedActiveAccountWithSession();

        var registerResponse = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/devices", tokenB, new { platform = "ios", pushToken = (string?)null }));
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = JsonSerializer.Deserialize<JsonElement>(await registerResponse.Content.ReadAsStringAsync(), CaseInsensitive);
        var victimDeviceId = registered.GetProperty("deviceId").GetString()!;

        var foreignResponse = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Delete, $"/v1/me/devices/{victimDeviceId}", tokenA));
        var randomId = "dev_01HZZZZZZZZZZZZZZZZZZZZZZZ";
        var randomResponse = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Delete, $"/v1/me/devices/{randomId}", tokenA));

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(randomResponse.StatusCode, foreignResponse.StatusCode);
        Assert.Equal(await randomResponse.Content.ReadAsStringAsync(), await foreignResponse.Content.ReadAsStringAsync());

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var victimRow = await db.Devices.SingleAsync(d => d.DeviceId == victimDeviceId);
        Assert.Null(victimRow.RevokedAt);
    }

    // ------------------------------------------------------------------------------------------------
    // Category-8 absence drill (§0/§1c/§12 item 10): PUT /…/8 wire-identical to PUT /…/17.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task PutPushConsent_Category8_IsWireIdenticalTo_Category17()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();

        var eight = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Put, "/v1/me/push-consents/8", token, new { enabled = true }));
        var seventeen = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Put, "/v1/me/push-consents/17", token, new { enabled = true }));

        Assert.Equal(HttpStatusCode.NotFound, eight.StatusCode);
        Assert.Equal(seventeen.StatusCode, eight.StatusCode);
        Assert.Equal(await seventeen.Content.ReadAsStringAsync(), await eight.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutPushConsent_Category3_Succeeds_AndReadsBackViaGet()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();

        var put = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Put, "/v1/me/push-consents/3", token, new { enabled = true }));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var get = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, "/v1/me/push-consents", token));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var rows = JsonSerializer.Deserialize<JsonElement[]>(await get.Content.ReadAsStringAsync(), CaseInsensitive)!;

        Assert.Equal(8, rows.Length); // exactly categories 1-7,9 — never 8.
        Assert.DoesNotContain(rows, r => r.GetProperty("category").GetInt32() == 8);
        var category3 = Assert.Single(rows, r => r.GetProperty("category").GetInt32() == 3);
        Assert.True(category3.GetProperty("enabled").GetBoolean());
    }

    // ------------------------------------------------------------------------------------------------
    // Handle-change cooldown LimitReached shape (§1c/§5).
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task SecondHandleChangeWithinCooldown_Renders429LimitReached_WithPremiumExtendsFalse()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();

        var first = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/handle", token, new { handle = UniqueHandle() }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/handle", token, new { handle = UniqueHandle() }));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        var body = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync(), CaseInsensitive);
        Assert.Equal("identity.handle.change", body.GetProperty("quotaKey").GetString());
        Assert.False(body.GetProperty("premiumExtends").GetBoolean());
        Assert.True(body.TryGetProperty("resetsAt", out _));
    }

    // ------------------------------------------------------------------------------------------------
    // GET /v1/me — full AccountSelf shape; birthdate/dob never in the response graph (§1c: arch-tested).
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task GetMe_RendersTheFullAccountSelfShape_AndNeverTheBirthdate()
    {
        var birthday18Today = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18);
        var (accountId, token) = await fixture.SeedActiveAccountWithSession(birthdate: birthday18Today);

        var response = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, "/v1/me", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("birthdate", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"dob\"", raw, StringComparison.OrdinalIgnoreCase);

        var body = JsonSerializer.Deserialize<JsonElement>(raw, CaseInsensitive);
        Assert.Equal(accountId, body.GetProperty("accountId").GetString());
        Assert.Equal(18, body.GetProperty("ageYears").GetInt32()); // turns 18 TODAY — already the new age.
        Assert.False(body.TryGetProperty("deletionScheduledFor", out var deletionProp) && deletionProp.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task GetMe_TurnsEighteenTomorrow_StillRendersSeventeen()
    {
        var birthdayTomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-18).AddDays(1);
        var (_, token) = await fixture.SeedActiveAccountWithSession(birthdate: birthdayTomorrow);

        var response = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, "/v1/me", token));
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), CaseInsensitive);
        Assert.Equal(17, body.GetProperty("ageYears").GetInt32());
    }

    // ------------------------------------------------------------------------------------------------
    // PATCH /v1/me — locale update.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task PatchMe_UpdatesLocale_WhenInTheAllowedSet()
    {
        var (accountId, token) = await fixture.SeedActiveAccountWithSession();

        var response = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Patch, "/v1/me", token, new { locale = "es" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal("es", account.Locale);
    }

    [Fact]
    public async Task PatchMe_RejectsALocaleOutsideTheAllowedSet()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();
        var response = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Patch, "/v1/me", token, new { locale = "fr" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- helpers ---------------------------------------------------------------------------------

    private async Task<string> CurrentSessionId(string accessToken)
    {
        var response = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, "/v1/me/sessions", accessToken));
        var rows = JsonSerializer.Deserialize<JsonElement[]>(await response.Content.ReadAsStringAsync(), CaseInsensitive)!;
        var current = Assert.Single(rows, r => r.GetProperty("current").GetBoolean());
        return current.GetProperty("sessionId").GetString()!;
    }

    private static string UniqueHandle() => $"httpme_{Guid.NewGuid():N}"[..20];
}
