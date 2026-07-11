using Microsoft.EntityFrameworkCore;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;
using Svac.Identity.Persistence;

namespace Svac.Identity.Policy;

/// <summary>
/// The `session` resource-ownership resolver (SLICE_S3_CONTRACT.md §3a). Predicate-folded ownership: the
/// SAME single indexed fetch the DELETE /v1/me/sessions/{sessionId} handler would do — unknown id
/// resolves to null owner, which <see cref="Svac.DomainCore.Policy.PolicyEngine"/> already collapses into
/// DenyAsAbsence (foreign and nonexistent are ONE branch, discharging SilentRej-L4 structurally).
/// </summary>
public sealed class SessionOwnershipResolver(IdentityDbContext db) : IResourceOwnershipResolver
{
    public string ResourceType => "session";

    public async Task<OpaqueId?> OwnerOf(string resourceId, CancellationToken ct = default)
    {
        var accountId = await db.Sessions
            .Where(s => s.SessionId == resourceId)
            .Select(s => s.AccountId)
            .SingleOrDefaultAsync(ct);

        return accountId is null ? null : OpaqueId.Parse(accountId);
    }
}

/// <summary>The `device` resource-ownership resolver (SLICE_S3_CONTRACT.md §3a) — same shape as <see cref="SessionOwnershipResolver"/>.</summary>
public sealed class DeviceOwnershipResolver(IdentityDbContext db) : IResourceOwnershipResolver
{
    public string ResourceType => "device";

    public async Task<OpaqueId?> OwnerOf(string resourceId, CancellationToken ct = default)
    {
        var accountId = await db.Devices
            .Where(d => d.DeviceId == resourceId)
            .Select(d => d.AccountId)
            .SingleOrDefaultAsync(ct);

        return accountId is null ? null : OpaqueId.Parse(accountId);
    }
}
