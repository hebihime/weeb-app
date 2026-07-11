using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;
using Svac.Identity.Config;
using Svac.Identity.Contracts;
using Svac.Identity.Deletion;
using Svac.Identity.Persistence;
using Xunit;

namespace Svac.Tests.Identity;

/// <summary>
/// Forced-race proofs for the Phase-P physical purge worker (SECURITY_REVIEW_S3.md CONC-1 / CONC-2 /
/// PII-2 / PII-9). CONC-1 is the CRITICAL finding: the shipped worker claimed a job with an unguarded
/// UPDATE-by-PK after an UNLOCKED account read, so a cancel racing in the gap was invisible to it and the
/// worker would physically purge an account whose owner had just received a successful cancel.
/// </summary>
[Collection(IdentityDbCollectionDefinition.Name)]
public sealed class DeletionWorkerRaceTests(IdentityDbFixture fixture)
{
    private static RequestContext UserCtx(string accountId, string correlationId) => new(
        new ActorRef(OpaqueId.Parse(accountId), ActorKind.User),
        new RegionCode("US", null), RegionSource.Signup, LawfulBasisVariant.ConservativeGlobalV0, "en", correlationId);

    /// <summary>Seeds one deleted-in-grace account (account_state='deleted', tombstoned_at NULL) with an INDEPENDENTLY-controlled effective_at/scheduled_for pair — the exact boundary window CONC-1 describes: the deletion_job's own scheduled_for is already due (so the sweep's own query picks it up) while the account's deletion_effective_at is still slightly in the future (so a concurrent CancelDeletion's own guard, `deletion_effective_at > now`, still passes when IT reads).</summary>
    private static async Task<(string AccountId, string DeletionId, byte[] BirthdateEnc, string Handle)> SeedDueJobWithGraceStillOpen(
        IServiceScope scope, TimeSpan effectiveAtLead)
    {
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();
        var handle = $"race_{Guid.NewGuid():N}"[..20];
        var birthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "1995-03-20", CancellationToken.None);

        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = handle,
            Email = $"conc1-{Guid.NewGuid():N}@example.test",
            EmailVerifiedAt = now,
            BirthdateEnc = birthdateEnc,
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "fixture",
            Locale = "en",
            AccountState = "deleted",
            StateChangedAt = now,
            DeletionRequestedAt = now,
            DeletionEffectiveAt = now.Add(effectiveAtLead), // grace still open when CancelDeletion reads it.
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "conservative_global_v0",
        });

        var deletionId = OpaqueId.New(IdPrefixes.Deletion, now, Random.Shared).ToString();
        db.DeletionJobs.Add(new DeletionJobEntity
        {
            DeletionId = deletionId,
            AccountId = accountId,
            State = "scheduled",
            RequestedAt = now,
            ScheduledFor = now.AddSeconds(-1), // ALREADY due — the sweep's own query picks this job up now.
            ExportOffered = true,
            Region = "US",
            LawfulBasis = "conservative_global_v0",
        });

        await db.SaveChangesAsync();
        return (accountId, deletionId, birthdateEnc, handle);
    }

    // ------------------------------------------------------------------------------------------------
    // CONC-1 (CRITICAL): interleave a cancel with the sweep — the cancel, having committed FIRST, must
    // make the worker abort with ZERO side effects. Forced via a gated ICustodyHoldRegistry that parks
    // the worker AFTER it has won the CAS claim (mutual exclusion already established) but BEFORE the
    // CONC-1 account re-verification runs — reproducing the exact "claimed, but not yet re-checked" window
    // the finding describes, with the cancel's own commit landing inside that window.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task CancelRacingTheSweep_CancelWins_WorkerAbortsWithZeroSideEffects()
    {
        using var seedScope = fixture.NewScope();
        var (accountId, deletionId, birthdateEnc, handle) = await SeedDueJobWithGraceStillOpen(seedScope, TimeSpan.FromSeconds(5));

        var gate = new GatedCustodyHoldRegistry();
        using var workerScope = fixture.NewScope();
        var worker = new DeletionPhysicalPurgeWorker(
            workerScope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            workerScope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Purge.IPurgePipeline>(),
            gate,
            workerScope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Config.IConfigRegistry>(),
            workerScope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Email.IEmailSender>(),
            workerScope.ServiceProvider.GetRequiredService<IEventStore>());

        var sweepTask = worker.RunDueSweepAsync(CancellationToken.None);
        await gate.Entered.Task; // worker has won the CAS claim (job now 'executing') and is parked mid custody-hold consult.

        using var cancelScope = fixture.NewScope();
        var lifecycle = cancelScope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
        await lifecycle.CancelDeletion(OpaqueId.Parse(accountId), UserCtx(accountId, "conc1-cancel"), CancellationToken.None);

        gate.Release.SetResult(); // let the worker proceed into its account re-verification.
        await sweepTask;

        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await assertDb.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.Equal("active", account.AccountState);
        Assert.Null(account.TombstonedAt);
        Assert.Null(account.DeletionEffectiveAt);
        Assert.Equal(handle, account.Handle); // never overwritten with the deleted_{id} sentinel.
        Assert.False(await assertDb.RetiredHandles.AnyAsync(h => h.Handle == handle));

        // The birthdate key must still be intact — crypto-shred never ran.
        var fieldEncryptor = assertScope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        var recovered = await fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, birthdateEnc, CancellationToken.None);
        Assert.Equal("1995-03-20", recovered);

        // The job itself must never be left 'executing' (CONC-2(b)'s dead-state bug) — the worker's own
        // abort path marks a lost-the-race job 'canceled'.
        var job = await assertDb.DeletionJobs.SingleAsync(d => d.DeletionId == deletionId);
        Assert.Equal("canceled", job.State);
    }

    // ------------------------------------------------------------------------------------------------
    // CONC-2(a): two concurrent sweeps racing the SAME due job — the guarded CAS must give exactly ONE
    // winner. Both sweep calls run genuinely concurrently (real DB-level locking decides the winner, not
    // an artificial gate) — proving mutual exclusion holds under real Task.WhenAll concurrency, not just
    // the single-worker-at-a-time happy path every other test exercises.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task TwoConcurrentSweeps_RacingTheSameDueJob_ExactlyOneExecutes()
    {
        using var seedScope = fixture.NewScope();
        var lifecycle = seedScope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
        var config = seedScope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Config.IConfigRegistry>();
        var originalGraceDays = await config.GetValue<int>(IdentityConfigKeys.DeletionGraceDays, CancellationToken.None);
        await config.SetValue(IdentityConfigKeys.DeletionGraceDays, 0, "test override", SystemActorFor(), RequestContext.System(SystemActorFor(), "conc2a-grace"));
        try
        {
            var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var accountId = OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();
            var fieldEncryptor = seedScope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
            var now = DateTimeOffset.UtcNow;
            db.Accounts.Add(new AccountEntity
            {
                AccountId = accountId, Handle = $"conc2a_{Guid.NewGuid():N}"[..20],
                Email = $"conc2a-{Guid.NewGuid():N}@example.test", EmailVerifiedAt = now,
                BirthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "1988-01-01", CancellationToken.None),
                AttestedAdultAt = now, TermsVersion = "v1", FandomTag = "fixture", Locale = "en",
                AccountState = "active", StateChangedAt = now, CreatedAt = now, LastActiveAt = now,
                Region = "US", RegionSource = "Signup", LawfulBasis = "conservative_global_v0",
            });
            await db.SaveChangesAsync();

            await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), UserCtx(accountId, "conc2a-request"), CancellationToken.None);
            // Capture the deletion_id (stable PK) BEFORE either sweep runs — account_id on this row gets
            // pseudonymized by the winning purge run, so the PK is the only way to re-find THIS row after.
            var deletionId = await db.DeletionJobs.Where(d => d.AccountId == accountId).Select(d => d.DeletionId).SingleAsync();

            using var scopeA = fixture.NewScope();
            using var scopeB = fixture.NewScope();
            var workerA = scopeA.ServiceProvider.GetRequiredService<DeletionPhysicalPurgeWorker>();
            var workerB = scopeB.ServiceProvider.GetRequiredService<DeletionPhysicalPurgeWorker>();

            var results = await Task.WhenAll(workerA.RunDueSweepAsync(CancellationToken.None), workerB.RunDueSweepAsync(CancellationToken.None));
            // Mutual exclusion is proven DETERMINISTICALLY below (exactly ONE completion email + ONE
            // purge-run receipt + job 'complete'), NOT by results.Sum(): under the guarded CAS the SECOND
            // sweep can LEGITIMATELY observe zero due jobs (the first already claimed/completed it) and
            // return 0, so the processed-count is timing-dependent (green locally, sum=1 on CI's slower
            // scheduling) and is NOT the invariant. At least one sweep must have picked it up.
            Assert.True(results.Sum() >= 1, "at least one concurrent sweep must have processed the due job");

            using var assertScope = fixture.NewScope();
            var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var job = await assertDb.DeletionJobs.SingleAsync(d => d.DeletionId == deletionId);
            Assert.Equal("complete", job.State);
            Assert.NotNull(job.PurgeRunIdsJson);

            var completionEmails = fixture.Emails.Sent.Count(m => m.TemplateKey == "email.deletion_completed" && m.To.StartsWith("conc2a-", StringComparison.Ordinal));
            Assert.Equal(1, completionEmails); // NOT two — double-execution would have sent it twice.
        }
        finally
        {
            await config.SetValue(IdentityConfigKeys.DeletionGraceDays, originalGraceDays, "restore", SystemActorFor(), RequestContext.System(SystemActorFor(), "conc2a-restore"));
        }
    }

    // ------------------------------------------------------------------------------------------------
    // CONC-2(b): a job left 'executing' past the lease window (simulating a crashed prior attempt that
    // never got to revert itself) must be picked up and completed by the NEXT sweep — never a dead state.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task StaleExecutingJob_PastTheLeaseWindow_IsPickedUpAndCompleted()
    {
        using var seedScope = fixture.NewScope();
        var db = seedScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var fieldEncryptor = seedScope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();

        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId, Handle = $"conc2b_{Guid.NewGuid():N}"[..20],
            Email = $"conc2b-{Guid.NewGuid():N}@example.test", EmailVerifiedAt = now,
            BirthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "1977-11-11", CancellationToken.None),
            AttestedAdultAt = now, TermsVersion = "v1", FandomTag = "fixture", Locale = "en",
            AccountState = "deleted", StateChangedAt = now, DeletionRequestedAt = now,
            DeletionEffectiveAt = now.AddDays(-1), // due.
            CreatedAt = now, LastActiveAt = now, Region = "US", RegionSource = "Signup", LawfulBasis = "conservative_global_v0",
        });
        var deletionId = OpaqueId.New(IdPrefixes.Deletion, now, Random.Shared).ToString();
        db.DeletionJobs.Add(new DeletionJobEntity
        {
            // A job stuck 'executing' since far in the past — a crashed prior attempt. The lease window
            // is sized off identity.export.pre_deletion_wait_hours (72h default) + a 1h buffer — 400
            // hours safely exceeds that regardless of config, without this test needing to know or
            // override the exact configured value.
            DeletionId = deletionId, AccountId = accountId, State = "executing",
            ExecutingSince = now.AddHours(-400),
            RequestedAt = now.AddHours(-400), ScheduledFor = now.AddHours(-400),
            ExportOffered = true, Region = "US", LawfulBasis = "conservative_global_v0",
        });
        await db.SaveChangesAsync();

        using var workerScope = fixture.NewScope();
        var worker = workerScope.ServiceProvider.GetRequiredService<DeletionPhysicalPurgeWorker>();
        var processed = await worker.RunDueSweepAsync(CancellationToken.None);
        Assert.True(processed >= 1);

        using var assertScope = fixture.NewScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var account = await assertDb.Accounts.SingleAsync(a => a.AccountId == accountId);
        Assert.NotNull(account.TombstonedAt); // the stale job WAS completed, not left dangling.
    }

    // ------------------------------------------------------------------------------------------------
    // PII-9: after account_deletion's own purge run hard-deletes events_behavioral for the raw
    // account_id (the registered verb), the worker's own post-purge completion event must NOT re-create a
    // row bearing that same raw account_id — it must be keyed on an anonymous identifier instead.
    // ------------------------------------------------------------------------------------------------
    [Fact]
    public async Task PostPurgeCompletionEvent_NeverReCreatesARawAccountIdBearingBehavioralRow()
    {
        using var scope = fixture.NewScope();
        var config = scope.ServiceProvider.GetRequiredService<Svac.DomainCore.Contracts.Config.IConfigRegistry>();
        var originalGraceDays = await config.GetValue<int>(IdentityConfigKeys.DeletionGraceDays, CancellationToken.None);
        await config.SetValue(IdentityConfigKeys.DeletionGraceDays, 0, "test override", SystemActorFor(), RequestContext.System(SystemActorFor(), "pii9-grace"));
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var fieldEncryptor = scope.ServiceProvider.GetRequiredService<IFieldEncryptor>();
            var now = DateTimeOffset.UtcNow;
            var accountId = OpaqueId.New(IdPrefixes.User, now, Random.Shared).ToString();
            db.Accounts.Add(new AccountEntity
            {
                AccountId = accountId, Handle = $"pii9_{Guid.NewGuid():N}"[..20],
                Email = $"pii9-{Guid.NewGuid():N}@example.test", EmailVerifiedAt = now,
                BirthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "2001-09-09", CancellationToken.None),
                AttestedAdultAt = now, TermsVersion = "v1", FandomTag = "fixture", Locale = "en",
                AccountState = "active", StateChangedAt = now, CreatedAt = now, LastActiveAt = now,
                Region = "US", RegionSource = "Signup", LawfulBasis = "conservative_global_v0",
            });
            await db.SaveChangesAsync();

            var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycle>();
            await lifecycle.RequestDeletion(OpaqueId.Parse(accountId), UserCtx(accountId, "pii9-request"), CancellationToken.None);

            var worker = scope.ServiceProvider.GetRequiredService<DeletionPhysicalPurgeWorker>();
            await worker.RunDueSweepAsync(CancellationToken.None);

            var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
            var behavioralForAccount = new List<string>();
            await foreach (var e in eventStore.ReadStream(StreamType.Behavioral, accountId))
            {
                behavioralForAccount.Add(e.EventType);
            }
            Assert.Empty(behavioralForAccount); // the pipeline's own Delete verb ran, AND nothing re-created a row here.
        }
        finally
        {
            await config.SetValue(IdentityConfigKeys.DeletionGraceDays, originalGraceDays, "restore", SystemActorFor(), RequestContext.System(SystemActorFor(), "pii9-restore"));
        }
    }

    private static ActorRef SystemActorFor() => new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);

    /// <summary>Parks HoldsFor (called once RunDueSweepAsync's CAS has already claimed the job) until released — the SAME synchronization idiom DeletionPipelineTests' FixtureCustodyHoldRegistry uses, extended with a gate.</summary>
    private sealed class GatedCustodyHoldRegistry : ICustodyHoldRegistry
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<CustodyHoldScope>> HoldsFor(string accountId, CancellationToken ct = default)
        {
            Entered.SetResult();
            await Release.Task;
            return Array.Empty<CustodyHoldScope>();
        }
    }
}
