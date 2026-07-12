using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.AdminHost.Domain.Execution;

/// <summary>
/// The audited-action chokepoint (SLICE_S5_CONTRACT.md §1c, §8 seam 3) — the ONE door every staff
/// mutation, now and for every future desk, flows through. See <see cref="AdminActionExecutor"/> for the
/// full sequence documentation.
/// </summary>
public interface IAdminActionExecutor
{
    /// <param name="callerCtx">
    /// <see cref="RequestContext.Actor"/> MUST be <see cref="ActorKind.Staff"/> (or <see
    /// cref="ActorKind.System"/> for the two bootstrap-capable actions, §3) — a caller reaching this door
    /// with any other actor kind is a caller bug, not a policy question, and throws
    /// <see cref="ArgumentException"/> before any DB is touched. <see cref="RequestContext.Staff"/> is
    /// IGNORED on input (overwritten internally after the fresh re-read, step 2) — callers never get to
    /// assert their own hat.
    /// </param>
    /// <param name="action">A PolicyTable action key, e.g. <c>"admin.staff.deactivate"</c> or <c>"core.config.set.ops"</c>.</param>
    /// <param name="target">
    /// The REAL resource this action targets — <c>("staff_account", "stf_...")</c>,
    /// <c>("config_entry", "match.swipe_cap_free_daily")</c> — never a <c>TargetRef.ForAction</c>
    /// placeholder (the admin-path half of Auth-F3, §9).
    /// </param>
    /// <param name="reason">Mandatory iff the action's PolicyTable row declares <c>RequiresReason</c> —
    /// EXCEPT <c>core.config.set.*</c> actions, whose reason requirement is per-CONFIG-KEY (checked inside
    /// <see cref="Svac.DomainCore.Contracts.Config.IConfigRegistry.SetValue{T}"/> itself, called from
    /// <paramref name="work"/>), never at this generic, per-ACTION layer.</param>
    /// <param name="work">The domain mutation. Invoked ONLY after every gate (staff-active, reason,
    /// Authorize, four-eyes) passes, inside the SAME database transaction this executor's own audit-event
    /// append commits or rolls back with.</param>
    public Task<AdminActionResult> Execute(
        RequestContext callerCtx,
        string action,
        TargetRef target,
        string? reason,
        Func<RequestContext, Task> work,
        CancellationToken ct = default);
}
