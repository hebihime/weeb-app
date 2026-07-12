using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Policy;

/// <summary>
/// Domain-core's own policy rows — source #1 of the boot-time union (PHASE_2A_SUBSTRATE.md §1,
/// SLICE_S5_CONTRACT.md §1d: "domain-core rows are source #1"). This is the exact 7-row table S1 shipped
/// as the old <c>PolicyTable</c> class, moved verbatim behind <see cref="IPolicyTableSource"/> so
/// <see cref="PolicyTable"/> can become the union of every registered source. With ONLY this source
/// registered (S1/S2), the resulting table is IDENTICAL to today's.
///
/// [S5] Four rows' StaffRoles are typed here (PHASE_2A_SUBSTRATE.md §1: "core-row typing") — additive,
/// no decision change, because no Staff actor is ever authorized against these rows in the S1/S2 suites.
/// </summary>
public sealed class CorePolicyTableSource : IPolicyTableSource
{
    public IReadOnlyList<PolicyTableEntry> Entries { get; } = BuildEntries();

    private static PolicyTableEntry[] BuildEntries() => new[]
    {
        new PolicyTableEntry(
            Action: "core.config.set.ops",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_config_set_ops",
            StaffRoleAllowlistNote: "system: seed loader; staff: SuperAdmin, EconomyOps",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin, StaffRole.EconomyOps }).Validate(),

        new PolicyTableEntry(
            Action: "core.config.set.founder",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_config_set_founder",
            StaffRoleAllowlistNote: "system: seed loader; staff: SuperAdmin (dangerous-edit interstitial at S5)",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin }).Validate(),

        new PolicyTableEntry(
            Action: "core.ledger.append",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: false,
            ReasonKey: "policy.denied.core_ledger_append",
            StaffRoleAllowlistNote: "system only — never client-reachable; structural: entry prevents an ungated route ever shipping").Validate(),

        new PolicyTableEntry(
            Action: "core.ledger.reverse",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_ledger_reverse",
            StaffRoleAllowlistNote: "system: spot-check pipeline; staff: SuperAdmin, EconomyOps — reversal is the ONLY correction verb",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin, StaffRole.EconomyOps }).Validate(),

        new PolicyTableEntry(
            Action: "core.event.tombstone",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_event_tombstone",
            StaffRoleAllowlistNote: "system (purge pipeline ONLY)").Validate(),

        new PolicyTableEntry(
            Action: "core.purge.execute",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_purge_execute",
            StaffRoleAllowlistNote: "system: scheduler; staff: SuperAdmin (manual DSR)",
            StaffRoles: new HashSet<StaffRole> { StaffRole.SuperAdmin }).Validate(),

        new PolicyTableEntry(
            Action: "core.quota.consume",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.User, ActorKind.Staff, ActorKind.Partner, ActorKind.Anonymous },
            Axes: PolicyAxis.None,
            DenyMode: PolicyDenyMode.DenyAsLimit,
            RequiresReason: false,
            ReasonKey: "n/a",
            StaffRoleAllowlistNote: "internal chokepoint",
            DynamicQuotaKey: true).Validate(),
    };
}
