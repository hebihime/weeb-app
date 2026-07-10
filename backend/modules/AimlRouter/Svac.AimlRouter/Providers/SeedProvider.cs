using Svac.AimlRouter.Contracts;
using Svac.DomainCore.Contracts;

namespace Svac.AimlRouter.Providers;

/// <summary>
/// Deterministic canned responses; TEST DI ONLY (SLICE_S2_CONTRACT.md §1b) — "NEVER an allowlist or
/// policy value (S1 DevSeams-not-in-9A ruling)". Tagged <see cref="DevSeamsOnlyAttribute"/> so it rides
/// the same never-in-prod-DI arch-test family every other fake backend in this codebase does
/// (IPaymentService's DevSeamsPaymentService, IFieldKeyVault's DevKeyringFieldKeyVault, ...): a
/// runtime-tunable that could swap this in from the ops desk must be structurally impossible.
///
/// Deterministic by construction: the reply is a pure function of the LAST user message's content, no
/// wall-clock read, no randomness — gate-lane golden vectors depend on that (&lt;2s, no network, no key,
/// no spend).
/// </summary>
[DevSeamsOnly]
internal sealed class SeedProvider : IModelProvider
{
    public const string ProviderId = "seed";

    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId: ProviderId,
        Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
        Transport: ProviderTransport.LocalProcess,
        CredentialRequirement: false);

    public Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct)
    {
        var lastUserText = invocation.Payload.Messages?.LastOrDefault(m => m.Role == AimlMessageRole.User)?.Content ?? string.Empty;
        var canned = $"seed-echo:{lastUserText}";
        var result = new ProviderExecutionResult(
            Output: AimlPayload.ForOutput(canned),
            InputTokens: EstimateTokens(lastUserText),
            OutputTokens: EstimateTokens(canned));
        return Task.FromResult(result);
    }

    /// <summary>A crude, deterministic token estimate (chars/4) — good enough for gate-lane budget-path exercises; never used for real billing.</summary>
    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);
}
