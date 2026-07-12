using System.Text.Json;
using Svac.AdminHost.Domain.Execution;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Audit;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Streams;

namespace Svac.AdminHost.Domain.Audit;

/// <summary>The Audit Trail desk's ONE closed outcome (mirrors <see cref="AdminActionResult"/>'s shape).</summary>
public abstract record AuditViewOutcome
{
    public sealed record Rendered(AuditPage Page) : AuditViewOutcome;
    public sealed record AccessDenied(string ReasonKey) : AuditViewOutcome;
}

/// <summary>
/// The audited-view path around <see cref="IAuditReader"/> (SLICE_S5_CONTRACT.md §0's "Audit Trail:
/// IAuditReader.Query via QuickGrid, cursor-paged; SuperAdmin-only (v0); each VIEW query itself audited
/// (filter metadata, not results)"). Every render of the desk — including the default, filter-less
/// landing view — is itself "a query" and is audited exactly once: routed through
/// <see cref="IAdminActionExecutor"/> (auth re-read + hat computation + 4A Authorize on
/// <c>admin.audit.read</c>) with the self-logging <c>admin.audit.viewed</c> event appended INSIDE the
/// executor's own `work` delegate (nested inside the call to its own Execute method, so
/// <c>AdminActionChokepointArchTests</c>'s text scan never flags it) — never the raw RESULT rows, only
/// the filter the operator applied, per the contract's own "filter metadata, not results" law. The
/// actual <see cref="IAuditReader.Query"/> READ runs AFTER the executor commits (a pure read has nothing
/// to roll back if it happened first, but sequencing it after keeps "auth→4A→...→audit→render" the exact
/// order the User Search sibling flow follows too).
/// </summary>
public sealed class AuditViewExecutionService(
    IAdminActionExecutor executor,
    IAuditReader auditReader,
    IEventStore eventStore)
{
    private const string Action = "admin.audit.read";

    public async Task<AuditViewOutcome> View(RequestContext callerCtx, AuditFilter filter, string? cursor, CancellationToken ct = default)
    {
        var target = new TargetRef("staff_account", callerCtx.Actor.Id.ToString());
        var result = await executor.Execute(callerCtx, Action, target, reason: null, work: async workCtx =>
        {
            var hat = workCtx.Staff?.ActingHat.ToString();
            var payload = JsonSerializer.Serialize(new
            {
                filter = new
                {
                    event_type_prefix = filter.EventTypePrefix,
                    actor_ref = filter.ActorRef,
                    stream_id = filter.StreamId,
                    from = filter.From,
                    to = filter.To,
                },
                hat,
            });
            await eventStore.Append(StreamType.Audit, target.ResourceId!, "admin.audit.viewed", payload, workCtx, ExpectedVersion.AnyVersion, ct);
        }, ct);

        if (result is not AdminActionResult.Success)
        {
            var reasonKey = result is AdminActionResult.Denied denied ? denied.ReasonKey : "policy.denied.unmapped_action";
            return new AuditViewOutcome.AccessDenied(reasonKey);
        }

        var page = await auditReader.Query(filter, new Svac.DomainCore.Contracts.Api.CursorPageRequest(cursor), ct);
        return new AuditViewOutcome.Rendered(page);
    }
}
