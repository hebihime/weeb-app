using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Auth;
using Svac.Identity.Consent;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Forced-race + idempotency proofs for POST /v1/signup/complete (SLICE_S3_CONTRACT.md §8/§10.3):
/// verifiedToken single-consumption (replay returns the winner's session) and handle uniqueness (23505
/// catch renders `handle.taken`, never a crash or a duplicate account).
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class SignupCompleteForcedRaceTests(IdentityDbFixture fixture)
{
    private static readonly string[] EnLocales = { "en" };

    [Fact]
    public async Task ReplayingTheSameVerifiedToken_AfterItAlreadyWon_ReturnsTheWinnersAccount_NeverADuplicate()
    {
        var email = UniqueEmail();
        var handle = UniqueHandle();
        var verifiedToken = await IssueAndConfirm(email);
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope1 = fixture.NewScope();
        var first = await scope1.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, handle, "2000-01-01", "shonen", "en", EnLocales, ctx, CancellationToken.None);
        var firstAccountId = Assert.IsType<SignupCompleteOutcome.SessionResult>(first).AccountId;

        // REPLAY: the exact same verifiedToken, presented again — a different handle in the replay
        // request must be IGNORED (the account already exists); idempotent under race means this
        // resolves to the WINNER's account, never a second account and never an error.
        using var scope2 = fixture.NewScope();
        var replay = await scope2.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, UniqueHandle(), "2000-01-01", "shonen", "en", EnLocales, ctx, CancellationToken.None);
        var replayResult = Assert.IsType<SignupCompleteOutcome.SessionResult>(replay);

        Assert.Equal(firstAccountId, replayResult.AccountId);
        // Exactly ONE account row exists for this email — the replay never created a second one.
        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var count = await db.Accounts.CountAsync(a => a.Email == email);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TwoDifferentSignups_RacingForTheSameHandle_OneWinsOneGetsHandleTaken_NeverACrash()
    {
        var handle = UniqueHandle();
        var tokenA = await IssueAndConfirm(UniqueEmail());
        var tokenB = await IssueAndConfirm(UniqueEmail());
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scopeA = fixture.NewScope();
        using var scopeB = fixture.NewScope();
        var taskA = scopeA.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(tokenA, handle, "2000-01-01", "shonen", "en", EnLocales, ctx, CancellationToken.None);
        var taskB = scopeB.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(tokenB, handle, "2000-01-01", "shonen", "en", EnLocales, ctx, CancellationToken.None);

        var results = await Task.WhenAll(taskA, taskB);

        var sessions = results.OfType<SignupCompleteOutcome.SessionResult>().ToList();
        var taken = results.OfType<SignupCompleteOutcome.HandleTakenResult>().ToList();

        Assert.Single(sessions);
        Assert.Single(taken);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(1, await db.Accounts.CountAsync(a => a.Handle == handle));
    }

    [Fact]
    public async Task SuccessfulCompletion_WritesAgeAttestationAndTermsAcceptance_ReadableBackOffConsentCurrent()
    {
        var email = UniqueEmail();
        var verifiedToken = await IssueAndConfirm(email);
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, UniqueHandle(), "2000-01-01", "shonen", "en", EnLocales, ctx, CancellationToken.None);
        var accountId = Assert.IsType<SignupCompleteOutcome.SessionResult>(outcome).AccountId;

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var kinds = await db.ConsentCurrent.Where(c => c.AccountId == accountId).Select(c => c.ConsentKind).ToListAsync();
        Assert.Contains("age_attestation_18_plus", kinds);
        Assert.Contains("terms_acceptance", kinds);

        // The audit + behavioral events landed too (same tx) — read back off the raw stream.
        var eventStore = assertScope.ServiceProvider.GetRequiredService<IEventStore>();
        var auditEvents = new List<string>();
        await foreach (var e in eventStore.ReadStream(StreamType.Audit, accountId))
        {
            auditEvents.Add(e.EventType);
        }
        Assert.Contains("identity.account_created", auditEvents);
    }

    private async Task<string> IssueAndConfirm(string email)
    {
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());
        using var scope = fixture.NewScope();
        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        await challenges.IssueForSignup(email, "en", ctx, CancellationToken.None);

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.EmailChallenges.SingleAsync(c => c.EmailLower == email && c.Purpose == "signup");
        var code = await RecoverCodeForTest(scope, row.ChallengeId, email);

        var confirmed = await challenges.ConfirmSignupCode(row.ChallengeId, code, ctx, CancellationToken.None);
        return Assert.IsType<ChallengeConfirmResult.ConfirmedResult>(confirmed).VerifiedToken;
    }

    /// <summary>Test-only: recovers the plaintext code from the FakeEmailSender's captured send (never Mailpit at this layer — that is the live E2E's job) so forced-race fixtures can drive the real confirm path end-to-end.</summary>
    private Task<string> RecoverCodeForTest(IServiceScope scope, string challengeId, string email)
    {
        var sent = fixture.Emails.Sent.LastOrDefault(m => m.To == email && m.TemplateKey == "email.verify_code")
            ?? throw new InvalidOperationException($"no verify_code mail captured for {email}.");
        return Task.FromResult(sent.Model["code"]);
    }

    private static string UniqueEmail() => $"race-{Guid.NewGuid():N}@example.com";
    private static string UniqueHandle() => $"race_{Guid.NewGuid():N}"[..20];
}
