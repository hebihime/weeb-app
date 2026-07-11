using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.Identity.Export;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// AUTH-1 (SECURITY_REVIEW_S3.md, MED — export read/download defense-in-depth): the export status/download
/// queries used to be scoped ONLY by <c>exportId</c>, relying entirely on the policy chokepoint
/// (<c>RequirePolicyAction</c> + <c>ExportOwnershipResolver</c>) to prove ownership — a latent single point
/// of failure on the highest-value target in the module (the whole data-subject corpus), unlike
/// DeleteMeSession/DeleteMeDevice's own <c>&amp;&amp; AccountId == accountId</c> re-scope. Now folded:
/// <see cref="IExportArtifactStore.GetReadyZipAsync"/> takes <c>accountId</c> and its query requires
/// <c>AccountId == accountId</c> directly.
///
/// This test calls the store DIRECTLY — no HTTP, no policy engine, no ownership resolver in the call path
/// at all — so it proves the query itself denies a foreign account, independent of the policy layer the
/// HTTP-level IDOR drill (ExportEndpointsHttpTests) also happens to exercise.
/// </summary>
[Collection(IdentityHttpCollectionDefinition.Name)]
public sealed class ExportArtifactStoreOwnershipTests(IdentityHttpFixture fixture)
{
    [Fact]
    public async Task GetReadyZipAsync_ForAReadyJobOwnedByAnotherAccount_ReturnsNull_PolicyLayerNeverInvoked()
    {
        var (ownerAccountId, _) = await fixture.SeedActiveAccountWithSession();
        var (attackerAccountId, _) = await fixture.SeedActiveAccountWithSession();
        Assert.NotEqual(ownerAccountId, attackerAccountId);

        var exportId = "exp_" + Guid.NewGuid().ToString("N")[..26];
        using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.ExportJobs.Add(new ExportJobEntity
            {
                ExportId = exportId,
                AccountId = ownerAccountId,
                State = "ready",
                Artifact = new byte[] { 1, 2, 3, 4 }, // not a real encrypted envelope — this test never reaches Unprotect() for the owner path.
                ManifestJson = "{}",
                RequestedAt = now,
                ReadyAt = now,
                ExpiresAt = now.AddHours(1),
                Region = "US",
                LawfulBasis = "legitimate_interest",
            });
            await db.SaveChangesAsync();
        }

        using var assertScope = fixture.NewScope();
        var store = assertScope.ServiceProvider.GetRequiredService<IExportArtifactStore>();

        var asAttacker = await store.GetReadyZipAsync(exportId, attackerAccountId, CancellationToken.None);
        Assert.Null(asAttacker); // the query's own AccountId predicate denies it — no policy engine involved.

        // Sanity: the row genuinely exists and is "ready" — the null above is ownership-scoping, not absence.
        var db2 = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db2.ExportJobs.SingleAsync(e => e.ExportId == exportId);
        Assert.Equal("ready", row.State);
        Assert.Equal(ownerAccountId, row.AccountId);
    }

    [Fact]
    public async Task GetReadyZipAsync_ForAnUnknownExportId_ReturnsNull()
    {
        using var scope = fixture.NewScope();
        var store = scope.ServiceProvider.GetRequiredService<IExportArtifactStore>();

        var result = await store.GetReadyZipAsync("exp_01HZZZZZZZZZZZZZZZZZZZZZZZ", "usr_does_not_matter", CancellationToken.None);
        Assert.Null(result);
    }
}
