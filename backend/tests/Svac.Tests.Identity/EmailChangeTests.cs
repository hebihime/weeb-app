using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Auth;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// PUT /v1/me/email (+ POST /v1/me/email/confirm) (SLICE_S3_CONTRACT.md §1c/§1b/§7): the SAME challenge
/// machine, code to the NEW address; confirm swaps the email and appends an identity.email_changed audit
/// event same-tx; the endpoint (not this service) sends the old-address security notice AFTER commit —
/// covered by the collision-branch assertion below (IssueForEmailChange sends the mail directly for the
/// anti-enumeration case, mirroring IssueForSignup's already-registered branch).
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class EmailChangeTests(IdentityDbFixture fixture)
{
    [Fact]
    public async Task IssueThenConfirm_SwapsTheEmail_AndAppendsTheAuditEvent()
    {
        var (accountId, oldEmail) = await SeedActiveAccount();
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var challengeId = await challenges.IssueForEmailChange(accountId, newEmail, "en", ctx, CancellationToken.None);

        var sent = fixture.Emails.Sent.Last(m => m.To == newEmail && m.TemplateKey == "email.verify_code");
        var outcome = await challenges.ConfirmEmailChange(accountId, challengeId, sent.Model["code"], ctx, CancellationToken.None);

        var swapped = Assert.IsType<EmailChangeConfirmResult.SwappedResult>(outcome);
        Assert.Equal(oldEmail, swapped.OldEmail);
        Assert.Equal(newEmail, swapped.NewEmail);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal(newEmail, account.Email);

        var eventStore = assertScope.ServiceProvider.GetRequiredService<IEventStore>();
        var auditTypes = new List<string>();
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, accountId))
        {
            auditTypes.Add(e.EventType);
        }
        Assert.Contains("identity.email_changed", auditTypes);
    }

    [Fact]
    public async Task ConfirmWithWrongCode_RendersInvalid_NeverSwapsTheEmail()
    {
        var (accountId, oldEmail) = await SeedActiveAccount();
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var challengeId = await challenges.IssueForEmailChange(accountId, newEmail, "en", ctx, CancellationToken.None);

        var outcome = await challenges.ConfirmEmailChange(accountId, challengeId, "000000", ctx, CancellationToken.None);
        Assert.IsType<EmailChangeConfirmResult.InvalidResult>(outcome);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal(oldEmail, account.Email);
    }

    [Fact]
    public async Task ConfirmFromADifferentAccount_RendersInvalid_ChallengeOwnershipIsChecked()
    {
        var (accountId, _) = await SeedActiveAccount();
        var (otherAccountId, _) = await SeedActiveAccount();
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var challengeId = await challenges.IssueForEmailChange(accountId, newEmail, "en", ctx, CancellationToken.None);
        var sent = fixture.Emails.Sent.Last(m => m.To == newEmail && m.TemplateKey == "email.verify_code");

        // otherAccountId presenting accountId's own challenge — must be denied, never honored.
        var outcome = await challenges.ConfirmEmailChange(otherAccountId, challengeId, sent.Model["code"], ctx, CancellationToken.None);
        Assert.IsType<EmailChangeConfirmResult.InvalidResult>(outcome);
    }

    [Fact]
    public async Task RequestingAnAlreadyRegisteredEmail_NeverPersistsARow_SendsAlreadyRegisteredMailToItsRealOwnerInstead()
    {
        var (accountId, _) = await SeedActiveAccount();
        var (ownedByOther, otherEmail) = await SeedActiveAccount();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var challengeId = await scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>()
            .IssueForEmailChange(accountId, otherEmail, "en", ctx, CancellationToken.None);

        // Byte-identical wire shape either way (a syntactically valid challengeId) — but structurally, no
        // row exists to confirm, and the mail landed on the REAL owner's mailbox, never disclosing to the
        // requester which account already holds that address.
        Assert.NotEmpty(challengeId);
        Assert.Contains(fixture.Emails.Sent, m => m.To == otherEmail && m.TemplateKey == "email.already_registered");
        Assert.DoesNotContain(fixture.Emails.Sent, m => m.To == otherEmail && m.TemplateKey == "email.verify_code");

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Null(await db.EmailChallenges.SingleOrDefaultAsync(c => c.ChallengeId == challengeId));

        var ownerAccount = await db.Accounts.SingleAsync(a => a.AccountId == ownedByOther);
        Assert.Equal(otherEmail, ownerAccount.Email); // untouched
    }

    private async Task<(string AccountId, string Email)> SeedActiveAccount()
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        var accountId = $"usr_{Guid.NewGuid():N}"[..26];
        var seededEmail = $"ec-{Guid.NewGuid():N}@example.com";
        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = $"ec_{Guid.NewGuid():N}"[..20],
            Email = seededEmail,
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
        return (accountId, seededEmail);
    }
}
