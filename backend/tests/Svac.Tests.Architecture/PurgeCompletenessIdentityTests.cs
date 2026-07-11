using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Purge;
using Svac.Identity.Persistence;
using Svac.Identity.Purge;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.Architecture;

/// <summary>
/// The identity sibling of <see cref="PurgeCompletenessTests"/> (SLICE_S3_CONTRACT.md §6a/§10.3): "seed a
/// subject across identity stores -&gt; run purge -&gt; assert the registered posture for every store,
/// INCLUDING the S2-scar tests: every subject-bearing row keyed by something OTHER than account_id
/// (email_challenges keyed by email, deletion_jobs) gets an explicit purge-reaches-it test." Seeds every
/// identity table through real EF writes shaped exactly like the production write paths
/// (SignupCompletionService/ExportEndpoints/DeletionEndpoints) — never a raw SQL INSERT — then runs the
/// SAME <see cref="IPurgePipeline.Run"/> the deletion worker calls, composed with every real
/// <see cref="IPurgeStoreExecutor"/> exactly as <c>AddIdentityModule</c> registers them.
/// </summary>
public sealed class PurgeCompletenessIdentityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:16-3.4")
        .Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var coreDb = NewCoreDb();
        await coreDb.Database.MigrateAsync();
        using var identityDb = NewIdentityDb();
        await identityDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewCoreDb() =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseNpgsql(ConnectionString).Options);

    private IdentityDbContext NewIdentityDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(ConnectionString).Options);

    private static readonly ActorRef SystemActor = new(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
    private static RequestContext SystemCtx(string correlationId) => RequestContext.System(SystemActor, correlationId);

    private static string FreshAccountId() => OpaqueId.New(IdPrefixes.User, DateTimeOffset.UtcNow, Random.Shared).ToString();

    private static PurgePipeline BuildPipeline(CoreDbContext coreDb, IdentityDbContext identityDb, IFieldEncryptor fieldEncryptor)
    {
        var vault = new DevKeyringFieldKeyVault();
        var config = new NeverCalledConfigRegistry();
        var executors = new IPurgeStoreExecutor[]
        {
            new EmailChallengesPurgeStoreExecutor(identityDb, config),
            new AccountsPurgeStoreExecutor(identityDb),
            new SessionsPurgeStoreExecutor(identityDb),
            new RefreshTokensPurgeStoreExecutor(identityDb),
            new DevicesPurgeStoreExecutor(identityDb),
            new PushCategoryConsentsPurgeStoreExecutor(identityDb),
            new ConsentCurrentPurgeStoreExecutor(identityDb),
            new HandleHistoryPurgeStoreExecutor(identityDb, config),
            new ExportJobsPurgeStoreExecutor(identityDb),
            new DeletionJobsPurgeStoreExecutor(identityDb),
        };
        var registry = new PurgeRegistry(new IPurgeRegistrySource[] { new CorePurgeRegistrySource(), new IdentityPurgeRegistrySource() });
        return new PurgePipeline(coreDb, new Svac.DomainCore.EventStore.PostgresEventStore(coreDb), registry, fieldEncryptor, new PolicyEngine(new PolicyTable()), vault, executors);
    }

    /// <summary>Seeds one account through EF writes shaped like the real production write paths across every identity store, including the TWO S2-scar cases: a signup-purpose email_challenge (no account_id, matched by email) and a login-purpose one (has account_id).</summary>
    private static async Task<SeedResult> SeedAccountAcrossIdentityStores(IdentityDbContext db, AesFieldEncryptor fieldEncryptor, string accountId, string emailLower)
    {
        var now = DateTimeOffset.UtcNow;
        var originalHandle = $"seed_{Guid.NewGuid():N}"[..20];

        var birthdateEnc = await fieldEncryptor.Protect(FieldEncryptionPurpose.Birthdate, new SubjectScope(accountId), "2000-01-01", CancellationToken.None);

        db.Accounts.Add(new AccountEntity
        {
            AccountId = accountId,
            Handle = originalHandle,
            Email = emailLower,
            EmailVerifiedAt = now,
            BirthdateEnc = birthdateEnc,
            AttestedAdultAt = now,
            TermsVersion = "v1",
            FandomTag = "fixture-tag",
            AvatarRef = null,
            Locale = "en",
            AccountState = "deleted", // Phase-L already ran by the time Phase P's purge call reaches here.
            IrlAccessState = "active",
            StateChangedAt = now,
            DeletionRequestedAt = now,
            DeletionEffectiveAt = now,
            CreatedAt = now,
            LastActiveAt = now,
            Region = "US",
            RegionSource = "Signup",
            LawfulBasis = "conservative_global_v0",
        });

        // S2-scar #1: a SIGNUP-purpose challenge, no account_id (§1g design), matched by email only.
        var signupChallengeId = OpaqueId.New(IdPrefixes.Challenge, now, Random.Shared).ToString();
        db.EmailChallenges.Add(new EmailChallengeEntity
        {
            ChallengeId = signupChallengeId, Purpose = "signup", EmailLower = emailLower, AccountId = null,
            CodeHash = new byte[32], Attempts = 1, VerifiedAt = now, VerifiedTokenHash = new byte[32], ConsumedAt = now,
            ExpiresAt = now.AddMinutes(30), CreatedAt = now, Locale = "en", Region = "US", LawfulBasis = "conservative_global_v0",
        });
        // The "easy" case: a LOGIN-purpose challenge that DOES carry account_id.
        var loginChallengeId = OpaqueId.New(IdPrefixes.Challenge, now, Random.Shared).ToString();
        db.EmailChallenges.Add(new EmailChallengeEntity
        {
            ChallengeId = loginChallengeId, Purpose = "login", EmailLower = emailLower, AccountId = accountId,
            CodeHash = new byte[32], Attempts = 0, ExpiresAt = now.AddMinutes(15), CreatedAt = now, Locale = "en",
            Region = "US", LawfulBasis = "conservative_global_v0",
        });

        var sessionId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString();
        var familyId = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString();
        db.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId, AccountId = accountId, DeviceId = null, AccessTokenHash = new byte[32],
            RefreshFamilyId = familyId, CreatedAt = now, LastSeenAt = now, AccessExpiresAt = now.AddHours(1),
            Region = "US", LawfulBasis = "conservative_global_v0",
        });
        // S2-scar #2: refresh_tokens is keyed by session_id, never account_id.
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString(), SessionId = sessionId, TokenHash = new byte[32],
            FamilyId = familyId, IssuedAt = now, ExpiresAt = now.AddDays(90), Region = "US", LawfulBasis = "conservative_global_v0",
        });

        var deviceId = OpaqueId.New(IdPrefixes.Device, now, Random.Shared).ToString();
        db.Devices.Add(new DeviceEntity
        {
            DeviceId = deviceId, AccountId = accountId, Platform = "ios", PushToken = "fixture-push-token",
            PushTokenUpdatedAt = now, CreatedAt = now, LastSeenAt = now, Region = "US", LawfulBasis = "conservative_global_v0",
        });

        db.PushCategoryConsents.Add(new PushCategoryConsentEntity
        {
            AccountId = accountId, Category = 1, Enabled = true, UpdatedAt = now, Region = "US", LawfulBasis = "conservative_global_v0",
        });
        db.ConsentCurrent.Add(new ConsentCurrentEntity
        {
            AccountId = accountId, ConsentKind = "AgeAttestation18Plus", Version = "v1", Status = "granted",
            Surface = "signup", DecidedAt = now, Region = "US", LawfulBasis = "conservative_global_v0",
        });
        db.HandleHistory.Add(new HandleHistoryEntity
        {
            Id = OpaqueId.New(IdPrefixes.Session, now, Random.Shared).ToString(), AccountId = accountId,
            OldHandle = "old_" + originalHandle, NewHandle = originalHandle, ChangedAt = now, Region = "US", LawfulBasis = "conservative_global_v0",
        });

        var exportId = OpaqueId.New(IdPrefixes.Export, now, Random.Shared).ToString();
        db.ExportJobs.Add(new ExportJobEntity
        {
            ExportId = exportId, AccountId = accountId, State = "ready", Artifact = new byte[] { 1, 2, 3 },
            ManifestJson = "{}", RequestedAt = now, ReadyAt = now, ExpiresAt = now.AddDays(3), Region = "US", LawfulBasis = "conservative_global_v0",
        });

        var deletionId = OpaqueId.New(IdPrefixes.Deletion, now, Random.Shared).ToString();
        db.DeletionJobs.Add(new DeletionJobEntity
        {
            DeletionId = deletionId, AccountId = accountId, State = "executing", RequestedAt = now, ScheduledFor = now,
            ExportOffered = true, Region = "US", LawfulBasis = "conservative_global_v0",
        });

        await db.SaveChangesAsync(CancellationToken.None);
        return new SeedResult(originalHandle, signupChallengeId, loginChallengeId, sessionId, exportId, deletionId, birthdateEnc);
    }

    private sealed record SeedResult(string OriginalHandle, string SignupChallengeId, string LoginChallengeId, string SessionId, string ExportId, string DeletionId, byte[] BirthdateEnc);

    [Fact]
    public async Task AccountDeletion_AcrossEveryIdentityStore_MatchesTheRegisteredPosture_IncludingTheS2ScarStores()
    {
        var accountId = FreshAccountId();
        var emailLower = $"purge-fixture-{Guid.NewGuid():N}@example.test";

        SeedResult seed;
        using (var seedDb = NewIdentityDb())
        {
            var vault = new DevKeyringFieldKeyVault();
            seed = await SeedAccountAcrossIdentityStores(seedDb, new AesFieldEncryptor(vault), accountId, emailLower);
        }

        using var coreDb = NewCoreDb();
        using var identityDb = NewIdentityDb();
        var fieldEncryptor = new AesFieldEncryptor(new DevKeyringFieldKeyVault());
        var pipeline = BuildPipeline(coreDb, identityDb, fieldEncryptor);

        var reports = await pipeline.Run(PurgeClass.AccountDeletion, new SubjectRef("account", accountId), SystemActor, SystemCtx("identity-purge-completeness"));

        var registry = new PurgeRegistry(new IPurgeRegistrySource[] { new CorePurgeRegistrySource(), new IdentityPurgeRegistrySource() });
        var expectedIdentityStores = registry.Entries
            .Where(e => e.PurgeClass == PurgeClass.AccountDeletion && e.StoreKey.StartsWith("identity.", StringComparison.Ordinal))
            .Select(e => e.StoreKey)
            .ToHashSet();
        var reportedIdentityStores = reports.Select(r => r.StoreKey).Where(k => k.StartsWith("identity.", StringComparison.Ordinal)).ToHashSet();
        Assert.Equal(expectedIdentityStores, reportedIdentityStores);

        using var assertDb = NewIdentityDb();
        var violations = new List<string>();

        // identity.accounts: Tombstone — row survives, PII nulled/sentineled, handle moved to retired_handles.
        var account = await assertDb.Accounts.SingleOrDefaultAsync(a => a.AccountId == accountId);
        if (account is null)
        {
            violations.Add("identity.accounts: row was deleted outright — registry declares Tombstone.");
        }
        else
        {
            if (account.Email is not null) violations.Add("identity.accounts: email was not nulled.");
            if (account.FandomTag != string.Empty) violations.Add("identity.accounts: fandom_tag was not cleared.");
            if (account.Handle == seed.OriginalHandle) violations.Add("identity.accounts: handle column still carries the original human-chosen value.");
            if (account.TombstonedAt is null) violations.Add("identity.accounts: tombstoned_at was never set.");
        }
        if (!await assertDb.RetiredHandles.AnyAsync(h => h.Handle == seed.OriginalHandle))
        {
            violations.Add("identity.retired_handles: the original handle was never moved into quarantine.");
        }

        // S2 scar #1: the signup-purpose challenge (no account_id) must be gone via email match.
        if (await assertDb.EmailChallenges.AnyAsync(c => c.ChallengeId == seed.SignupChallengeId))
        {
            violations.Add("identity.email_challenges: the SIGNUP-purpose (account_id-less, email-keyed) row survived the purge — the S2 scar.");
        }
        if (await assertDb.EmailChallenges.AnyAsync(c => c.ChallengeId == seed.LoginChallengeId))
        {
            violations.Add("identity.email_challenges: the LOGIN-purpose (account_id-keyed) row survived the purge.");
        }

        if (await assertDb.Sessions.AnyAsync(s => s.SessionId == seed.SessionId))
        {
            violations.Add("identity.sessions: row survived the purge.");
        }
        // S2 scar #2: refresh_tokens, keyed by session_id.
        if (await assertDb.RefreshTokens.AnyAsync(r => r.SessionId == seed.SessionId))
        {
            violations.Add("identity.refresh_tokens: row survived the purge (keyed by session_id, not account_id — the S2-scar analogue).");
        }

        if (await assertDb.Devices.AnyAsync(d => d.AccountId == accountId))
        {
            violations.Add("identity.devices: row survived the purge.");
        }
        if (await assertDb.PushCategoryConsents.AnyAsync(p => p.AccountId == accountId))
        {
            violations.Add("identity.push_category_consents: row survived the purge.");
        }
        if (await assertDb.ConsentCurrent.AnyAsync(c => c.AccountId == accountId))
        {
            violations.Add("identity.consent_current: row survived the purge.");
        }

        // identity.handle_history: Pseudonymize — row SURVIVES, account_id re-keyed away from the original.
        var handleHistoryRows = await assertDb.HandleHistory.Where(h => h.AccountId == accountId).ToListAsync();
        if (handleHistoryRows.Count != 0)
        {
            violations.Add("identity.handle_history: the account_id column was never re-keyed (still matches the original) — registry declares Pseudonymize, not Delete.");
        }

        if (await assertDb.ExportJobs.AnyAsync(e => e.ExportId == seed.ExportId))
        {
            violations.Add("identity.export_jobs: row (incl. artifact) survived the purge.");
        }

        // identity.deletion_jobs: Pseudonymize — the RECEIPT SURVIVES by deletion_id (PK, unaffected), account_id re-keyed.
        var deletionJob = await assertDb.DeletionJobs.SingleOrDefaultAsync(d => d.DeletionId == seed.DeletionId);
        if (deletionJob is null)
        {
            violations.Add("identity.deletion_jobs: the receipt itself was deleted — registry declares Pseudonymize (the receipt survives, S1 purge_runs precedent).");
        }
        else if (deletionJob.AccountId == accountId)
        {
            violations.Add("identity.deletion_jobs: account_id was never re-keyed away from the original.");
        }

        // Birthdate crypto-shred: automatic via the ALREADY-registered global field_key_refs/
        // data_protection_keys CryptoShred cells (no identity-specific registration needed).
        await Assert.ThrowsAnyAsync<Exception>(() => fieldEncryptor.Unprotect(FieldEncryptionPurpose.Birthdate, seed.BirthdateEnc, CancellationToken.None));

        Assert.Empty(violations);
    }

    /// <summary>Fixture config registry that fails loudly if ever called — the AccountDeletion path must never read config (only RetentionExpiry-class cells do), proving the retention-expiry age-gates inside EmailChallengesPurgeStoreExecutor/HandleHistoryPurgeStoreExecutor are correctly class-scoped.</summary>
    private sealed class NeverCalledConfigRegistry : Svac.DomainCore.Contracts.Config.IConfigRegistry
    {
        public Task<T> GetValue<T>(string key, CancellationToken ct = default) =>
            throw new InvalidOperationException($"config key \"{key}\" was read during an AccountDeletion-class purge run — only RetentionExpiry-class cells should ever consult config.");

        public Task SetValue<T>(string key, T value, string reason, ActorRef actor, RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("fixture adapter: not exercised in this suite.");

        public Task<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>> ListEntries(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>>(Array.Empty<Svac.DomainCore.Contracts.Config.ConfigEntryView>());
    }
}
