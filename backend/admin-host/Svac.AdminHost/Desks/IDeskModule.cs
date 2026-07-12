using Svac.DomainCore.Contracts.Policy;

namespace Svac.AdminHost.Desks;

/// <summary>
/// The desk-registration seam (SLICE_S5_CONTRACT.md §8 seam 1): "the nav renders ONLY registered desks,
/// role-filtered; unregistered = ABSENT." S5's own surfaces are the first registrants (§0 governing
/// thesis P2); every later desk slice (S8/S10/S12/S15/S18/S25/S29/S30/S33) adds itself with an additive
/// file + one DI registration (<c>services.AddSingleton&lt;IDeskModule, MyDesk&gt;()</c>) and ZERO edits
/// to this file or to <c>AdminNav</c>.
/// </summary>
public interface IDeskModule
{
    /// <summary>Stable identity for the desk (log lines, test fixtures) — never rendered.</summary>
    public string DeskId { get; }

    /// <summary>Keyed string (AdminStringCatalog) for the nav label — never a literal (i18n-lint).</summary>
    public string TitleKey { get; }

    /// <summary>Lower sorts first; ties broken by DeskId (ordinal) for determinism.</summary>
    public int NavOrder { get; }

    /// <summary>Which staff roles ever see this desk in the nav. The Role axis on the desk's own policy
    /// row is the REAL access gate — this is the nav-rendering mirror of that same allowlist (absence,
    /// never a grayed-out affordance, for a role that cannot see it).</summary>
    public IReadOnlySet<StaffRole> VisibleTo { get; }

    /// <summary>The Razor page component this desk routes to — contract-literal name (SLICE_S5_CONTRACT.md
    /// §8 seam 1: "RootComponent"). <see cref="RouteHref"/> is the nav's own href, kept alongside rather
    /// than derived from this Type at render time (static SSR renders no live route table to reflect
    /// on before the first request completes routing) — the SAME two facts a `[Route]`/`@page` component
    /// already declares, named twice on purpose so a desk registration and its own page can never drift.</summary>
    public Type RootComponent { get; }

    /// <summary>The absolute path <see cref="RootComponent"/> is mapped at (its own <c>@page</c> directive, restated here for nav rendering).</summary>
    public string RouteHref { get; }
}
