using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.DomainCore.Policy;

/// <summary>
/// The checked-in typed C# policy table (SLICE_S1_CONTRACT.md §3). Zero consumer mutation endpoints at
/// S1, so zero consumer-actor rows — the substrate's own internal verbs get real rows so the engine is
/// exercised by real consumers (the migration seed loader, the purge scheduler, the ledger reversal
/// spot-check pipeline), not vacuously. This is the ONE source of truth the generated action×axis matrix
/// suite and the startup boot-refusal check both read.
/// </summary>
public sealed class PolicyTable : IPolicyTable
{
    public IReadOnlyList<PolicyTableEntry> Entries { get; } = BuildEntries();

    public PolicyTableEntry? Find(string action) => Entries.FirstOrDefault(e => e.Action == action);

    private static PolicyTableEntry[] BuildEntries() => new[]
    {
        new PolicyTableEntry(
            Action: "core.config.set.ops",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_config_set_ops",
            StaffRoleAllowlistNote: "system: seed loader; staff: SuperAdmin, EconomyOps").Validate(),

        new PolicyTableEntry(
            Action: "core.config.set.founder",
            ActorKinds: new HashSet<ActorKind> { ActorKind.System, ActorKind.Staff },
            Axes: PolicyAxis.Role,
            DenyMode: PolicyDenyMode.DenyStandard,
            RequiresReason: true,
            ReasonKey: "policy.denied.core_config_set_founder",
            StaffRoleAllowlistNote: "system: seed loader; staff: SuperAdmin (dangerous-edit interstitial at S5)").Validate(),

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
            StaffRoleAllowlistNote: "system: spot-check pipeline; staff: SuperAdmin, EconomyOps — reversal is the ONLY correction verb").Validate(),

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
            StaffRoleAllowlistNote: "system: scheduler; staff: SuperAdmin (manual DSR)").Validate(),

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
