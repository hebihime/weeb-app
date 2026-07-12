using System.Text.Json;

namespace Svac.Identity.Export;

/// <summary>Shared serialization shape for every export contributor + the manifest (SLICE_S3_CONTRACT.md §6b) — one JSON convention across the whole artifact.</summary>
internal static class ExportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Parses a stored event payload back into an embeddable JSON node (never a double-escaped string). Null in, null out.</summary>
    public static JsonElement? ParsePayload(string? payloadJson) =>
        payloadJson is null ? null : JsonSerializer.Deserialize<JsonElement>(payloadJson);
}
