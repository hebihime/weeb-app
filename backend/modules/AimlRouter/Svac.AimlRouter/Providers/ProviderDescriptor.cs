using Svac.AimlRouter.Contracts;

namespace Svac.AimlRouter.Providers;

/// <summary>Static self-description every <see cref="IModelProvider"/> carries (SLICE_S2_CONTRACT.md §1b).</summary>
public sealed record ProviderDescriptor(
    string ProviderId,
    IReadOnlyList<AimlTaskKind> Capabilities,
    ProviderTransport Transport,
    bool CredentialRequirement);

/// <summary>One provider invocation's neutral request shape, handed to <see cref="IModelProvider.ExecuteAsync"/>.</summary>
public sealed record ProviderInvocation(string Model, AimlPayload Payload, TimeSpan Timeout);

/// <summary>
/// One provider invocation's result, INCLUDING token counts (SLICE_S2_CONTRACT.md §1b: RoutingReceipt
/// carries InputTokens/OutputTokens) — kept separate from the bare <see cref="AimlPayload"/> output so a
/// transport never needs to smuggle telemetry through the neutral envelope's own fields.
/// </summary>
public sealed record ProviderExecutionResult(AimlPayload Output, int InputTokens, int OutputTokens);
