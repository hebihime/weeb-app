using System.Text.Json;
using System.Text.Json.Serialization;

namespace Svac.DomainCore.Contracts.Ids;

/// <summary>
/// Serializes OpaqueId as a bare string ("usr_01H...") — never {"prefix":"usr","value":"usr_01H..."} —
/// so the OpenAPI schema is `{"type":"string"}` (SLICE_S1_CONTRACT.md §1c: "OpaqueId (string format)"),
/// matching every future endpoint's actual wire shape.
/// </summary>
public sealed class OpaqueIdJsonConverter : JsonConverter<OpaqueId>
{
    public override OpaqueId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? throw new JsonException("OpaqueId cannot be null.");
        return OpaqueId.Parse(raw);
    }

    public override void Write(Utf8JsonWriter writer, OpaqueId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
