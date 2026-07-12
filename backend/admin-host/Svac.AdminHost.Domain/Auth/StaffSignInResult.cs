using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Auth;

/// <summary>
/// The closed outcome union <see cref="StaffSignInPipeline.SignIn"/> returns (SLICE_S5_CONTRACT.md §1b) —
/// mirrors <c>PolicyDecision</c>'s own closed-union shape so callers pattern-match, never string-sniff.
/// </summary>
public abstract record StaffSignInResult
{
    /// <summary>The subject resolved to an active staff row and the MFA claim was present. <see
    /// cref="RolesHeld"/> is the full grant snapshot at sign-in time (read fresh from the grants table,
    /// NEVER from Entra claims, per §1b) — the caller mints the cookie from this.</summary>
    public sealed record Allowed(ActorRef Staff, IReadOnlySet<StaffRole> RolesHeld) : StaffSignInResult;

    /// <summary>
    /// MFA is checked FIRST, before any directory lookup (§1b: "MFA is enforced by US, fail-closed ...
    /// absence ⇒ sign-in refused"), so this refusal fires identically whether or not a staff row exists
    /// for the subject — the two facts are never conflated in the audit trail.
    /// </summary>
    public sealed record RefusedNoMfa : StaffSignInResult;

    /// <summary>No admin.staff_accounts row for this external_subject. JIT provisioning is REFUSED (§1b)
    /// — this pipeline NEVER inserts a row on this path, under any circumstance.</summary>
    public sealed record RefusedUnknownSubject : StaffSignInResult;

    /// <summary>A row exists but status != 'active' (deactivated).</summary>
    public sealed record RefusedInactiveAccount : StaffSignInResult;
}
