using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Consent;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// PUT /v1/me/push-consents/{category} write path (SLICE_S3_CONTRACT.md §1c/§1b/§8): a category write
/// lands on `events_consent` (readable back off the raw stream) AND the `identity.push_category_consents`
/// projection row, in that order, via the same <see cref="IConsentLedgerWriter"/> door the signup
/// attestation/ToS consents use. Category 8 is UNREPRESENTABLE at the TYPE level (<see cref="PushCategoryValue"/>
/// has no member for it) — proven here as a compile-time/reflection fact, never a runtime range check
/// that a future edit could loosen.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class PushConsentWriteTests(IdentityDbFixture fixture)
{
    [Fact]
    public void Category8_HasNoMemberInThePushCategoryValueEnum_UnrepresentableNotJustChecked()
    {
        var names = Enum.GetNames<PushCategoryValue>();
        Assert.DoesNotContain("Category8", names);
        var values = Enum.GetValues<PushCategoryValue>().Cast<int>();
        Assert.DoesNotContain(8, values);
    }

    [Fact]
    public async Task WritingCategory3Enabled_LandsOnEventsConsent_AndTheProjectionRow()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());
        var subject = new SubjectRef("account", accountId);

        using var scope = fixture.NewScope();
        await scope.ServiceProvider.GetRequiredService<IConsentLedgerWriter>()
            .Record(subject, ConsentKind.PushCategory(PushCategoryValue.Category3), "v1", "push_consents", ConsentDecision.Granted, ctx, CancellationToken.None);

        using var assertScope = fixture.NewScope();
        var eventStore = assertScope.ServiceProvider.GetRequiredService<IEventStore>();
        var consentEvents = new List<string>();
        await foreach (var e in eventStore.ReadStream(StreamType.Consent, accountId))
        {
            consentEvents.Add(e.PayloadJson!);
        }
        Assert.Contains(consentEvents, p => p.Contains("push_category_3", StringComparison.Ordinal) && p.Contains("granted", StringComparison.Ordinal));

        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.PushCategoryConsents.SingleAsync(p => p.AccountId == accountId && p.Category == 3);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task RevokingAfterGranting_FlipsTheProjectionRow_LatestWriteWins()
    {
        var accountId = await SeedAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());
        var subject = new SubjectRef("account", accountId);

        using var scope1 = fixture.NewScope();
        var writer1 = scope1.ServiceProvider.GetRequiredService<IConsentLedgerWriter>();
        await writer1.Record(subject, ConsentKind.PushCategory(PushCategoryValue.Category5), "v1", "push_consents", ConsentDecision.Granted, ctx, CancellationToken.None);

        using var scope2 = fixture.NewScope();
        var writer2 = scope2.ServiceProvider.GetRequiredService<IConsentLedgerWriter>();
        await writer2.Record(subject, ConsentKind.PushCategory(PushCategoryValue.Category5), "v1", "push_consents", ConsentDecision.Revoked, ctx, CancellationToken.None);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.PushCategoryConsents.SingleAsync(p => p.AccountId == accountId && p.Category == 5);
        Assert.False(row.Enabled);
    }

    private async Task<string> SeedAccount()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        var accountId = $"usr_{Guid.NewGuid():N}"[..26];
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = $"pc_{Guid.NewGuid():N}"[..20],
            Email = $"pc-{Guid.NewGuid():N}@example.com",
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
