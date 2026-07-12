using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Export;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Ledger;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// HTTP-level `/v1/me/export*` proofs against the REAL host (SLICE_S3_CONTRACT.md §1c/§3/§6b/§10.3):
/// single-active idempotency (duplicate POST ⇒ same job), download through the authed endpoint only,
/// the OwnedResource(export) IDOR drill (another account's exportId ⇒ absence, byte-identical to a random
/// id), and export-completeness (every registered store's JSON file lands in the zip + manifest.json
/// matches the export-registry, including the consent + ledger contributions).
/// </summary>
[Collection(IdentityHttpCollectionDefinition.Name)]
public sealed class ExportEndpointsHttpTests(IdentityHttpFixture fixture)
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------------------------------------
    // Single-active idempotency (§1c/§2): a duplicate POST while a job is pending/ready resolves to the
    // SAME exportId — never a second row, race-proof by the partial unique index ux_export_active.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task PostExportTwice_ReturnsTheSameJob_AndOnlyOneRowExists()
    {
        var (accountId, token) = await fixture.SeedActiveAccountWithSession();

        var first = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", token, new { }));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstBody = JsonSerializer.Deserialize<JsonElement>(await first.Content.ReadAsStringAsync(), CaseInsensitive);
        var firstExportId = firstBody.GetProperty("exportId").GetString()!;
        Assert.StartsWith("exp_", firstExportId, StringComparison.Ordinal);

        var second = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", token, new { }));
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var secondBody = JsonSerializer.Deserialize<JsonElement>(await second.Content.ReadAsStringAsync(), CaseInsensitive);
        Assert.Equal(firstExportId, secondBody.GetProperty("exportId").GetString());

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var rowCount = await db.ExportJobs.CountAsync(e => e.AccountId == accountId);
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task PostExport_RunsSynchronously_AndReachesReady_AndSendsTheExportReadyEmail()
    {
        var (_, token) = await fixture.SeedActiveAccountWithSession();

        var post = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", token, new { }));
        var exportId = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), CaseInsensitive).GetProperty("exportId").GetString()!;

        var status = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{exportId}", token));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusBody = JsonSerializer.Deserialize<JsonElement>(await status.Content.ReadAsStringAsync(), CaseInsensitive);
        Assert.Equal("ready", statusBody.GetProperty("state").GetString());
        Assert.True(statusBody.TryGetProperty("expiresAt", out var expiresAt) && expiresAt.ValueKind != JsonValueKind.Null);

        Assert.Contains(fixture.Emails.Sent, m => m.TemplateKey == "email.export_ready");
    }

    // ------------------------------------------------------------------------------------------------
    // Lazy expiry (§4: identity.export.link_ttl_hours) — an expired "ready" row (1) reports "expired" on
    // GET, (2) never permanently blocks a fresh request via ux_export_active, and (3) can no longer be
    // downloaded.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task AnExpiredReadyJob_ReportsExpired_NeverBlocksAFreshRequest_AndCannotBeDownloaded()
    {
        var (accountId, token) = await fixture.SeedActiveAccountWithSession();
        var staleExportId = "exp_01HAAAAAAAAAAAAAAAAAAAAAAA";

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.ExportJobs.Add(new ExportJobEntity
            {
                ExportId = staleExportId,
                AccountId = accountId,
                State = "ready",
                Artifact = new byte[] { 1, 2, 3 },
                ManifestJson = "{}",
                RequestedAt = now.AddDays(-2),
                ReadyAt = now.AddDays(-2),
                ExpiresAt = now.AddHours(-1), // already expired
                Region = "US",
                LawfulBasis = "legitimate_interest",
            });
            await db.SaveChangesAsync();
        }

        var status = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{staleExportId}", token));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusBody = JsonSerializer.Deserialize<JsonElement>(await status.Content.ReadAsStringAsync(), CaseInsensitive);
        Assert.Equal("expired", statusBody.GetProperty("state").GetString());

        var download = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{staleExportId}/download", token));
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        // The expired row must never block a fresh request via ux_export_active (state IN pending/ready).
        var post = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", token, new { }));
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var postBody = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), CaseInsensitive);
        var newExportId = postBody.GetProperty("exportId").GetString();
        Assert.NotEqual(staleExportId, newExportId);

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var staleRow = await db.ExportJobs.SingleAsync(e => e.ExportId == staleExportId);
            Assert.Equal("expired", staleRow.State); // the sweep persisted, not just the read-time render.
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Download through the authed endpoint only (§1c: "NO public SAS URL") — a not-ready job renders
    // absence, never a distinguishing reason.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task DownloadBeforeReady_Renders404()
    {
        // A "pending" job never observably exists in this build (ExportWorker.RunAsync runs
        // synchronously before POST returns, §6b's own documented deviation) — proven instead against a
        // job that failed to reach ready: an exportId that was never created renders absence identically.
        var (_, token) = await fixture.SeedActiveAccountWithSession();
        var neverRequestedId = "exp_01HZZZZZZZZZZZZZZZZZZZZZZZ";

        var download = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{neverRequestedId}/download", token));
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    // ------------------------------------------------------------------------------------------------
    // THE OwnedResource(export) IDOR drill (§3): a foreign account's exportId ⇒ absence, byte-identical
    // to a random id; the victim's export/job row is provably untouched.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task GetOrDownload_ForAForeignAccountsExportId_Renders404ByteIdenticalToARandomId()
    {
        var (_, victimToken) = await fixture.SeedActiveAccountWithSession();
        var (_, attackerToken) = await fixture.SeedActiveAccountWithSession();

        var post = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", victimToken, new { }));
        var victimExportId = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), CaseInsensitive).GetProperty("exportId").GetString()!;

        var randomId = "exp_01HZZZZZZZZZZZZZZZZZZZZZZZ";

        var foreignStatus = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{victimExportId}", attackerToken));
        var randomStatus = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{randomId}", attackerToken));
        Assert.Equal(HttpStatusCode.NotFound, foreignStatus.StatusCode);
        Assert.Equal(randomStatus.StatusCode, foreignStatus.StatusCode);
        Assert.Equal(await randomStatus.Content.ReadAsStringAsync(), await foreignStatus.Content.ReadAsStringAsync());

        var foreignDownload = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{victimExportId}/download", attackerToken));
        var randomDownload = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{randomId}/download", attackerToken));
        Assert.Equal(HttpStatusCode.NotFound, foreignDownload.StatusCode);
        Assert.Equal(randomDownload.StatusCode, foreignDownload.StatusCode);
        Assert.Equal(await randomDownload.Content.ReadAsStringAsync(), await foreignDownload.Content.ReadAsStringAsync());

        // The victim's own job survives untouched and downloads fine with their OWN token.
        var victimDownload = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{victimExportId}/download", victimToken));
        Assert.Equal(HttpStatusCode.OK, victimDownload.StatusCode);
    }

    // ------------------------------------------------------------------------------------------------
    // Export-completeness (§6b/§10.3): seed a subject across stores (consent + ledger explicitly, per
    // the contract's own callout) -> run export -> assert EVERY Contributes-state store in the compiled
    // IExportRegistry appears in the zip, and manifest.json's per-store receipt matches.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task RunExport_EveryRegisteredContributesStore_AppearsInTheZip_InclConsentAndLedger()
    {
        var (accountId, token) = await fixture.SeedActiveAccountWithSession();

        // Consent contribution: a real push-category write through the real HTTP surface.
        var putConsent = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Put, "/v1/me/push-consents/3", token, new { enabled = true }));
        Assert.Equal(HttpStatusCode.NoContent, putConsent.StatusCode);

        // Device contribution.
        var postDevice = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/devices", token, new { platform = "ios", pushToken = "export-test-token" }));
        Assert.Equal(HttpStatusCode.Created, postDevice.StatusCode);

        // Ledger contribution: a real ILedger.Append (the S1 seam), mirroring PurgeCompletenessTests'
        // own seeding pattern — the export's own ledger_entries contributor rides ILedger.BalanceOf.
        using (var seedScope = fixture.NewScope())
        {
            var ledger = seedScope.ServiceProvider.GetRequiredService<ILedger>();
            var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
            var systemCtx = RequestContext.System(systemActor, "export-completeness-seed");
            await ledger.Append(new LedgerEntry(accountId, CrewId: null, EventType: "quest_complete", Points: 10, Xp: 10, Svac: 0, QuestId: null, EvidenceRef: null), systemActor, systemCtx);
        }

        var post = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", token, new { }));
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var exportId = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), CaseInsensitive).GetProperty("exportId").GetString()!;

        var download = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{exportId}/download", token));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var zipBytes = await download.Content.ReadAsByteArrayAsync();

        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(e => e.Name).ToHashSet();

        IReadOnlyList<ExportRegistrationEntry> registryEntries;
        using (var scope = fixture.NewScope())
        {
            registryEntries = scope.ServiceProvider.GetRequiredService<IExportRegistry>().Entries;
        }
        var contributesKeys = registryEntries.Where(e => e.State == ExportRegistryState.Contributes).Select(e => e.StoreKey).ToList();
        Assert.NotEmpty(contributesKeys);

        foreach (var storeKey in contributesKeys)
        {
            Assert.Contains($"{storeKey}.json", entryNames);
        }
        Assert.Contains("manifest.json", entryNames);

        var manifestJson = ReadEntry(archive, "manifest.json");
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestJson, CaseInsensitive);
        var manifestStoreKeys = manifest.GetProperty("stores").EnumerateArray().Select(s => s.GetProperty("storeKey").GetString()).ToHashSet();
        foreach (var storeKey in contributesKeys)
        {
            Assert.Contains(storeKey, manifestStoreKeys);
        }

        // The consent contribution, read back for real off the zip content (never a stub).
        var pushConsentsJson = ReadEntry(archive, "identity.push_category_consents.json");
        var pushConsents = JsonSerializer.Deserialize<JsonElement[]>(pushConsentsJson, CaseInsensitive)!;
        var category3 = Assert.Single(pushConsents, p => p.GetProperty("category").GetInt32() == 3);
        Assert.True(category3.GetProperty("enabled").GetBoolean());

        var consentCurrentJson = ReadEntry(archive, "identity.consent_current.json");
        Assert.Contains("push_category_3", consentCurrentJson, StringComparison.Ordinal);

        var eventsConsentJson = ReadEntry(archive, "events_consent.json");
        Assert.Contains("push_category_3", eventsConsentJson, StringComparison.Ordinal);

        // The ledger contribution: balance.points >= 10 AND the raw entry is present.
        var ledgerEntriesJson = ReadEntry(archive, "ledger_entries.json");
        var ledgerPayload = JsonSerializer.Deserialize<JsonElement>(ledgerEntriesJson, CaseInsensitive);
        Assert.True(ledgerPayload.GetProperty("balance").GetProperty("points").GetInt64() >= 10);
        var ledgerEntriesArray = ledgerPayload.GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(ledgerEntriesArray, e => e.GetProperty("eventType").GetString() == "quest_complete");

        var eventsLedgerJson = ReadEntry(archive, "events_ledger.json");
        Assert.Contains("quest_complete", eventsLedgerJson, StringComparison.Ordinal);

        // The devices contribution.
        var devicesJson = ReadEntry(archive, "identity.devices.json");
        Assert.Contains("export-test-token", devicesJson, StringComparison.Ordinal);

        // The accounts contribution — birthdate IS present here (Art. 15's own artifact), unlike GET /v1/me.
        var accountsJson = ReadEntry(archive, "identity.accounts.json");
        Assert.Contains("\"birthdate\"", accountsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"birthdate\": null", accountsJson, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // PII-7 (SECURITY_REVIEW_S3.md): the artifact bytea stored on identity.export_jobs must never be
    // plaintext-readable — it is now encrypted under the subject's own field key — AND an expired job's
    // Artifact/manifest columns must be nulled in the SAME expiry transition, never left to persist
    // unbounded after the job's own download window has passed.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task StoredArtifact_IsNotPlaintextReadable_AndIsNulledOnExpiry()
    {
        var (accountId, token) = await fixture.SeedActiveAccountWithSession();

        var post = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Post, "/v1/me/export", token, new { }));
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var exportId = JsonSerializer.Deserialize<JsonElement>(await post.Content.ReadAsStringAsync(), CaseInsensitive).GetProperty("exportId").GetString()!;

        byte[] rawArtifact;
        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var row = await db.ExportJobs.SingleAsync(e => e.ExportId == exportId);
            Assert.Equal("ready", row.State);
            rawArtifact = row.Artifact ?? throw new InvalidOperationException("ready job has no artifact.");
        }

        // Not plaintext-readable: the raw stored bytes must NOT contain the zip's own PK signature, the
        // literal "birthdate" field name, nor the account's own plaintext birthdate value — all of which
        // WOULD be present verbatim in a plaintext zip (this exact account's export contains the subject's
        // own decrypted birthdate, by Art. 15 design — see AccountExportContributor).
        var rawText = System.Text.Encoding.Latin1.GetString(rawArtifact); // byte-preserving decode for a substring scan over arbitrary binary.
        Assert.DoesNotContain("PK", rawText, StringComparison.Ordinal); // the ZIP local-file-header magic.
        Assert.DoesNotContain("birthdate", rawText, StringComparison.OrdinalIgnoreCase);

        // The download path still decrypts transparently — the export is not broken by encrypting it at rest.
        var download = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{exportId}/download", token));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        var zipBytes = await download.Content.ReadAsByteArrayAsync();
        using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            Assert.Contains("manifest.json", archive.Entries.Select(e => e.Name));
        }

        // Force the job to expire, then re-read its status through the endpoint (triggers the lazy sweep) —
        // Artifact + ManifestJson must both be nulled in that SAME expiry transition.
        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.ExportJobs.Where(e => e.ExportId == exportId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)));
        }
        var status = await fixture.Client.SendAsync(IdentityHttpFixture.Authed(HttpMethod.Get, $"/v1/me/export/{exportId}", token));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusBody = JsonSerializer.Deserialize<JsonElement>(await status.Content.ReadAsStringAsync(), CaseInsensitive);
        Assert.Equal("expired", statusBody.GetProperty("state").GetString());

        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var expiredRow = await db.ExportJobs.SingleAsync(e => e.ExportId == exportId);
            Assert.Null(expiredRow.Artifact);
            Assert.Null(expiredRow.ManifestJson);
        }
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"zip entry \"{name}\" not found — entries present: {string.Join(", ", archive.Entries.Select(e => e.Name))}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
