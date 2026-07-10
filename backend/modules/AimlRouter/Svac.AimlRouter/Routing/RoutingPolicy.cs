using System.Text.Json.Serialization;
using Svac.AimlRouter.Contracts;

namespace Svac.AimlRouter.Routing;

/// <summary>The 9A `aiml.provider_allowlist` entry shape (SLICE_S2_CONTRACT.md §4). Founder-scoped; a
/// `Set` naming an undeclared model or a ceiling the resolver would violate fails bounds validation
/// (§4) — enforced where the config registry validates, not here (this is the deserialized shape only).</summary>
public sealed record ProviderAllowlistEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("kinds")] IReadOnlyList<string> Kinds,
    [property: JsonPropertyName("payload_class_ceiling")] PayloadClass PayloadClassCeiling,
    [property: JsonPropertyName("dpa_signed")] bool DpaSigned,
    [property: JsonPropertyName("special_category_ok")] bool SpecialCategoryOk,
    [property: JsonPropertyName("residency")] string Residency,
    [property: JsonPropertyName("models")] IReadOnlyList<string> Models);

/// <summary>One (provider, model) hop as it appears in a chain — the routing-policy shape, before resolution.</summary>
public sealed record TaskChainLink(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model")] string Model);

/// <summary>The 9A `aiml.routing_policy` shape (SLICE_S2_CONTRACT.md §4). Ops-scoped, desk-tunable per 15A.</summary>
public sealed record RoutingPolicy(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("default_chain")] IReadOnlyList<TaskChainLink> DefaultChain,
    [property: JsonPropertyName("task_chains")] IReadOnlyDictionary<string, IReadOnlyList<TaskChainLink>> TaskChains,
    [property: JsonPropertyName("residency_overrides")] IReadOnlyList<string> ResidencyOverrides);
