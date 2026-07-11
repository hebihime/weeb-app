using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.Identity.Policy;

/// <summary>
/// Identity's own policy rows — one registered <see cref="IPolicyTableSource"/>, unioned with
/// <c>CorePolicyTableSource</c> at boot (SLICE_S3_CONTRACT.md §3b). Carries EVERY §3b row, including the
/// internal suspend/ban/reinstate/deletion.execute rows that have NO endpoint at S3 (rows without
/// endpoints are legal; endpoints without rows are boot refusal, §3a).
/// </summary>
public sealed class IdentityPolicyTableSource : IPolicyTableSource
{
    private static readonly IReadOnlySet<ActorKind> Anonymous = new HashSet<ActorKind> { ActorKind.Anonymous };
    private static readonly IReadOnlySet<ActorKind> User = new HashSet<ActorKind> { ActorKind.User };
    private static readonly IReadOnlySet<ActorKind> SystemOnly = new HashSet<ActorKind> { ActorKind.System };
    private static readonly IReadOnlySet<ActorKind> SystemAndStaff = new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff };
    private static readonly IReadOnlySet<string> ActiveOnly = new HashSet<string> { "active" };
    private static readonly IReadOnlySet<string> ActiveOrSuspended = new HashSet<string> { "active", "suspended" };

    public IReadOnlyList<PolicyTableEntry> Entries { get; } = BuildEntries();

    private static PolicyTableEntry[] BuildEntries() => new[]
    {
        new PolicyTableEntry(
            Action: "identity.signup.challenge",
            ActorKinds: Anonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a").Validate(),

        new PolicyTableEntry(
            Action: "identity.signup.confirm",
            ActorKinds: Anonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a").Validate(),

        new PolicyTableEntry(
            Action: "identity.signup.complete",
            ActorKinds: Anonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a").Validate(),

        new PolicyTableEntry(
            Action: "identity.auth.request_code",
            ActorKinds: Anonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a").Validate(),

        new PolicyTableEntry(
            Action: "identity.auth.create_session",
            ActorKinds: Anonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a").Validate(),

        new PolicyTableEntry(
            Action: "identity.auth.refresh",
            ActorKinds: Anonymous,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a").Validate(),

        new PolicyTableEntry(
            Action: "identity.auth.logout",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: null /* any state — rights survive suspension AND grace */).Validate(),

        new PolicyTableEntry(
            Action: "identity.me.read",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            // SilentRej-L4: the FIRST true read row — IsReadPath=true, S1's read-path guard goes non-vacuous.
            IsReadPath: true,
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            Action: "identity.settings.update",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        new PolicyTableEntry(
            Action: "identity.handle.change",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsLimit,
            RequiresReason: false,
            ReasonKey: "n/a",
            QuotaKeyForLimit: "identity.handle.change",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOnly).Validate(),

        new PolicyTableEntry(
            Action: "identity.email.change",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        new PolicyTableEntry(
            Action: "identity.email.change_confirm",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        new PolicyTableEntry(
            Action: "identity.session.list",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            IsReadPath: true,
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            // THE Auth-F3 exemplar route (§3): DELETE /v1/me/sessions/{sessionId}.
            Action: "identity.session.revoke",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("session"),
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            Action: "identity.device.register",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        new PolicyTableEntry(
            Action: "identity.device.remove",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("device"),
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            Action: "identity.consent.set_push_category",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        // [/v1/me/push-consents build] The GET-side row §3b's table left implicit alongside the PUT verb
        // it lists explicitly — added now so the read, like `identity.me.read`/`identity.session.list`
        // before it, goes through the SAME chokepoint rather than shipping ungated (SilentRej-L4's own
        // logic: every consumer-reachable read gets a row, not just mutations). Same accountState axis as
        // the PUT row — push-category management is settings-shaped and denies in grace, same as the write.
        new PolicyTableEntry(
            Action: "identity.consent.read_push_categories",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            IsReadPath: true,
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        // --- Pass-2-adjacent rows registered now per §3b (schema exists; endpoints/pipelines are Pass 2).
        // Registering them now means an ungated export/deletion HTTP route can never ship even before
        // Pass 2 exists (B1 machinery) — but NO endpoint in THIS build maps onto either action.
        new PolicyTableEntry(
            Action: "identity.export.request",
            ActorKinds: User,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsLimit,
            RequiresReason: false,
            ReasonKey: "n/a",
            QuotaKeyForLimit: "identity.export.request.daily",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            Action: "identity.export.read",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            IsReadPath: true,
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("export"),
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            Action: "identity.deletion.request",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: ActiveOrSuspended).Validate(),

        new PolicyTableEntry(
            Action: "identity.deletion.cancel",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: null).Validate(),

        new PolicyTableEntry(
            Action: "identity.deletion.read",
            ActorKinds: User,
            Axes: PolicyAxis.AccountState,
            DenyMode: PolicyDenyMode.DenyAsAbsence,
            RequiresReason: false,
            ReasonKey: "n/a",
            IsReadPath: true,
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.SelfOnly,
            AllowedAccountStates: null).Validate(),

        // --- Internal-only rows (§3b): NO HTTP mapping at S3 — S12 drives suspend/ban/reinstate,
        // the deletion worker (Pass 2) drives execute. Their existence now means an ungated moderation/
        // deletion-execution route can never ship (S1 core.ledger.append precedent).
        new PolicyTableEntry(
            Action: "identity.account.suspend",
            ActorKinds: SystemAndStaff,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.identity_account_suspend",
            StaffRoleAllowlistNote: "system: automation; staff: SuperAdmin, SafetyAgent",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin, StaffRole.SafetyAgent },
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("account")).Validate(),

        new PolicyTableEntry(
            Action: "identity.account.ban",
            ActorKinds: SystemAndStaff,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.identity_account_ban",
            StaffRoleAllowlistNote: "system: automation; staff: SuperAdmin, SafetyAgent",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin, StaffRole.SafetyAgent },
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("account")).Validate(),

        new PolicyTableEntry(
            Action: "identity.account.reinstate",
            ActorKinds: SystemAndStaff,
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.identity_account_reinstate",
            StaffRoleAllowlistNote: "system: automation; staff: SuperAdmin, SafetyAgent",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin, StaffRole.SafetyAgent },
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("account")).Validate(),

        new PolicyTableEntry(
            Action: "identity.deletion.execute",
            ActorKinds: SystemOnly,
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.identity_deletion_execute",
            StaffRoleAllowlistNote: "system only (the deletion worker, Pass 2) — the job row is provenance",
            TargetRule: Svac.DomainCore.Contracts.Policy.TargetRule.OwnedResource("account")).Validate(),
    };
}
