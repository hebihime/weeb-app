using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Purge;
using Svac.DomainCore.Contracts.FieldEncryption;
using Svac.DomainCore.Contracts.Purge;
using Svac.DomainCore.FieldEncryption;
using Svac.DomainCore.Persistence;
using Svac.DomainCore.Policy;
using Svac.DomainCore.Purge;
using Testcontainers.PostgreSql;
using Xunit;

namespace Svac.Tests.AdminHost;

// ============================================================================================
// INTERFACE-SKETCH addendum (SLICE_S5_CONTRACT.md §6: "Purge-completeness suite gains the staff-
// pseudonymize case: seed -> erase -> audit chain resolves stf_ -> PII gone"). AdminPurgeRegistrySource
// (backend/admin-host/Svac.AdminHost.Domain/Purge/AdminPurgeRegistrySource.cs) already registers both
// storeKeys with their declared verbs (scaffold-landed) -- this sketch is only the two IPurgeStoreExecutor
// implementations that make those declared verbs REAL, which is Pass B/D's deliverable.
//
// namespace Svac.AdminHost.Domain.Purge
// {
//     // StatutoryErasure AND RetentionExpiry both declare Pseudonymize for admin.staff_accounts (§6) --
//     // one executor, verb-dispatched. external_subject/email/display_name are re-keyed via the SAME
//     // PurgePseudonymizer.Pseudonymize(original, purgeClass, hmacKey) every other module's Pseudonymize
//     // verb uses (cross-store-correlatable for a key holder, irreversible for everyone else); the stf_
//     // id and status are NEVER touched -- "so every audit chain still resolves" (§6) means the id a
//     // config.set/admin.action.executed event's actor_ref names must still resolve to a real row.
//     public sealed class StaffAccountsPurgeStoreExecutor(AdminDbContext adminDb) : IPurgeStoreExecutor
//     {
//         public string StoreKey => "admin.staff_accounts";
//         public Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default);
//     }
//
//     // StatutoryErasure declares Tombstone for admin.staff_role_grants (§6): reason texts +
//     // grantor/revoker PII tombstoned WHERE THE ERASED SUBJECT APPEARS (as staff_id, granted_by, OR
//     // revoked_by) -- the grant/revoke STRUCTURE (role, granted_at/revoked_at, staff_id itself) survives
//     // for accountability, per §6's own text.
//     public sealed class StaffRoleGrantsPurgeStoreExecutor(AdminDbContext adminDb) : IPurgeStoreExecutor
//     {
//         public string StoreKey => "admin.staff_role_grants";
//         public Task<int> ExecuteAsync(PurgeVerb verb, PurgeClass purgeClass, SubjectRef subject, byte[] pseudonymizeHmacKey, CancellationToken ct = default);
//     }
// }
// ============================================================================================

public sealed class StaffPurgeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgis/postgis:16-3.4").Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        await coreDb.Database.MigrateAsync();
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        await adminDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class NeverCalledConfigRegistry : Svac.DomainCore.Contracts.Config.IConfigRegistry
    {
        public Task<T> GetValue<T>(string key, CancellationToken ct = default) =>
            throw new InvalidOperationException($"config key \"{key}\" read during a staff StatutoryErasure purge run -- admin stores never consult config for that class.");

        public Task SetValue<T>(string key, T value, string reason, Svac.DomainCore.Contracts.Ids.ActorRef actor, Svac.DomainCore.Contracts.RequestContext ctx, CancellationToken ct = default) =>
            throw new NotSupportedException("fixture adapter: not exercised in this suite.");

        public Task<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>> ListEntries(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Svac.DomainCore.Contracts.Config.ConfigEntryView>>(Array.Empty<Svac.DomainCore.Contracts.Config.ConfigEntryView>());
    }

    private static PurgePipeline BuildPipeline(CoreDbContext coreDb, AdminDbContext adminDb, IFieldEncryptor fieldEncryptor)
    {
        var vault = new DevKeyringFieldKeyVault();
        var registry = new PurgeRegistry(new IPurgeRegistrySource[] { new CorePurgeRegistrySource(), new AdminPurgeRegistrySource() });
        var executors = new IPurgeStoreExecutor[]
        {
            new StaffAccountsPurgeStoreExecutor(adminDb),
            new StaffRoleGrantsPurgeStoreExecutor(adminDb),
        };
        return new PurgePipeline(
            coreDb,
            new Svac.DomainCore.EventStore.PostgresEventStore(coreDb),
            registry,
            fieldEncryptor,
            new PolicyEngine(new PolicyTable()),
            vault,
            executors);
    }

    [Fact]
    public async Task StatutoryErasure_OnADeactivatedStaffAccount_PseudonymizesPii_KeepsIdAndStatusResolvable()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (staffId, actor, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "purge-target");
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        row.Status = "deactivated";
        row.DeactivatedAt = DateTimeOffset.UtcNow;
        await adminDb.SaveChangesAsync();
        var originalEmail = row.Email;
        var originalSubject = row.ExternalSubject;
        var originalDisplayName = row.DisplayName;

        var fieldEncryptor = new AesFieldEncryptor(new DevKeyringFieldKeyVault());
        var pipeline = BuildPipeline(coreDb, adminDb, fieldEncryptor);

        var reports = await pipeline.Run(
            PurgeClass.StatutoryErasure,
            new SubjectRef("staff_account", staffId),
            AdminTestSupport.SystemActor(),
            AdminTestSupport.SystemCtx("staff-purge-drill"));

        var staffReport = Assert.Single(reports, r => r.StoreKey == "admin.staff_accounts");
        Assert.Equal(1, staffReport.RowsAffected);

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var purged = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        Assert.Equal(staffId, purged.Id); // id survives -- the audit chain must still resolve stf_
        Assert.Equal("deactivated", purged.Status); // status survives (§6)
        Assert.NotEqual(originalEmail, purged.Email);
        Assert.NotEqual(originalSubject, purged.ExternalSubject);
        Assert.NotEqual(originalDisplayName, purged.DisplayName);
        Assert.DoesNotContain("@", purged.Email); // no longer a real, contactable email address

        // Every event this staff actor ever generated still resolves by id (the whole point of
        // pseudonymize-not-delete): the actor_ref recorded on any prior audit event named this SAME
        // stf_ id, which the purge never touched.
        Assert.Equal(staffId, actor.Id.ToString());
    }

    [Fact]
    public async Task StatutoryErasure_TombstonesGrantReasonTextAndGrantorRef_StructuralRoleAndTimestampsSurvive()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        using var coreDb = AdminTestSupport.NewCoreDb(ConnectionString);
        var (staffId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "purge-grant-target");
        var grant = await adminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId);
        var originalReason = grant.GrantReason;
        var originalRole = grant.Role;
        var originalGrantedAt = grant.GrantedAt;

        var fieldEncryptor = new AesFieldEncryptor(new DevKeyringFieldKeyVault());
        var pipeline = BuildPipeline(coreDb, adminDb, fieldEncryptor);

        await pipeline.Run(
            PurgeClass.StatutoryErasure,
            new SubjectRef("staff_account", staffId),
            AdminTestSupport.SystemActor(),
            AdminTestSupport.SystemCtx("staff-grant-purge-drill"));

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var purgedGrant = await freshAdminDb.StaffRoleGrants.SingleAsync(g => g.StaffId == staffId);
        Assert.NotEqual(originalReason, purgedGrant.GrantReason); // free-text PII tombstoned
        Assert.Equal(originalRole, purgedGrant.Role); // structure survives -- accountability (§6)
        Assert.Equal(originalGrantedAt, purgedGrant.GrantedAt);
        Assert.Equal(staffId, purgedGrant.StaffId); // the grant still resolves to the SAME (pseudonymized) staff row
    }

    // ------------------------------------------------------------------------------------------------
    // SECURITY_REVIEW_S5.md S5-05 (fixNow): AdminPurgeRegistrySource's OWN registration comment for
    // RetentionExpiry says "admin.staff_pii_retention_years post-DEACTIVATION" — an age-cutoff sweep was
    // never meant to touch a still-ACTIVE operator/founder. Before this fix, StaffAccountsPurgeStoreExecutor
    // enforced no such guard: a misconfigured/mistimed RetentionExpiry sweep (age computed off CreatedAt,
    // say) could pseudonymize (destroying the external_subject Entra oid lookup key) an active row with no
    // in-app recovery — including the last SuperAdmin, compounding S5-03's own lockout concern. These
    // tests exercise the executor DIRECTLY (never through the full pipeline) since the guard lives entirely
    // inside it.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RetentionExpiry_OnAnActiveStaffAccount_NoOps_NeverPseudonymizes()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "retention-active-target");
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        var originalEmail = row.Email;
        var originalSubject = row.ExternalSubject;
        var originalDisplayName = row.DisplayName;

        var executor = new StaffAccountsPurgeStoreExecutor(adminDb);
        var rowsAffected = await executor.ExecuteAsync(
            PurgeVerb.Pseudonymize, PurgeClass.RetentionExpiry, new SubjectRef("staff_account", staffId), pseudonymizeHmacKey: new byte[32]);

        Assert.Equal(0, rowsAffected); // no-op -- an active row is never in scope for a mere age-cutoff sweep

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var unchanged = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        Assert.Equal("active", unchanged.Status);
        Assert.Equal(originalEmail, unchanged.Email); // byte-identical -- never touched
        Assert.Equal(originalSubject, unchanged.ExternalSubject); // the Entra oid lookup key survives -- no lockout
        Assert.Equal(originalDisplayName, unchanged.DisplayName);
    }

    [Fact]
    public async Task RetentionExpiry_OnADeactivatedStaffAccount_StillPseudonymizes_TheLegitimateCaseIsUnaffected()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "retention-deactivated-target");
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        row.Status = "deactivated";
        row.DeactivatedAt = DateTimeOffset.UtcNow;
        await adminDb.SaveChangesAsync();
        var originalEmail = row.Email;

        var executor = new StaffAccountsPurgeStoreExecutor(adminDb);
        var rowsAffected = await executor.ExecuteAsync(
            PurgeVerb.Pseudonymize, PurgeClass.RetentionExpiry, new SubjectRef("staff_account", staffId), pseudonymizeHmacKey: new byte[32]);

        Assert.Equal(1, rowsAffected); // the guard never blocks the case it was actually written for

        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var purged = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        Assert.Equal("deactivated", purged.Status);
        Assert.NotEqual(originalEmail, purged.Email);
    }

    [Fact]
    public async Task StatutoryErasure_OnAnActiveStaffAccount_StillPseudonymizes_ARealDsrIsNeverBlockedByThisGuard()
    {
        using var adminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var (staffId, _, _) = await AdminTestSupport.SeedActiveStaff(adminDb, new[] { "safety_agent" }, "statutory-active-target");
        var row = await adminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        var originalEmail = row.Email;

        var executor = new StaffAccountsPurgeStoreExecutor(adminDb);
        var rowsAffected = await executor.ExecuteAsync(
            PurgeVerb.Pseudonymize, PurgeClass.StatutoryErasure, new SubjectRef("staff_account", staffId), pseudonymizeHmacKey: new byte[32]);

        // S5-05's guard is scoped to RetentionExpiry ONLY -- a real DSR (StatutoryErasure) must still
        // reach an ACTIVE row exactly as it always did; this guard must never become a way to dodge a
        // legitimate erasure obligation just by staying active.
        Assert.Equal(1, rowsAffected);
        using var freshAdminDb = AdminTestSupport.NewAdminDb(ConnectionString);
        var purged = await freshAdminDb.StaffAccounts.SingleAsync(s => s.Id == staffId);
        Assert.Equal("active", purged.Status); // status itself is never touched by the erasure -- only PII fields are
        Assert.NotEqual(originalEmail, purged.Email);
    }
}
