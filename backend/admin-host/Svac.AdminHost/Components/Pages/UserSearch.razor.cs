using Microsoft.AspNetCore.Components;
using Svac.AdminHost.Domain.Search;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Components.Pages;

/// <summary>
/// Code-behind for UserSearch.razor (SLICE_S5_CONTRACT.md §0/§8 seam 1/6, Pass D) — a PARTIAL CLASS file
/// for the same i18n-lint escape-valve reason every other desk's code-behind gives.
///
/// Two DIFFERENT gates, deliberately: (1) landing on this page with no query term submitted is NOT
/// itself a search — showing the empty FORM is gated by a lightweight, direct
/// <see cref="IPolicyEngine.Authorize"/> check (mirrors Dashboard.razor.cs's own reasoning: nothing here
/// is a staff ACTION yet, so nothing needs auditing); (2) submitting a query term IS the audited
/// action — routed entirely through <see cref="UserSearchExecutionService"/> (auth→4A→quota→audit→
/// render, §0/§9), which re-derives the SAME Authorize decision internally via
/// <see cref="Svac.AdminHost.Domain.Execution.IAdminActionExecutor"/> regardless of what gate (1) already
/// found — never trusting gate (1) alone to have been the real one. Wire contract
/// (backend/e2e/admin-host.e2e.mjs's header comment): <c>GET|POST /user-search?query=...&amp;queryClass=...</c>
/// renders results INLINE (200), never a redirect — this page is GET-driven end to end, so a query
/// submitted via its own GET form round-trips through SupplyParameterFromQuery exactly like any other
/// navigation.
/// </summary>
public sealed partial class UserSearch
{
    [Inject] private IPolicyEngine PolicyEngine { get; set; } = null!;
    [Inject] private UserSearchExecutionService SearchService { get; set; } = null!;
    [Inject] private IRequestContextAccessor RequestContextAccessor { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "query")]
    private string? Query { get; set; }

    [SupplyParameterFromQuery(Name = "queryClass")]
    private string? QueryClassRaw { get; set; }

    [SupplyParameterFromQuery(Name = "cursor")]
    private string? Cursor { get; set; }

    private bool _canView;
    private bool _searchAttempted;
    private UserSearchExecutionOutcome? _outcome;
    private static readonly IReadOnlyList<string> AllQueryClasses = Enum.GetNames<UserSearchQueryClass>();

    protected override async Task OnInitializedAsync()
    {
        var ctx = RequestContextAccessor.Current;
        if (ctx.Actor.Kind != ActorKind.Staff)
        {
            _canView = false;
            return;
        }

        var decision = await PolicyEngine.Authorize(ctx.Actor, "admin.user_search.execute", TargetRef.ForAction("admin.user_search.execute"));
        _canView = decision.IsAllowed;
        if (!_canView)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Query) || !Enum.TryParse<UserSearchQueryClass>(QueryClassRaw, ignoreCase: false, out var queryClass))
        {
            return; // no (or malformed) query submitted -- render the form only, nothing to audit yet.
        }

        _searchAttempted = true;
        _outcome = await SearchService.Execute(ctx, queryClass, Query.Trim(), Cursor);
    }
}
