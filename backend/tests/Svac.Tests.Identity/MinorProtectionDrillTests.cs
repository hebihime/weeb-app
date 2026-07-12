using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Auth;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Minor-protection drills (SLICE_S3_CONTRACT.md §1g/§8/§10.3/§10.4): under-18 and under-13 render the
/// SAME wire outcome (RefusedAgeFloorResult, wire-identical), zero persistence either way, the under-13
/// challenge row is provably destroyed, and the refusal behavioral event carries zero identifiers.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class MinorProtectionDrillTests(IdentityDbFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly string[] EnLocales = { "en" };

    [Fact]
    public async Task Under18Signup_RefusedIdenticallyToUnder13_ZeroAccountRowPersists_ChallengeRowSurvives()
    {
        var age17Birthdate = Today.AddYears(-17).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (email, verifiedToken, challengeId) = await IssueAndConfirm();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, $"minor17_{Guid.NewGuid():N}"[..20], age17Birthdate, "shonen", "en", EnLocales, ctx, CancellationToken.None);

        Assert.IsType<SignupCompleteOutcome.RefusedAgeFloorResult>(outcome);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await db.Accounts.CountAsync(a => a.Email == email));
        // Under-18-but-13-plus: the challenge row SURVIVES unconsumed (a legitimate resubmission with a
        // corrected birthdate can still complete before TTL) — this is the ONE structural difference from
        // the under-13 branch, invisible on the wire (both render the identical Problem).
        var row = await db.EmailChallenges.SingleOrDefaultAsync(c => c.ChallengeId == challengeId);
        Assert.NotNull(row);
        Assert.Null(row!.ConsumedAt);
    }

    [Fact]
    public async Task Under13Signup_RefusedIdenticallyToUnder18_ZeroAccountRowPersists_ChallengeRowHardDeleted()
    {
        var age10Birthdate = Today.AddYears(-10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (email, verifiedToken, challengeId) = await IssueAndConfirm();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, $"minor10_{Guid.NewGuid():N}"[..20], age10Birthdate, "shonen", "en", EnLocales, ctx, CancellationToken.None);

        // Wire-identical outcome type to the under-18 drill above — same Problem shape at the endpoint layer.
        Assert.IsType<SignupCompleteOutcome.RefusedAgeFloorResult>(outcome);

        using var assertScope = fixture.NewScope();
        var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await db.Accounts.CountAsync(a => a.Email == email));
        // Under-13: the challenge row is provably DESTROYED, not merely marked consumed.
        var row = await db.EmailChallenges.SingleOrDefaultAsync(c => c.ChallengeId == challengeId);
        Assert.Null(row);
    }

    [Fact]
    public async Task RefusalBehavioralEvent_CarriesZeroIdentifiers()
    {
        var age10Birthdate = Today.AddYears(-10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (_, verifiedToken, _) = await IssueAndConfirm();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        await scope.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, $"minorevt_{Guid.NewGuid():N}"[..20], age10Birthdate, "shonen", "en", EnLocales, ctx, CancellationToken.None);

        using var assertScope = fixture.NewScope();
        var eventStore = assertScope.ServiceProvider.GetRequiredService<IEventStore>();
        var events = new List<Svac.DomainCore.Contracts.Streams.RecordedEvent>();
        await foreach (var e in eventStore.ReadStream(StreamType.Behavioral, ctx.Actor.Id.ToString()))
        {
            events.Add(e);
        }

        var refusal = Assert.Single(events, e => e.EventType == "identity.signup_refused_age");
        Assert.Equal("{}", refusal.PayloadJson);
    }

    [Fact]
    public async Task FutureBirthdate_IsAValidationErrorNeverAnAgeFloorVerdict()
    {
        var futureBirthdate = Today.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (_, verifiedToken, _) = await IssueAndConfirm();
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());

        using var scope = fixture.NewScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<SignupCompletionService>()
            .Complete(verifiedToken, $"future_{Guid.NewGuid():N}"[..20], futureBirthdate, "shonen", "en", EnLocales, ctx, CancellationToken.None);

        var validation = Assert.IsType<SignupCompleteOutcome.ValidationErrorResult>(outcome);
        Assert.Equal("birthdate", validation.Field);
    }

    private async Task<(string Email, string VerifiedToken, string ChallengeId)> IssueAndConfirm()
    {
        var email = $"minor-{Guid.NewGuid():N}@example.com";
        var ctx = IdentityDbFixture.AnonymousContext(Guid.NewGuid().ToString());
        using var scope = fixture.NewScope();
        var challenges = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var challengeId = await challenges.IssueForSignup(email, "en", ctx, CancellationToken.None);

        var sent = fixture.Emails.Sent.Last(m => m.To == email && m.TemplateKey == "email.verify_code");
        var confirmed = await challenges.ConfirmSignupCode(challengeId, sent.Model["code"], ctx, CancellationToken.None);
        var verifiedToken = Assert.IsType<ChallengeConfirmResult.ConfirmedResult>(confirmed).VerifiedToken;
        return (email, verifiedToken, challengeId);
    }
}
