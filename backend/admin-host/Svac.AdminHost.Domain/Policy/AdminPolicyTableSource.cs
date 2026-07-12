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
    // SECURITY_REVIEW_S5.md S5-01: the union of everyone who can commit AT LEAST ONE config.set action
    // (core.config.set.ops's own allowlist, {SuperAdmin, EconomyOps} — core.config.set.founder's
    // {SuperAdmin} is already a subset) — an EconomyOps hat needs to SEE the desk's current values to use
    // its own ops-scope edit form at all, so read access can never be narrower than write access for the
    // roles that actually edit here. Least-privilege beyond that (matching admin.audit.read's own
    // SuperAdmin-v0 posture) would strand EconomyOps with edit rights but no page to use them from.
    private static readonly IReadOnlySet<StaffRole> ConfigReadRoles = new HashSet<StaffRole>
    {
        StaffRole.SuperAdmin, StaffRole.EconomyOps,
    };

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

        // SECURITY_REVIEW_S5.md S5-01 (fixNow): the Config Registry desk's READ surface had NO policy row
        // at all — ConfigRegistry.razor.cs's OnInitializedAsync called IConfigRegistry.ListEntries()
        // unconditionally, leaking the FULL 9A registry (every founder/ops/set-scope value, including
        // verification.age_gate_challenge_threshold and every other business-sensitive tunable) to any
        // request that reached the page, staff or not. This row + the page's own new Authorize gate close
        // that — mirrors Dashboard.razor.cs/UserSearch.razor.cs's identical "a VIEW is gated by a direct
        // IPolicyEngine.Authorize check, never routed through the executor" pattern (a config-registry
        // VIEW carries no per-view audit requirement, unlike a config EDIT, which already rides
        // core.config.set.founder/ops through the executor untouched by this row).
        new PolicyTableEntry(
            Action: "admin.config.read",
            ActorKinds: StaffOnly,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.admin_config_read",
            StaffRoleAllowlistNote: "staff: SuperAdmin, EconomyOps — the union of every role that can commit at least one core.config.set.* action (§S5-01); Analyst/SafetyAgent/ContentModerator/VenueConOps have no config.set row at all",
            IsReadPath: true,
            StaffRoles: ConfigReadRoles).Validate(),

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
