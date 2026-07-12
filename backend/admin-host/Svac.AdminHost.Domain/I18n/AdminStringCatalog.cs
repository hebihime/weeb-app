using System.Text.Json;
using System.Text.Json.Serialization;

namespace Svac.AdminHost.Domain.I18n;

/// <summary>
/// The admin host's keyed-string catalog (SLICE_S5_CONTRACT.md §8 seam 14: "ALL admin strings keyed from
/// commit one"; §11 OQ-1 RATIFIED (a): EN-only, staff-only audience). Every Razor page renders through
/// this catalog — never a literal — so <c>tools/i18n-lint/i18n-lint.mjs</c>'s razor-platform hardcoded-
/// string tripwire (already anticipating "backend admin Razor" in its own doc comment) has zero findings
/// against this host from commit one, and flipping to the product's x4 locale set later is translation
/// data dropped into this same shape, never a rewrite.
/// </summary>
public sealed class AdminStringCatalog
{
    private readonly Dictionary<string, string> _entries;

    public AdminStringCatalog()
        : this(ResolveDefaultCatalogPath())
    {
    }

    public AdminStringCatalog(string catalogPath)
    {
        var json = File.ReadAllText(catalogPath);
        var file = JsonSerializer.Deserialize<CatalogFile>(json)
            ?? throw new InvalidOperationException($"admin string catalog \"{catalogPath}\" deserialized to null.");
        _entries = file.Entries;
    }

    /// <summary>Looks up a keyed string; throws (never silently falls back to the key itself) so a typo is a build-time-visible bug, not a leaked raw key on a live page.</summary>
    public string this[string key] => _entries.TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException($"admin string key \"{key}\" is not registered in the catalog — add it to Svac.AdminHost.Domain/I18n/admin-en.json.");

    private static string ResolveDefaultCatalogPath() =>
        Path.Combine(AppContext.BaseDirectory, "I18n", "admin-en.json");

    private sealed record CatalogFile([property: JsonPropertyName("entries")] Dictionary<string, string> Entries);
}
