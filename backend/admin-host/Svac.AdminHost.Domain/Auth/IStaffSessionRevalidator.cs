using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.AdminHost.Domain.Auth;

/// <summary>
/// The SECOND revocation leg (SLICE_S5_CONTRACT.md §1b: "security_stamp ... cookie validation +
/// revalidation ... re-checks stamp + status — a deactivated operator loses a LIVE session within the
/// interval"). Called by the cookie auth handler's <c>OnValidatePrincipal</c> on every request past the
/// 9A <c>admin.session_revalidate_seconds</c> interval, NEVER on every request (that would defeat the
/// point of a cookie).
/// </summary>
public interface IStaffSessionRevalidator
{
    public Task<bool> IsStillValid(OpaqueId staffId, string cookieSecurityStamp, CancellationToken ct = default);
}

/// <summary>The real, grant-table-backed implementation: re-reads the CURRENT row every call — a stale
/// cookie-carried stamp (from before a grant/revoke/deactivate bumped it) or a deactivated row both fail.</summary>
public sealed class GrantTableStaffSessionRevalidator(AdminDbContext adminDb) : IStaffSessionRevalidator
{
    public async Task<bool> IsStillValid(OpaqueId staffId, string cookieSecurityStamp, CancellationToken ct = default)
    {
        var id = staffId.ToString();
        var row = await adminDb.StaffAccounts.SingleOrDefaultAsync(s => s.Id == id, ct);
        return row is not null && row.Status == "active" && row.SecurityStamp == cookieSecurityStamp;
    }
}
