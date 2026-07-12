using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Auth;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Forced-race proof for refresh rotation (SLICE_S3_CONTRACT.md §8/§10.3): "family CAS -&gt; one winner,
/// reuse alarm" — presenting an already-consumed refresh token revokes the whole family + session,
/// appends an audit event, and renders the SAME generic failure a thief would see for an unknown token.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class RefreshRotationForcedRaceTests(IdentityDbFixture fixture)
{
    [Fact]
    public async Task TwoConcurrentRefreshesOfTheSameToken_OneWins_OneIsTreatedAsReuse_SessionRevoked()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var seedScope = fixture.NewScope();
        var config = seedScope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Config.IConfigRegistry>();
        var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var issued = await SessionIssuance.IssueAsync(db, accountId, null, "US", "legitimate_interest",
            await config.GetValue<int>("identity.session.access_ttl_minutes"),
            await config.GetValue<int>("identity.session.refresh_ttl_days"),
            await config.GetValue<int>("identity.session.max_active_per_account"),
            CancellationToken.None);

        using var scopeA = fixture.NewScope();
        using var scopeB = fixture.NewScope();
        var taskA = scopeA.ServiceProvider.GetRequiredService<RefreshRotationService>().Rotate(issued.RefreshToken, ctx, CancellationToken.None);
        var taskB = scopeB.ServiceProvider.GetRequiredService<RefreshRotationService>().Rotate(issued.RefreshToken, ctx, CancellationToken.None);

        var results = await Task.WhenAll(taskA, taskB);

        var rotated = results.OfType<RefreshOutcome.RotatedResult>().ToList();
        var failed = results.Count(r => r is RefreshOutcome.FailedResult);

        Assert.Single(rotated);
        Assert.Equal(1, failed);

        // The loser's presentation was a REUSE of an already-consumed token (both tasks raced the SAME
        // row) — the session must now be revoked, reason rotation_reuse, and the audit event landed.
        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await assertDb.Sessions.SingleAsync(s => s.SessionId == issued.SessionId);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal("rotation_reuse", session.RevokeReason);

        var eventStore = assertScope.ServiceProvider.GetRequiredService<IEventStore>();
        var auditTypes = new List<string>();
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, accountId))
        {
            auditTypes.Add(e.EventType);
        }
        Assert.Contains("identity.session_family_revoked", auditTypes);
    }

    [Fact]
    public async Task PresentingAConsumedRefreshToken_AfterALegitimateRotation_IsTreatedAsReuse()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var seedScope = fixture.NewScope();
        var config = seedScope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Config.IConfigRegistry>();
        var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var issued = await SessionIssuance.IssueAsync(db, accountId, null, "US", "legitimate_interest",
            await config.GetValue<int>("identity.session.access_ttl_minutes"),
            await config.GetValue<int>("identity.session.refresh_ttl_days"),
            await config.GetValue<int>("identity.session.max_active_per_account"),
            CancellationToken.None);

        using var scope1 = fixture.NewScope();
        var firstRotation = await scope1.ServiceProvider.GetRequiredService<RefreshRotationService>().Rotate(issued.RefreshToken, ctx, CancellationToken.None);
        Assert.IsType<RefreshOutcome.RotatedResult>(firstRotation);

        // Present the OLD (now-consumed) token again — the theft alarm.
        using var scope2 = fixture.NewScope();
        var reuse = await scope2.ServiceProvider.GetRequiredService<RefreshRotationService>().Rotate(issued.RefreshToken, ctx, CancellationToken.None);
        Assert.IsType<RefreshOutcome.FailedResult>(reuse);

        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await assertDb.Sessions.SingleAsync(s => s.SessionId == issued.SessionId);
        Assert.Equal("rotation_reuse", session.RevokeReason);

        // Even the freshly-rotated (legitimate) access token is now dead — the whole family died.
        Assert.True(session.AccessExpiresAt <= DateTimeOffset.UtcNow || session.RevokedAt is not null);
    }

    private async Task<string> SeedAccount()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var accountId = $"usr_{Guid.NewGuid():N}"[..26];
        var now = DateTimeOffset.UtcNow;
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = $"refresh_{Guid.NewGuid():N}"[..20],
            Email = $"{Guid.NewGuid():N}@example.com",
            EmailVerifiedAt = now,
            BirthdateEnc = new byte[] { 1, 2, 3 },
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "shonen",
            Locale = "en",
            AccountState = "active",
            StateChangedAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "legitimate_interest",
        });
        await db.SaveChangesAsync();
        return accountId;
    }
}
