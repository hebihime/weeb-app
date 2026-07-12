using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Auth;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// POST /v1/me/handle (SLICE_S3_CONTRACT.md §1c/§3b/§5): cooldown = handle_history max(changed_at) +
/// identity.handle.cooldown_days, rendered as THE one LimitReached shape (never a second deny surface);
/// reserved/retired/taken collapse to ONE handle.taken outcome; a successful change writes handle_history
/// and an identity.handle_changed audit event in the SAME tx as the accounts.handle update.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class HandleChangeTests(IdentityDbFixture fixture)
{
    [Fact]
    public async Task FirstHandleChange_Succeeds_WritesHistoryAndAuditEvent_NoCooldownYet()
    {
        var accountId = await SeedAccount();
        var newHandle = UniqueHandle();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<HandleChangeService>().Change(accountId, newHandle, ctx, CancellationToken.None);

        var changed = Assert.IsType<HandleChangeOutcome.ChangedResult>(outcome);
        Assert.Equal(newHandle, changed.NewHandle);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal(newHandle, account.Handle);

        var history = await db.HandleHistory.SingleAsync(h => h.AccountId == accountId);
        Assert.Equal(newHandle, history.NewHandle);

        var eventStore = assertScope.ServiceProvider.GetRequiredService<IEventStore>();
        var auditTypes = new List<string>();
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, accountId))
        {
            auditTypes.Add(e.EventType);
        }
        Assert.Contains("identity.handle_changed", auditTypes);
    }

    [Fact]
    public async Task SecondChangeWithinCooldown_DeniedAsLimitReached_WithCorrectResetsAtAndPremiumExtendsFalse()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope1 = fixture.NewScope();
        var service = scope1.ServiceProvider.GetRequiredService<HandleChangeService>();
        var first = await service.Change(accountId, UniqueHandle(), ctx, CancellationToken.None);
        Assert.IsType<HandleChangeOutcome.ChangedResult>(first);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var firstChangedAt = await db.HandleHistory.Where(h => h.AccountId == accountId).Select(h => h.ChangedAt).SingleAsync();

        using var scope2 = fixture.NewScope();
        var second = await scope2.ServiceProvider.GetRequiredService<HandleChangeService>().Change(accountId, UniqueHandle(), ctx, CancellationToken.None);

        var cooldown = Assert.IsType<HandleChangeOutcome.CooldownResult>(second);
        Assert.Equal("identity.handle.change", cooldown.LimitReached.QuotaKey);
        Assert.False(cooldown.LimitReached.PremiumExtends);
        // resetsAt = last change + identity.handle.cooldown_days (v0 30, per identity.config.json).
        Assert.Equal(firstChangedAt.AddDays(30), cooldown.LimitReached.ResetsAt);

        // No second history row was written — the denied attempt left zero trace on the mutation surface.
        Assert.Equal(1, await db.HandleHistory.CountAsync(h => h.AccountId == accountId));
    }

    [Fact]
    public async Task ReservedRetiredAndTaken_AllRenderTheOneHandleTakenOutcome()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        var reserved = $"reservedhc_{Guid.NewGuid():N}"[..20];
        var retired = $"retiredhc_{Guid.NewGuid():N}"[..20];
        var taken = $"takenhc_{Guid.NewGuid():N}"[..20];

        using (var seedScope = fixture.NewScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.ReservedHandles.Add(new ReservedHandleEntity { Handle = reserved, Reason = "brand" });
            db.RetiredHandles.Add(new RetiredHandleEntity { Handle = retired, RetiredAt = DateTimeOffset.UtcNow });
            await SeedAccountRow(db, taken, "active");
            await db.SaveChangesAsync();
        }

        foreach (var handle in new[] { reserved, retired, taken })
        {
            using var scope = fixture.NewScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<HandleChangeService>().Change(accountId, handle, ctx, CancellationToken.None);
            Assert.IsType<HandleChangeOutcome.TakenResult>(outcome);
        }
    }

    [Fact]
    public async Task InvalidHandleShape_RendersInvalidOutcome_NeverTaken()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<HandleChangeService>().Change(accountId, "x", ctx, CancellationToken.None); // too short

        Assert.IsType<HandleChangeOutcome.InvalidResult>(outcome);
    }

    [Fact]
    public async Task SameHandleAsCurrent_IsANoOp_NeverConsumesTheCooldownOrWritesHistory()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var currentHandle = await db.Accounts.Where(a => a.AccountId == accountId).Select(a => a.Handle).SingleAsync();

        var outcome = await scope.ServiceProvider.GetRequiredService<HandleChangeService>().Change(accountId, currentHandle, ctx, CancellationToken.None);
        Assert.IsType<HandleChangeOutcome.NoOpResult>(outcome);

        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await assertDb.HandleHistory.CountAsync(h => h.AccountId == accountId));
    }

    private async Task<string> SeedAccount()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var accountId = $"usr_{Guid.NewGuid():N}"[..26];
        await SeedAccountRow(db, UniqueHandle(), "active", accountId);
        await db.SaveChangesAsync();
        return accountId;
    }

    private static Task SeedAccountRow(IdentityDbContext db, string handle, string state, string? accountId = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId ?? $"usr_{Guid.NewGuid():N}"[..26],
            Handle = handle,
            Email = $"hc-{Guid.NewGuid():N}@example.com",
            EmailVerifiedAt = now,
            BirthdateEnc = new byte[] { 1, 2, 3 },
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "shonen",
            Locale = "en",
            AccountState = state,
            StateChangedAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "legitimate_interest",
        });
        return Task.CompletedTask;
    }

    private static string UniqueHandle() => $"hc_{Guid.NewGuid():N}"[..20];
}
