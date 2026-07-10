using System.Text.Json.Serialization;

namespace Svac.PublicApi;

/// <summary>
/// Deserialization shape of i18n/locales.json (SLICE_S1_CONTRACT.md §1c: "sourced from i18n/
/// locales.json at boot"). Explicit JsonPropertyName pins the exact lowercase JSON keys — System.Text.
/// Json's default is CASE-SENSITIVE property matching, so relying on PascalCase-to-lowercase inference
/// silently deserializes both fields to null instead of throwing, a real bug caught by the compose
/// fresh-boot smoke test (GET /v1/client-config returned locales:null before this fix).
/// </summary>
public sealed record LocalesFile(
    [property: JsonPropertyName("locales")] string[] Locales,
    [property: JsonPropertyName("default")] string Default);
