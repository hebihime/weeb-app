using Microsoft.AspNetCore.Components;
using Svac.AdminHost.Domain.Tiles;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Components.Pages;

/// <summary>
/// Code-behind for Dashboard.razor (SLICE_S5_CONTRACT.md §8 seam 1/2, Pass D) — a PARTIAL CLASS file for
/// the same i18n-lint escape-valve reason StaffRoles.razor.cs/ConfigRegistry.razor.cs's own doc comments
/// give.
///
/// Gated by a DIRECT <see cref="IPolicyEngine.Authorize"/> call on <c>admin.dashboard.read</c> — never
/// routed through <see cref="Svac.AdminHost.Domain.Execution.IAdminActionExecutor"/> like User
/// Search/Audit Trail: a dashboard VIEW is not itself an audited staff action (§0's contract table
/// carries no per-view audit requirement for <c>admin.dashboard.read</c>, unlike
/// <c>admin.user_search.execute</c>/<c>admin.audit.read</c>), so this page uses the SAME lighter-weight
/// gate <see cref="Svac.DomainCore.Policy.PolicyEngine"/> itself is built from — a pure decision function,
/// never a mutating call, so nothing here needs the executor's transactional machinery. Every staff actor
/// holding AT LEAST ONE of the six roles passes (§3: "admin.dashboard.read ... all six roles — Analyst's
/// whole scope"); a staff row with ZERO active grants is denied even this page, same as every other desk.
/// </summary>
public sealed partial class Dashboard
{
    [Inject] private IPolicyEngine PolicyEngine { get; set; } = null!;
    [Inject] private IRequestContextAccessor RequestContextAccessor { get; set; } = null!;
    [Inject] private IStaffRoleResolver StaffRoleResolver { get; set; } = null!;
    [Inject] private IEnumerable<IMetricsTileSource> TileSources { get; set; } = null!;

    private bool _canView;
    private IReadOnlyList<RenderedTile> _tiles = Array.Empty<RenderedTile>();

    protected override async Task OnInitializedAsync()
    {
        var ctx = RequestContextAccessor.Current;
        if (ctx.Actor.Kind != ActorKind.Staff)
        {
            _canView = false;
            return;
        }

        var decision = await PolicyEngine.Authorize(ctx.Actor, "admin.dashboard.read", TargetRef.ForAction("admin.dashboard.read"));
        if (!decision.IsAllowed)
        {
            _canView = false;
            return;
        }

        _canView = true;

        var heldRoles = await StaffRoleResolver.GrantsOf(ctx.Actor);
        var visible = TileSources.Where(t => t.VisibleTo.Overlaps(heldRoles)).ToList();

        var rendered = new List<RenderedTile>(visible.Count);
        foreach (var source in visible)
        {
            // Sequential, not Task.WhenAll: several sources share the SAME scoped CoreDbContext/
            // AdminDbContext (EF Core is not safe for concurrent use on one context, exactly the hazard
            // AddAdminHostModule's own IDbContextFactory doc comment names for StaffRoles.razor) — a
            // handful of tiles at this slice's data volume makes sequential negligible.
            var result = await source.Query();
            rendered.Add(new RenderedTile(source.TileId, source.TitleKey, result));
        }
        _tiles = rendered;
    }

    private sealed record RenderedTile(string TileId, string TitleKey, MetricsTileResult Result);
}
