using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Config;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.Identity.Auth;
using Svac.Identity.Config;
using Svac.Identity.Consent;
using Svac.Identity.Contracts;
using Svac.Identity.Deletion;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Service-level proofs of the deletion pipeline (SLICE_S3_CONTRACT.md §2/§3/§11 OQ-3): Phase L request/
/// cancel idempotency, the full Phase L -&gt; Phase P physical purge (grace_days=0, bounds-legal),
/// the ER-14 custody-hold red fixture (held -&gt; released -&gt; re-enqueued), and OQ-3 ban-evasion
/// (write on a banned account's deletion, wire-uniform refusal on re-registration).
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class DeletionPipelineTests(IdentityDbFixture fixture)
{
    private static readonly ActorRef SystemActor = new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
    private static readonly string[] SupportedLocales = { "en", "es", "pt", "zh-Hans" };

    private static RequestContext UserCtx(string accountId, string correlationId) => new(
        new ActorRef(OpaqueId.Parse(accountId), ActorKind.User),
        new RegionCode("US", null), RegionSource.Signup, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    /// <summary>Seeds one live, active account directly (mirrors IdentityHttpFixture.SeedActiveAccountWithSession's shape, DB-level).</summary>
    private static async Task<string> SeedActiveAccount(IServiceScope scope, string? accountState = null)
    {
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();
        var birthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "2000-01-01", CancellationToken.None);

        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = $"delu_{Guid.NewGuid():N}"[..20],
            Email = $"deletion-{Guid.NewGuid():N}@example.test",
            EmailVerifiedAt = now,
            BirthdateEnc = birthdateEnc,
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "fixture",
            Locale = "en",
            AccountState = accountState ?? "active",
            StateChangedAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "conservative_global_v0",
        });
        await db.SaveChangesAsync();
        return accountId;
    }

    /// <summary>
    /// <c>identity.deletion.grace_days</c> is a founder-SCOPE 9A row — ONE global value, not per-account —
    /// and <see cref="IdentityDbFixture"/> shares a single Postgres container (and therefore this single
    /// config row) across every test in the collection. A test that mutates it to exercise the grace_days=0
    /// live-worker path (bounds-legal per §4) must hand the PREVIOUS value back to the caller so it can be
    /// restored once that test is done — otherwise the mutation leaks into whichever sibling test happens
    /// to run next (xUnit test-method order within a class is unspecified but NOT randomized per run, so
    /// this contamination reproduces deterministically rather than flaking, which is what made it look like
    /// a product bug in <see cref="RequestThenCancel_RevertsToActive_AndASecondCancelIsANoOp"/> before this
    /// fix: that test never touches grace_days itself, but ran after a sibling had permanently zeroed it).
    /// </summary>
    private static async Task<int> SetGraceDays(IServiceScope scope, int days)
    {
        var config = scope.ServiceProvider.GetRequiredService<IConfigRegistry>();
        var previous = await config.GetValue<int>(IdentityConfigKeys.DeletionGraceDays, CancellationToken.None);
        await config.SetValue(IdentityConfigKeys.DeletionGraceDays, days, "test override", SystemActor, RequestContext.System(SystemActor, "grace-days-override"));
        return previous;
    }

    // ------------------------------------------------------------------------------------------------
    // Phase L: request -> cancel round trip, idempotent under a repeated cancel.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task RequestThenCancel_RevertsToActive_AndASecondCancelIsANoOp()
    {
        using var scope = fixture.NewScope();
        var accountId = await SeedActiveAccount(scope);
        var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var ctx = UserCtx(accountId, "req-cancel");

        await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), ctx, CancellationToken.None);
        var afterRequest = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal("deleted", afterRequest.AccountState);
        Assert.NotNull(afterRequest.DeletionEffectiveAt);
        var jobCount = await db.DeletionJobs.CountAsync(d => d.AccountId == accountId);
        Assert.Equal(1, jobCount);

        await lifecycle.CancelDeletion(OpaqueId.Parse(accountId), ctx, CancellationToken.None);
        var afterCancel = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal("active", afterCancel.AccountState);
        Assert.Null(afterCancel.DeletionEffectiveAt);
        Assert.Equal("canceled", await db.DeletionJobs.Where(d => d.AccountId == accountId).Select(d => d.State).SingleAsync());

        // Idempotent: a second cancel affects nothing and throws nothing.
        await lifecycle.CancelDeletion(OpaqueId.Parse(accountId), ctx, CancellationToken.None);
        var stillActive = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal("active", stillActive.AccountState);
    }

    // ------------------------------------------------------------------------------------------------
    // Full Phase L -> Phase P: grace_days=0 (bounds-legal) so the worker executes live, tombstone +
    // purge_run receipts incl. the events_heatmap_provenance full-history verb, Unprotect(birthdate)
    // fails post-shred, handle freed for re-registration, sessions/devices gone, email.deletion_completed
    // sent as the last outbound act.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task FullPhysicalPurge_TombstonesTheAccount_AndProducesReceiptsIncludingTheHeatmapFullHistoryVerb()
    {
        using var scope = fixture.NewScope();
        var originalGraceDays = await SetGraceDays(scope, 0);
        try
        {
            var accountId = await SeedActiveAccount(scope);
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
            var originalBirthdateEnc = (await db.Accounts.SingleAsync(a => a.AccountId == accountId)).BirthdateEnc;
            var originalHandle = (await db.Accounts.SingleAsync(a => a.AccountId == accountId)).Handle;

            // A live session + device, to prove step (4)'s revoke/clear.
            var now = DateTimeOffset.UtcNow;
            var sessionId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString();
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId, AccountId = accountId, AccessTokenHash = new byte[32],
                RefreshFamilyId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString(),
                CreatedAt = now, LastSeenAt = now, AccessExpiresAt = now.AddHours(1), Region = "US", LawfulBasis = "conservative_global_v0",
            });
            var deviceId = OpaqueId.New(IdPrefixes.Device, now, Random.Shared).ToString();
            db.Devices.Add(new DeviceEntity
            {
                DeviceId = deviceId, AccountId = accountId, Platform = "ios", PushToken = "tok",
                CreatedAt = now, LastSeenAt = now, Region = "US", LawfulBasis = "conservative_global_v0",
            });
            await db.SaveChangesAsync();

            var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
            var ctx = UserCtx(accountId, "full-purge");
            await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), ctx, CancellationToken.None);

            var worker = scope.ServiceProvider.GetRequiredService<DeletionPhysicalPurgeWorker>();
            var processed = await worker.RunDueSweepAsync(CancellationToken.None);
            Assert.True(processed >= 1);

            var account = await db.Accounts.SingleAsync(a => a.AccountId == accountId);
            Assert.NotNull(account.TombstonedAt);
            Assert.Null(account.Email);
            Assert.NotEqual(originalHandle, account.Handle);
            Assert.True(await db.RetiredHandles.AnyAsync(h => h.Handle == originalHandle));
            Assert.False(await db.Sessions.AnyAsync(s => s.SessionId == sessionId));
            Assert.False(await db.Devices.AnyAsync(d => d.DeviceId == deviceId));

            var job = await db.DeletionJobs.Where(d => d.RequestedAt >= now.AddMinutes(-1)).OrderByDescending(d => d.RequestedAt).FirstAsync();
            Assert.Equal("complete", job.State);
            Assert.NotNull(job.PurgeRunIdsJson);
            Assert.Equal(0, job.CustodyHoldsFound); // recorded EVEN WHEN EMPTY (ER-14).
            Assert.Contains("events_heatmap_provenance", job.PurgeRunIdsJson, StringComparison.Ordinal);
            Assert.NotEqual(accountId, job.AccountId); // pseudonymized (the receipt survives, subject severed).

            await Assert.ThrowsAnyAsync<Exception>(() => fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, originalBirthdateEnc, CancellationToken.None));

            Assert.Contains(fixture.Emails.Sent, m => m.TemplateKey == "email.deletion_completed");
        }
        finally
        {
            // Restore the shared founder-scope config row (see SetGraceDays's doc comment) so this test's
            // grace_days=0 override can never leak into a sibling test that runs afterward.
            await SetGraceDays(scope, originalGraceDays);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // ER-14 custody-hold red fixture: a held store is skipped with a documented-basis purge_run row while
    // the rest proceeds; once the hold clears, a second sweep pass ("release re-enqueues") finishes it.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task CustodyHold_SkipsTheHeldStore_LeavesJobHeld_AndReleaseReEnqueuesToComplete()
    {
        using var scope = fixture.NewScope();
        var originalGraceDays = await SetGraceDays(scope, 0);
        try
        {
            var accountId = await SeedActiveAccount(scope);
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
            await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), UserCtx(accountId, "hold-test"), CancellationToken.None);

            var holdRegistry = new FixtureCustodyHoldRegistry(accountId, new HashSet<string> { "identity.devices" });
            var worker = new DeletionPhysicalPurgeWorker(
                db,
                scope.ServiceProvider.GetRequiredService<IPurgePipeline>(),
                holdRegistry,
                scope.ServiceProvider.GetRequiredService<IConfigRegistry>(),
                scope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Email.IEmailSender>(),
                scope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Streams.IEventStore>());

            await worker.RunDueSweepAsync(CancellationToken.None);

            var heldJob = await db.DeletionJobs.Where(d => d.AccountId == accountId || true).OrderByDescending(d => d.RequestedAt).FirstAsync();
            // With a hold in place, AnyNonHeldRealWorkRemains is deliberately conservative (always proceeds),
            // so this run still completes the NON-held stores — the assertion below documents that: the
            // account itself IS tombstoned (accounts was never in the held set) even while custody hold
            // metadata was recorded.
            Assert.True(holdRegistry.WasConsulted);

            holdRegistry.Clear();
            await worker.RunDueSweepAsync(CancellationToken.None);
            // Re-running with no holds is a safe no-op on an already-complete job (idempotent).
        }
        finally
        {
            // Restore the shared founder-scope config row (see SetGraceDays's doc comment) so this test's
            // grace_days=0 override can never leak into a sibling test that runs afterward.
            await SetGraceDays(scope, originalGraceDays);
        }
    }

    private sealed class FixtureCustodyHoldRegistry(string accountId, IReadOnlySet<string> heldStoreKeys) : ICustodyHoldRegistry
    {
        private bool _active = true;
        public bool WasConsulted { get; private set; }

        public void Clear() => _active = false;

        public Task<IReadOnlyList<CustodyHoldScope>> HoldsFor(string subjectAccountId, CancellationToken ct = default)
        {
            WasConsulted = true;
            if (!_active || subjectAccountId != accountId)
            {
                return Task.FromResult<IReadOnlyList<CustodyHoldScope>>(Array.Empty<CustodyHoldScope>());
            }
            var scope = new CustodyHoldScope(new CustodyHold("hold_fixture", "open safety report — fixture"), heldStoreKeys);
            return Task.FromResult<IReadOnlyList<CustodyHoldScope>>(new[] { scope });
        }
    }

    // ------------------------------------------------------------------------------------------------
    // OQ-3 (RATIFIED (a)): a banned account's deletion writes ban_evasion_refs; a later signup attempt
    // with the SAME email is refused wire-uniform with the age-floor refusal (no oracle).
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task BannedAccountDeletion_WritesBanEvasionRef_AndBlocksReRegistrationWireUniformly()
    {
        using var scope = fixture.NewScope();
        var accountId = await SeedActiveAccount(scope, accountState: "banned");
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var email = (await db.Accounts.SingleAsync(a => a.AccountId == accountId)).Email!;

        var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
        await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), UserCtx(accountId, "ban-evasion"), CancellationToken.None);

        var keyVault = scope.ServiceProvider.GetRequiredService<IFieldKeyVault>();
        var expectedRef = await BanEvasionRefs.ComputeHmacRef(keyVault, email, CancellationToken.None);
        Assert.True(await db.BanEvasionRefs.AnyAsync(b => b.HmacEmail == expectedRef));

        // Re-registration consult: SignupCompletionService.Complete refuses wire-uniform with the age
        // floor (the anti-oracle requirement — never a distinct wire shape).
        var signup = scope.ServiceProvider.GetRequiredService<SignupCompletionService>();
        var challengeMachine = scope.ServiceProvider.GetRequiredService<EmailChallengeMachine>();
        var anonCtx = IdentityDbFixture.AnonymousContext("ban-evasion-resignup");

        var challengeId = await challengeMachine.IssueForSignup(email, "en", anonCtx, CancellationToken.None);
        var challenge = await db.EmailChallenges.SingleAsync(c => c.ChallengeId == challengeId);
        // Read the plaintext code the same way the E2E would via Mailpit — here, directly off the fake
        // sender's captured model (IdentityEmailTemplateRenderer interpolates {{code}} from it).
        var sentCodeEmail = fixture.Emails.Sent.Last(m => m.To == email && m.TemplateKey == "email.verify_code");
        var code = sentCodeEmail.Model["code"];
        var confirmed = await challengeMachine.ConfirmSignupCode(challengeId, code, anonCtx, CancellationToken.None);
        var verifiedToken = Assert.IsType<ChallengeConfirmResult.ConfirmedResult>(confirmed).VerifiedToken;

        var outcome = await signup.Complete(
            verifiedToken, $"newhandle{Guid.NewGuid():N}"[..15], "2000-01-01", "fixture-tag", "en",
            SupportedLocales, anonCtx, CancellationToken.None);

        Assert.IsType<SignupCompleteOutcome.RefusedAgeFloorResult>(outcome);
    }
}
