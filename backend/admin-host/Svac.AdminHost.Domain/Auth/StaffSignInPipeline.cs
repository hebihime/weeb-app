using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.AdminHost.Domain.Auth;

/// <summary>
/// The staff sign-in pipeline (SLICE_S5_CONTRACT.md §1b, §8 seams 4/12) — the ONE code path both the prod
/// Entra/OIDC transport and <c>DevSeamsStaffTransport</c> call after translating their own credential
/// shape into <see cref="StaffExternalClaims"/>, so the pipeline itself is transport-agnostic and the E2E
/// exercises the real path in either environment (never a stub bypass).
///
/// Sequence, in this exact order (§1b): (1) MFA claim check FIRST, before any directory lookup — a
/// no-MFA refusal never conflates with an unknown-subject refusal in the audit trail; (2) directory
/// lookup by <c>external_subject</c> — no row ⇒ RefusedUnknownSubject, JIT NEVER provisions; (3)
/// <c>status == 'active'</c> check; (4) active grants read fresh from the grants table (never from Entra
/// claims) for the caller to mint a cookie/session from.
///
/// Every refusal is audited <c>admin.signin.refused</c> (metadata only: subject + a reason key, NEVER the
/// raw claim bag) — stream_id is <c>signin:{externalSubject}</c> pre-mapping (no ActorRef resolvable yet,
/// true for the no-MFA and unknown-subject legs) or the resolved <c>stf_</c> ref once a row is found
/// (true for the inactive-account leg). [Pass D] The ALLOWED leg is audited too, symmetrically:
/// <c>admin.signin.succeeded</c> (metadata only: subject, keyed by the resolved <c>stf_</c> ref) — this
/// is the live source the dashboard's "staff sign-ins" tile (SLICE_S5_CONTRACT.md §8 seam 2) reads.
/// </summary>
public sealed class StaffSignInPipeline(AdminDbContext adminDb, IEventStore eventStore)
{
    public async Task<StaffSignInResult> SignIn(StaffExternalClaims claims, RequestContext ctx, CancellationToken ct = default)
    {
        if (!claims.HasMfaClaim)
        {
            await AuditRefusal($"signin:{claims.ExternalSubject}", claims.ExternalSubject, "no_mfa_claim", ctx, ct);
            return new StaffSignInResult.RefusedNoMfa();
        }

        var staff = await adminDb.StaffAccounts.SingleOrDefaultAsync(s => s.ExternalSubject == claims.ExternalSubject, ct);
        if (staff is null)
        {
            await AuditRefusal($"signin:{claims.ExternalSubject}", claims.ExternalSubject, "unknown_subject", ctx, ct);
            return new StaffSignInResult.RefusedUnknownSubject();
        }

        if (staff.Status != "active")
        {
            await AuditRefusal(staff.Id, claims.ExternalSubject, "inactive_account", ctx, ct);
            return new StaffSignInResult.RefusedInactiveAccount();
        }

        var roleCodes = await adminDb.StaffRoleGrants
            .Where(g => g.StaffId == staff.Id && g.RevokedAt == null)
            .Select(g => g.Role)
            .ToListAsync(ct);
        var roles = roleCodes.Select(StaffRoleCodes.Parse).ToHashSet();

        // [Pass D fix] The allowed leg was never audited before this pass — every refusal leg was, but a
        // SUCCESSFUL sign-in left no live source for the dashboard's "staff sign-ins" tile (§8 seam 2) to
        // read (real-or-honestly-dark, §8 seam 16, forbids fabricating that tile from refusals alone).
        // Symmetric with AuditRefusal below: metadata only (subject, never the raw claim bag), keyed by
        // the resolved stf_ ref (a real row exists on this leg, unlike the no-MFA/unknown-subject
        // refusal legs above).
        await AuditSuccess(staff.Id, claims.ExternalSubject, ctx, ct);

        return new StaffSignInResult.Allowed(new ActorRef(OpaqueId.Parse(staff.Id), ActorKind.Staff), roles);
    }

    private async Task AuditSuccess(string streamId, string subject, RequestContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { subject });
        await eventStore.Append(StreamType.Audit, streamId, "admin.signin.succeeded", payload, ctx, ExpectedVersion.AnyVersion, ct);
    }

    private async Task AuditRefusal(string streamId, string subject, string reasonKey, RequestContext ctx, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { subject, reason_key = reasonKey });
        await eventStore.Append(StreamType.Audit, streamId, "admin.signin.refused", payload, ctx, ExpectedVersion.AnyVersion, ct);
    }
}
