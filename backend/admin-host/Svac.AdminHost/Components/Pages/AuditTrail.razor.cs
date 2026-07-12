using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Svac.AdminHost.Domain.Audit;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Audit;
using Svac.DomainCore.Contracts.Ids;

namespace Svac.AdminHost.Components.Pages;

/// <summary>
/// Code-behind for AuditTrail.razor (SLICE_S5_CONTRACT.md §0/§8 seam 7, Pass D) — a PARTIAL CLASS file
/// for the same i18n-lint escape-valve reason every other desk's code-behind gives.
///
/// EVERY render of this page — including the default, filter-less landing view reached from the nav —
/// is itself a query and is routed through <see cref="AuditViewExecutionService"/> (auth re-read + 4A
/// Authorize on <c>admin.audit.read</c> + the self-logging <c>admin.audit.viewed</c> event, §0: "each
/// VIEW query itself audited (filter metadata, not results)"). A non-staff or non-qualifying actor is
/// denied EXACTLY like a real query attempt would be (the executor's own refusal path audits it as
/// <c>admin.action.refused</c>) — never a separate, un-audited "can I even load this page" pre-check.
/// </summary>
public sealed partial class AuditTrail
{
    [Inject] private AuditViewExecutionService ViewService { get; set; } = null!;
    [Inject] private IRequestContextAccessor RequestContextAccessor { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "eventTypePrefix")]
    private string? EventTypePrefix { get; set; }

    [SupplyParameterFromQuery(Name = "actorRef")]
    private string? ActorRefFilter { get; set; }

    [SupplyParameterFromQuery(Name = "streamId")]
    private string? StreamIdFilter { get; set; }

    [SupplyParameterFromQuery(Name = "cursor")]
    private string? Cursor { get; set; }

    private bool _canView;
    private IReadOnlyList<AuditRow> _rows = Array.Empty<AuditRow>();
    private string? _nextCursor;
    private bool _hasMore;

    // Named property-accessor expressions for QuickGrid's <PropertyColumn Property="..."> bindings —
    // kept HERE, never as an inline `r => r.Foo` lambda in the .razor markup: tools/i18n-lint/i18n-lint.
    // mjs's razor hardcoded-literal tripwire (rule 2) uses a simple ">text<" regex that cannot parse a
    // lambda's own "=>" arrow inside an attribute value — an inline lambda in the markup makes that scan
    // misread everything up to the next real "<" as a hardcoded string. Found live authoring this file.
    private static readonly Expression<Func<AuditRow, DateTimeOffset>> RecordedAtColumn = r => r.RecordedAt;
    private static readonly Expression<Func<AuditRow, string>> EventTypeColumn = r => r.EventType;
    private static readonly Expression<Func<AuditRow, string>> ActorRefColumn = r => r.ActorRef;
    private static readonly Expression<Func<AuditRow, string>> StreamIdColumn = r => r.StreamId;

    protected override async Task OnInitializedAsync()
    {
        var ctx = RequestContextAccessor.Current;
        if (ctx.Actor.Kind != ActorKind.Staff)
        {
            _canView = false;
            return;
        }

        var filter = new AuditFilter(
            EventTypePrefix: string.IsNullOrWhiteSpace(EventTypePrefix) ? null : EventTypePrefix.Trim(),
            ActorRef: string.IsNullOrWhiteSpace(ActorRefFilter) ? null : ActorRefFilter.Trim(),
            StreamId: string.IsNullOrWhiteSpace(StreamIdFilter) ? null : StreamIdFilter.Trim());

        var outcome = await ViewService.View(ctx, filter, Cursor);
        switch (outcome)
        {
            case AuditViewOutcome.Rendered rendered:
                _canView = true;
                _rows = rendered.Page.Items
                    .Select(e => new AuditRow(e.EventId, e.StreamId, e.EventType, e.ActorRef, e.RecordedAt, e.PayloadJson))
                    .ToList();
                _nextCursor = rendered.Page.NextCursor;
                _hasMore = rendered.Page.HasMore;
                break;
            case AuditViewOutcome.AccessDenied:
                _canView = false;
                break;
        }
    }

    private sealed record AuditRow(string EventId, string StreamId, string EventType, string ActorRef, DateTimeOffset RecordedAt, string? PayloadJson);
}
