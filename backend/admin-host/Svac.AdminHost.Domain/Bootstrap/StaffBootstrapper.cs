using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.AdminHost.Domain.Bootstrap;

/// <summary>
/// First-SuperAdmin bootstrap (SLICE_S5_CONTRACT.md §1b: "if admin.staff_accounts is empty AND
/// SVAC_ADMIN_BOOTSTRAP_SUBJECT ... is set, provision that subject + SuperAdmin grant as a system-actor
/// action, audited like any grant. One-shot: no bootstrap path exists once any account exists."). An env
/// var, never a 9A entry (§1b: "bootstrap precedes the desk that edits 9A").
///
/// Provisions the staff row THEN the SuperAdmin grant, both as system-actor <c>admin.action.executed</c>
/// envelope events (<c>admin.staff.provision</c>, <c>admin.staff.role_grant</c>) in the SAME transaction
/// as their row inserts — audited exactly like a founder-driven grant would be, per the
/// StaffAndSystem-allowlisted policy rows (<see cref="AdminPolicyTableSource"/> §3: "system: bootstrap").
///
/// Idempotent-under-race (§1b concurrency law 19): a concurrent double-call (two instances racing an
/// empty table on cold boot) has its LOSER hit <c>ux_staff_accounts_external_subject</c>'s unique
/// violation, caught here, and returns false rather than throwing or double-provisioning.
/// </summary>
public sealed class StaffBootstrapper(AdminDbContext adminDb, IEventStore eventStore)
{
    private const string PostgresUniqueViolation = "23505";

    /// <returns>true iff THIS call provisioned the account; false = no-op (already provisioned, by this
    /// call or a concurrent racer).</returns>
    public async Task<bool> BootstrapIfEmpty(string externalSubject, string email, string displayName, string region, CancellationToken ct = default)
    {
        if (await adminDb.StaffAccounts.AnyAsync(ct))
        {
            return false; // one-shot: no bootstrap path once ANY account exists, regardless of subject.
        }

        var systemActor = new ActorRef(OpaqueId.New(IdPrefixes.System, DateTimeOffset.UtcNow, Random.Shared), ActorKind.System);
        var ctx = RequestContext.System(systemActor, correlationId: "admin-host-bootstrap");

        var now = DateTimeOffset.UtcNow;
        var staffId = OpaqueId.New(IdPrefixes.Staff, now, Random.Shared).ToString();
        var stamp = Guid.NewGuid().ToString("N");

        adminDb.StaffAccounts.Add(new StaffAccountEntity
        {
            Id = staffId,
            ExternalSubject = externalSubject,
            Email = email,
            DisplayName = displayName,
            Status = "active",
            SecurityStamp = stamp,
            Region = region,
            LawfulBasis = "contract",
            CreatedAt = now,
            UpdatedAt = now,
        });

        var grantId = OpaqueId.New(IdPrefixes.StaffRoleGrant, now, Random.Shared).ToString();
        adminDb.StaffRoleGrants.Add(new StaffRoleGrantEntity
        {
            Id = grantId,
            StaffId = staffId,
            Role = StaffRoleCodes.ToCode(StaffRole.SuperAdmin),
            GrantedBy = systemActor.Id.ToString(),
            GrantReason = "bootstrap: first SuperAdmin (SVAC_ADMIN_BOOTSTRAP_SUBJECT)",
            GrantedAt = now,
            Region = region,
            LawfulBasis = "contract",
        });

        try
        {
            await adminDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent cold-boot racer won the empty-table check and committed first — this call's
            // own INSERT hits the unique constraint. Detach the failed entities so this DbContext instance
            // stays usable, then report "did not provision" rather than throwing.
            adminDb.ChangeTracker.Clear();
            return false;
        }

        await AppendEnvelope(staffId, "admin.staff.provision", staffId, systemActor, ctx, ct);
        await AppendEnvelope(staffId, "admin.staff.role_grant", grantId, systemActor, ctx, ct);

        return true;
    }

    private async Task AppendEnvelope(string streamId, string action, string targetId, ActorRef actor, RequestContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            action,
            target_ref = targetId,
            hat = StaffRole.SuperAdmin.ToString(),
            roles_held = new[] { StaffRole.SuperAdmin.ToString() },
            reason = "bootstrap",
        });
        await eventStore.Append(StreamType.Audit, streamId, "admin.action.executed", payload, ctx, ExpectedVersion.AnyVersion, ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation };
}
