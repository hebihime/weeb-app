using System.Text.Json.Serialization;

namespace Svac.AimlRouter.Contracts;

/// <summary>
/// Egress classification of the data leaving our trust boundary toward a model vendor (SLICE_S2_
/// CONTRACT.md §1b/§12.5). REQUIRED on every <see cref="AimlRequest"/> — <see cref="AimlRequest"/>'s
/// positional constructor parameter carries no default value, so a caller must state a class
/// explicitly; there is deliberately no "safe" implicit zero value a lazy call site could fall through
/// to. Ordered least-to-most sensitive; a provider's allowlist ceiling (9A `aiml.provider_allowlist`)
/// is compared against this ordinal.
///
/// String-serialized (not the numeric default): the 9A `aiml.provider_allowlist` manifest (§4) writes
/// `"payload_class_ceiling": "Pseudonymous"`, a founder-readable value on the ops desk (S5) — a bare `1`
/// would be unreviewable in a config-change audit event.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayloadClass
{
    NonPersonal,
    Pseudonymous,
    Personal,
    SpecialCategory,
}
