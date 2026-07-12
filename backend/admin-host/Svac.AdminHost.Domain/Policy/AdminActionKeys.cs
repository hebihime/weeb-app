namespace Svac.AdminHost.Domain.Policy;

/// <summary>
/// The action keys the (Phase 2) <c>AdminActionExecutor</c> will register itself against —
/// SLICE_S5_CONTRACT.md §1c's "Boot law: RequireAdminActionsCovered() — every action key registered with
/// the executor resolves to a PolicyTable row at startup or the host refuses to boot." The executor
/// itself (staff re-read, hat computation, four-eyes, reason check, the one same-tx audit event) is real
/// business logic and explicitly out of THIS scaffold's deliverable list; this static list is the data
/// half of that boot law, kept separate on purpose so Phase 2 has exactly one place to wire the executor
/// against — either this same list (grown as desks register verbs) or a superset it asserts contains
/// this list, never a second, drifting enumeration.
///
/// Every key here MUST resolve to an <see cref="AdminPolicyTableSource"/> row (proven by
/// Svac.AdminHost's RequireAdminActionsCovered boot check, mirroring Svac.DomainCore.Hosting.
/// StartupPolicyCoverage.RequireMutationsPolicyMapped's shape at the layer the admin host actually
/// mutates through, per §1c).
/// </summary>
public static class AdminActionKeys
{
    /// <summary>The five real day-one AdminActionExecutor consumers named in §8 seam 3 as
    /// grant/revoke/provision/deactivate/config-set — the four verb-less staff-lifecycle actions here;
    /// config.set rides the ALREADY-covered core.config.set.* rows (§1c: "verbs with a native 3A event
    /// are NOT double-logged"), so it is not repeated in this executor-owned list.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "admin.staff.provision",
        "admin.staff.deactivate",
        "admin.staff.reactivate",
        "admin.staff.role_grant",
        "admin.staff.role_revoke",
    };
}
