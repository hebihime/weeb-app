using System.Text.Json.Serialization;

namespace Svac.AimlRouter.Audit;

/// <summary>
/// The ONE audit event every <c>InvokeAsync</c> appends (SLICE_S2_CONTRACT.md §1b): "Metadata only —
/// prompt and completion text NEVER appear in any event payload, enforced by a closed C# record type +
/// a serialized-shape test." This type IS that enforcement: it has no field capable of carrying prompt
/// or completion text (no <c>AimlPayload</c>/message field exists on this record at all), so no future
/// edit can smuggle content in without changing this file in a way a shape test catches.
/// </summary>
public sealed record AimlRouteDecidedEvent(
    [property: JsonPropertyName("invocation_id")] string InvocationId,
    [property: JsonPropertyName("caller")] string Caller,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("payload_class")] string PayloadClass,
    [property: JsonPropertyName("subject_ref")] string? SubjectRef,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("transport")] string? Transport,
    [property: JsonPropertyName("decision_source")] string? DecisionSource,
    [property: JsonPropertyName("policy_version")] int PolicyVersion,
    [property: JsonPropertyName("failover_from")] string? FailoverFrom,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("latency_ms")] long LatencyMs,
    [property: JsonPropertyName("input_tokens")] int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens,
    [property: JsonPropertyName("payload_sha256")] string PayloadSha256);
