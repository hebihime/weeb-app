using Microsoft.EntityFrameworkCore;
using Npgsql;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.AdminHost.Domain.Auth;

/// <summary>
/// The dev-only staff-auth transport (SLICE_S5_CONTRACT.md §1b: "<c>DevSeamsStaffTransport</c>
/// ([DevSeamsOnly], arch-tested never-in-prod-DI): a dev sign-in page issuing deterministic fixture
/// principals with the SAME claim shape Entra emits"). NEVER a 9A entry (§1b/§12.16 — DevSeams is an
/// environment flag, structurally never a runtime-tunable).
///
/// Idempotently provisions the underlying <c>admin.staff_accounts</c>/<c>admin.staff_role_grants</c> rows
/// for a fixture on first use (dev/compose convenience — no separate seed step) EXCEPT the
/// <c>no_staff_row</c> fixture, which deliberately never provisions anything so
/// <c>StaffSignInPipeline.SignIn</c> genuinely returns <c>RefusedUnknownSubject</c>. This provisioning is
/// NOT an audited staff action (it never flows through <c>AdminActionExecutor</c>) — it is dev fixture
/// bootstrapping, the exact same class of direct-EF setup <c>AdminTestSupport.SeedActiveStaff</c> already
/// does in the deterministic test suite, just reachable from a real HTTP sign-in click instead of a unit
/// test's arrange step.
/// </summary>
[Svac.DomainCore.Contracts.DevSeamsOnly]
public sealed class DevSeamsStaffTransport(AdminDbContext adminDb)
{
    private const string PostgresUniqueViolation = "23505";

    public async Task<StaffExternalClaims?> ResolveClaims(string fixtureKey, CancellationToken ct = default)
    {
        var fixture = DevSeamsStaffFixtures.Find(fixtureKey);
        if (fixture is null)
        {
            return null;
        }

        if (fixture.ProvisionRow)
        {
            await EnsureProvisioned(fixture, ct);
        }

        return new StaffExternalClaims(fixture.ExternalSubject, fixture.HasMfaClaim, fixture.Email, fixture.DisplayName);
    }

    private async Task EnsureProvisioned(DevSeamsStaffFixture fixture, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await adminDb.StaffAccounts.SingleOrDefaultAsync(s => s.ExternalSubject == fixture.ExternalSubject, ct);

        string staffId;
        if (existing is null)
        {
            staffId = OpaqueId.New(IdPrefixes.Staff, now, Random.Shared).ToString();
            adminDb.StaffAccounts.Add(new StaffAccountEntity
            {
                Id = staffId,
                ExternalSubject = fixture.ExternalSubject,
                Email = fixture.Email,
                DisplayName = fixture.DisplayName,
                Status = "active",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                Region = "US",
                LawfulBasis = "contract",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            staffId = existing.Id;
            // Dev convenience: a fixture never locks itself out across restarts even if a prior manual
            // deactivate-drill left it deactivated — this is fixture bootstrapping, not a real lifecycle.
            if (existing.Status != "active")
            {
                existing.Status = "active";
                existing.DeactivatedAt = null;
                existing.UpdatedAt = now;
            }
        }

        try
        {
            await adminDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent request for the SAME fixture raced this insert — idempotent-under-race:
            // re-read the winner rather than throwing.
            adminDb.ChangeTracker.Clear();
            existing = await adminDb.StaffAccounts.SingleAsync(s => s.ExternalSubject == fixture.ExternalSubject, ct);
            staffId = existing.Id;
        }

        var systemActorId = OpaqueId.New(IdPrefixes.System, now, Random.Shared).ToString();
        var activeRoleCodes = await adminDb.StaffRoleGrants
            .Where(g => g.StaffId == staffId && g.RevokedAt == null)
            .Select(g => g.Role)
            .ToListAsync(ct);

        foreach (var role in fixture.Roles)
        {
            var code = StaffRoleCodes.ToCode(role);
            if (activeRoleCodes.Contains(code))
            {
                continue;
            }

            adminDb.StaffRoleGrants.Add(new StaffRoleGrantEntity
            {
                Id = OpaqueId.New(IdPrefixes.StaffRoleGrant, now, Random.Shared).ToString(),
                StaffId = staffId,
                Role = code,
                GrantedBy = systemActorId,
                GrantReason = "devseams fixture provisioning",
                GrantedAt = now,
                Region = "US",
                LawfulBasis = "contract",
            });
        }

        try
        {
            await adminDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Same race, on the grant's ux_active_grant partial unique index — a concurrent request
            // already granted this exact role; nothing left to do.
            adminDb.ChangeTracker.Clear();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation };
}
