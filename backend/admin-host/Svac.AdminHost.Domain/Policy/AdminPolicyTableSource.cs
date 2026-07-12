using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Domain.Policy;

/// <summary>
/// The admin host's own policy rows (SLICE_S5_CONTRACT.md §3) — one registered <see
/// cref="IPolicyTableSource"/>, unioned with <c>CorePolicyTableSource</c> (+ identity's, + AimlRouter's,
/// if registered on the SAME host — they are not, admin is its own deploy unit) at boot. Every row here
/// is data, not business logic: the executor that will actually evaluate the staff-lifecycle verbs
/// (AdminActionExecutor) is Phase 2 (SLICE_S5_CONTRACT.md deliverable list) — rows without an executor/
/// endpoint yet are legal, exactly as SLICE_S3_CONTRACT.md's IdentityPolicyTableSource already
/// established for its own Pass-2-adjacent rows. All rows carry <see cref="ActorKind.Staff"/> (+ System
/// for the two bootstrap-capable rows) — the §0 structural law "staff power exists only inside the
/// admin host" (a): 4A admin.* rows carry only ActorKind.Staff (+System for bootstrap).
/// </summary>
public sealed class AdminPolicyTableSource : IPolicyTableSource
{
    private static readonly IReadOnlySet<ActorKind> StaffOnly = new HashSet<ActorKind> { ActorKind.Staff };
    private static readonly IReadOnlySet<ActorKind> StaffAndSystem = new HashSet<ActorKind> { ActorKind.Staff, ActorKind.System };
    private static readonly IReadOnlySet<ActorKind> StaffAndAnonymous = new HashSet<ActorKind> { ActorKind.Staff, ActorKind.Anonymous };

    private static readonly IReadOnlySet<StaffRole> SuperAdminOnly = new HashSet<StaffRole> { StaffRole.SuperAdmin };
    private static readonly IReadOnlySet<StaffRole> UserSearchRoles = new HashSet<StaffRole>
    {
        StaffRole.SuperAdmin, StaffRole.SafetyAgent, StaffRole.ContentModerator,
    };
    private static readonly IReadOnlySet<StaffRole> AllSixRoles = new HashSet<StaffRole>(Enum.GetValues<StaffRole>());

    public IReadOnlyList<PolicyTableEntry> Entries { get; } = BuildEntries();

    private static PolicyTableEntry[] BuildEntries() => new[]
    {
        new PolicyTableEntry(
            Action: "admin.staff.provision",
            ActorKinds: StaffAndSystem,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.admin_staff_provision",
            StaffRoleAllowlistNote: "system: bootstrap (first SuperAdmin, §1b); staff: SuperAdmin",
            StaffRoles: SuperAdminOnly).Validate(),

        new PolicyTableEntry(
            Action: "admin.staff.deactivate",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.admin_staff_deactivate",
            StaffRoleAllowlistNote: "staff: SuperAdmin — bumps the security_stamp, kills live sessions",
            StaffRoles: SuperAdminOnly).Validate(),

        new PolicyTableEntry(
            Action: "admin.staff.reactivate",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.admin_staff_reactivate",
            StaffRoleAllowlistNote: "staff: SuperAdmin — lifecycle completeness",
            StaffRoles: SuperAdminOnly).Validate(),

        new PolicyTableEntry(
            Action: "admin.staff.role_grant",
            ActorKinds: StaffAndSystem,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.admin_staff_role_grant",
            StaffRoleAllowlistNote: "system: bootstrap (first SuperAdmin grant, §1b); staff: SuperAdmin-only per A5 — grants are audited",
            StaffRoles: SuperAdminOnly).Validate(),

        new PolicyTableEntry(
            Action: "admin.staff.role_revoke",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.admin_staff_role_revoke",
            StaffRoleAllowlistNote: "staff: SuperAdmin",
            StaffRoles: SuperAdminOnly).Validate(),

        new PolicyTableEntry(
            Action: "admin.user_search.execute",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.admin_user_search_execute",
            StaffRoleAllowlistNote: "staff: SuperAdmin, SafetyAgent, ContentModerator — Analyst structurally excluded (\"no user PII beyond aggregate\"); the audited query IS the record, no reason required",
            IsReadPath: true,
            StaffRoles: UserSearchRoles).Validate(),

        new PolicyTableEntry(
            Action: "admin.audit.read",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.admin_audit_read",
            StaffRoleAllowlistNote: "staff: SuperAdmin (v0) — raw events carry user refs/PII, least privilege; desk slices widen per-desk by row edit",
            IsReadPath: true,
            StaffRoles: SuperAdminOnly).Validate(),

        new PolicyTableEntry(
            Action: "admin.dashboard.read",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.admin_dashboard_read",
            StaffRoleAllowlistNote: "staff: all six roles — Analyst's whole scope",
            IsReadPath: true,
            StaffRoles: AllSixRoles).Validate(),

        new PolicyTableEntry(
            // Maps Blazor infrastructure endpoints (pre-auth sign-in page, component dispatch) honestly
            // for RequireMutationsPolicyMapped — DenyAsAbsence, no role restriction (every actual staff
            // verb is gated INSIDE by the executor + RequireAdminActionsCovered, never by this row).
            Action: "admin.host.transport",
            ActorKinds: StaffAndAnonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            StaffRoleAllowlistNote: "no role restriction — Blazor page/component dispatch, not a staff verb",
            IsReadPath: true).Validate(),
    };
}
