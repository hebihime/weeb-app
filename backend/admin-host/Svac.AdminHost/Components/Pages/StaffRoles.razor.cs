using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Svac.AdminHost.Domain.Persistence;
using Svac.AdminHost.Domain.Policy;
using Svac.DomainCore.Contracts;
using Svac.DomainCore.Contracts.Ids;
using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Components.Pages;

/// <summary>
/// Code-behind for StaffRoles.razor (SLICE_S5_CONTRACT.md §8 seam 1, Pass B). Deliberately a PARTIAL
/// CLASS file, not an inline <c>@code</c> block: <c>tools/i18n-lint/i18n-lint.mjs</c>'s hardcoded-literal
/// tripwire (rule 2) scans <c>*.razor</c> markup text with a generic "&gt;text&lt;" regex that cannot
/// distinguish HTML tags from C# generic-type angle brackets — a `.razor.cs` code-behind file is never a
/// `.razor` file, so it is structurally outside that scan's reach, the standard escape valve for
/// non-trivial component logic in Blazor.
///
/// Uses <see cref="IDbContextFactory{AdminDbContext}"/> to create a FRESH, independent
/// <see cref="AdminDbContext"/> for this page's own read, rather than injecting the ambient
/// request-scoped instance directly: <see cref="Svac.AdminHost.Components.Layout.AdminLayout"/>'s own
/// <c>OnInitializedAsync</c> ALSO reads staff-role data (via <c>IStaffRoleResolver</c>, backed by that
/// SAME scoped context) for nav filtering, and Blazor's static-SSR renderer does not serialize a layout's
/// and its body's <c>OnInitializedAsync</c> calls strongly enough to rule out both touching one shared,
/// not-thread-safe <see cref="Microsoft.EntityFrameworkCore.DbContext"/> at the same time — verified live:
/// sharing the scoped instance here throws EF Core's own <c>ConcurrencyDetector</c> exception under a real
/// HTTP round trip. A fresh, request-independent context sidesteps the hazard entirely (Microsoft's own
/// documented pattern for Blazor + EF Core) without touching AdminLayout.razor or
/// GrantTableStaffRoleResolver (Pass A's files) — <see cref="Svac.DomainCore.Contracts.Policy.
/// IStaffRoleResolver"/> is not used here at all; this page derives its OWN SuperAdmin-membership check
/// from the SAME roster query it already runs for rendering, one fewer DB round trip, zero shared state.
/// </summary>
public sealed partial class StaffRoles
{
    [Inject] private IDbContextFactory<AdminDbContext> AdminDbFactory { get; set; } = null!;
    [Inject] private IRequestContextAccessor RequestContextAccessor { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "notice")]
    private string? Notice { get; set; }

    [SupplyParameterFromQuery(Name = "error")]
    private string? Error { get; set; }

    [SupplyParameterFromQuery(Name = "detail")]
    private string? Detail { get; set; }

    private static readonly IReadOnlyList<string> AllRoleCodes = Enum.GetValues<StaffRole>().Select(StaffRoleCodes.ToCode).ToList();
    private static readonly string SuperAdminCode = StaffRoleCodes.ToCode(StaffRole.SuperAdmin);

    private bool _canView;
    private IReadOnlyList<StaffRow> _staff = Array.Empty<StaffRow>();

    protected override async Task OnInitializedAsync()
    {
        var ctx = RequestContextAccessor.Current;
        if (ctx.Actor.Kind != ActorKind.Staff)
        {
            _canView = false;
            return;
        }

        await using var db = await AdminDbFactory.CreateDbContextAsync();

        var accounts = await db.StaffAccounts.OrderBy(a => a.Email).ToListAsync();
        var activeGrantsByStaffId = (await db.StaffRoleGrants.Where(g => g.RevokedAt == null).ToListAsync())
            .GroupBy(g => g.StaffId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Role).OrderBy(r => r, StringComparer.Ordinal).ToList());

        var callerStaffId = ctx.Actor.Id.ToString();
        _canView = activeGrantsByStaffId.TryGetValue(callerStaffId, out var callerRoleCodes) && callerRoleCodes.Contains(SuperAdminCode);
        if (!_canView)
        {
            return;
        }

        _staff = accounts
            .Select(a => new StaffRow(
                a.Id, a.ExternalSubject, a.Email, a.DisplayName, a.Status, a.Region,
                activeGrantsByStaffId.TryGetValue(a.Id, out var roleCodes) ? roleCodes : Array.Empty<string>()))
            .ToList();
    }

    private sealed record StaffRow(string Id, string ExternalSubject, string Email, string DisplayName, string Status, string Region, IReadOnlyList<string> ActiveGrantRoleCodes);
}
