using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.Identity.Auth;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>Forced-race proof for the confirm step's guarded consumption (SLICE_S3_CONTRACT.md §8: "code confirm (guarded-UPDATE CAS)") — two concurrent confirm attempts against the SAME challenge row, only one legitimate code among several guesses, never double-grants and never crashes.</summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class EmailChallengeConfirmForcedRaceTests(IdentityDbFixture fixture)
{
    [Fact]
    public async Task ConcurrentConfirmAttempts_WrongCodeAndRightCode_OnlyTheRightOneConfirms()
    {
        var email = $"confirm-race-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var issueScope = fixture.NewScope();
        await issueScope.ServiceProvider.GetRequiredService<EmailChallengeMachine>().IssueForSignup(email, "en", ctx, CancellationToken.None);

        var sent = fixture.Emails.Sent.Last(m => m.To == email && m.TemplateKey == "email.verify_code");
        var challengeId = await FindChallengeId(email);
        var realCode = sent.Model["code"];
        var wrongCode = realCode == "000000" ? "111111" : "000000";

        using var scopeA = fixture.NewScope();
        using var scopeB = fixture.NewScope();
        var wrongTask = scopeA.ServiceProvider.GetRequiredService<EmailChallengeMachine>().ConfirmSignupCode(challengeId, wrongCode, ctx, CancellationToken.None);
        var rightTask = scopeB.ServiceProvider.GetRequiredService<EmailChallengeMachine>().ConfirmSignupCode(challengeId, realCode, ctx, CancellationToken.None);

        var results = await Task.WhenAll(wrongTask, rightTask);

        Assert.Contains(results, r => r is ChallengeConfirmResult.InvalidResult);
        Assert.Contains(results, r => r is ChallengeConfirmResult.ConfirmedResult);
    }

    [Fact]
    public async Task ExhaustingMaxAttempts_WithWrongCodes_ThenTheRealCode_StillFails()
    {
        var email = $"exhaust-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var issueScope = fixture.NewScope();
        var machine = issueScope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        await machine.IssueForSignup(email, "en", ctx, CancellationToken.None);

        var sent = fixture.Emails.Sent.Last(m => m.To == email && m.TemplateKey == "email.verify_code");
        var challengeId = await FindChallengeId(email);
        var realCode = sent.Model["code"];
        var wrongCode = realCode == "000000" ? "111111" : "000000";

        // identity.email_code.max_attempts = 5 (identity.config.json v0) — exhaust it with wrong guesses.
        for (var i = 0; i < 5; i++)
        {
            using var scope = fixture.NewScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>().ConfirmSignupCode(challengeId, wrongCode, ctx, CancellationToken.None);
            Assert.IsType<ChallengeConfirmResult.InvalidResult>(outcome);
        }

        using var finalScope = fixture.NewScope();
        var finalOutcome = await finalScope.ServiceProvider.GetRequiredService<EmailChallengeMachine>().ConfirmSignupCode(challengeId, realCode, ctx, CancellationToken.None);
        Assert.IsType<ChallengeConfirmResult.InvalidResult>(finalOutcome);
    }

    private async Task<string> FindChallengeId(string email)
    {
        using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Svac.Identity.Persistence.IdentityDbContext>();
        var row = await db.EmailChallenges.SingleAsync(c => c.EmailLower == email && c.Purpose == "signup");
        return row.ChallengeId;
    }
}
