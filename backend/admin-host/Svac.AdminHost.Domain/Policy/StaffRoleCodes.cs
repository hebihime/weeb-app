using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Policy;

/// <summary>
/// The snake_case &lt;-&gt; <see cref="StaffRole"/> mapping for <c>admin.staff_role_grants.role</c>
/// (SLICE_S5_CONTRACT.md §2's CHECK constraint literals, verbatim). One mapping, used by every reader of
/// the grants table (the sign-in pipeline, <c>GrantTableStaffRoleResolver</c>, the bootstrapper) — never
/// re-derived ad hoc per call site.
/// </summary>
public static class StaffRoleCodes
{
    private static readonly Dictionary<StaffRole, string> ToCodeMap = new()
    {
        [StaffRole.SuperAdmin] = "super_admin",
        [StaffRole.SafetyAgent] = "safety_agent",
        [StaffRole.ContentModerator] = "content_moderator",
        [StaffRole.VenueConOps] = "venue_con_ops",
        [StaffRole.EconomyOps] = "economy_ops",
        [StaffRole.Analyst] = "analyst",
    };

    private static readonly Dictionary<string, StaffRole> FromCodeMap =
        ToCodeMap.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static string ToCode(StaffRole role) => ToCodeMap.TryGetValue(role, out var code)
        ? code
        : throw new ArgumentOutOfRangeException(nameof(role), role, "unregistered StaffRole — extend StaffRoleCodes alongside the enum, never leave a silent gap.");

    /// <summary>Parses a persisted role code. Throws on an unrecognized code rather than silently
    /// dropping it from a grants set — a row the CHECK constraint accepted but this mapping does not
    /// know about is a contract-drift bug, not a value to swallow.</summary>
    public static StaffRole Parse(string code) => FromCodeMap.TryGetValue(code, out var role)
        ? role
        : throw new ArgumentOutOfRangeException(nameof(code), code, "unrecognized staff_role_grants.role code — extend StaffRoleCodes alongside admin.staff_role_grants' CHECK constraint, never leave a silent gap.");
}
