namespace Svac.DomainCore.Contracts.Policy;

/// <summary>
/// One contributor of rows to the boot-time UNION that forms the enforced <see cref="IPolicyTable"/>
/// (PHASE_2A_SUBSTRATE.md §1, SLICE_S5_CONTRACT.md §1d). Domain-core's own rows are source #1
/// (<c>CorePolicyTableSource</c>, this surgery); every feature module contributes its own verbs by
/// registering an additional source — NEVER by editing domain-core or another module's table. A
/// duplicate action key across two registered sources is a boot refusal (fail-closed, red-fixture-proven).
/// </summary>
public interface IPolicyTableSource
{
    public IReadOnlyList<PolicyTableEntry> Entries { get; }
}
