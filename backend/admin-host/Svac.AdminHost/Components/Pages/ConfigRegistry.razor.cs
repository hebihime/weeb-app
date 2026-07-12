using Microsoft.AspNetCore.Components;
using Svac.AdminHost.ConfigRegistry;
using Svac.DomainCore.Contracts.Config;

namespace Svac.AdminHost.Components.Pages;

/// <summary>
/// Code-behind for ConfigRegistry.razor (SLICE_S5_CONTRACT.md §8 seam 1, Pass C) — a PARTIAL CLASS file
/// for the same reason StaffRoles.razor.cs is one (its own doc comment): <c>tools/i18n-lint/i18n-lint.
/// mjs</c>'s hardcoded-literal tripwire only scans <c>*.razor</c> markup, never <c>.razor.cs</c>.
///
/// <see cref="IConfigRegistry"/> is injected DIRECTLY here (never via an
/// <c>IDbContextFactory</c> workaround like StaffRoles.razor.cs's own AdminDbContext read) because it is
/// backed by <c>CoreDbContext</c>, not <c>AdminDbContext</c> — <see cref="Layout.AdminLayout"/>'s own
/// <c>OnInitializedAsync</c> only ever touches <c>AdminDbContext</c> (via <c>IStaffRoleResolver</c>), so
/// there is no shared, not-thread-safe <c>DbContext</c> for this page's own read to race with.
/// </summary>
public sealed partial class ConfigRegistry
{
    [Inject] private IConfigRegistry Registry { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "notice")]
    private string? Notice { get; set; }

    [SupplyParameterFromQuery(Name = "errorKind")]
    private string? ErrorKind { get; set; }

    [SupplyParameterFromQuery(Name = "detail")]
    private string? Detail { get; set; }

    private IReadOnlyList<ConfigEntryView> _rows = Array.Empty<ConfigEntryView>();
    private IReadOnlyDictionary<string, string> _pending = new Dictionary<string, string>();

    /// <summary>§4: "the exclusion list renders on the desk, not silently." The THREE keys SLICE_S5_
    /// CONTRACT.md §4 names as deliberately NOT seeded (nakama_rep_floor, ε-budget-per-principal, annual
    /// Premium) — static data, never derived from the manifest (there is nothing to derive an ABSENCE
    /// from), and never rendered as a raw literal in the .razor markup (every label + reason routes
    /// through <see cref="Domain.I18n.AdminStringCatalog"/>, keyed here, looked up there).</summary>
    private static readonly IReadOnlyList<ExcludedKeyRow> ExcludedKeys = new[]
    {
        new ExcludedKeyRow("nakama_rep_floor", "admin.config.excluded.nakama_rep_floor.reason"),
        new ExcludedKeyRow("ε-budget-per-principal", "admin.config.excluded.epsilon_budget.reason"),
        new ExcludedKeyRow("annual Premium", "admin.config.excluded.annual_premium.reason"),
    };

    protected override async Task OnInitializedAsync()
    {
        var entries = await Registry.ListEntries();
        _rows = entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
        _pending = ConfigManifestPendingSliceIndex.Load();
    }

    private sealed record ExcludedKeyRow(string KeyLabel, string ReasonKey);
}
