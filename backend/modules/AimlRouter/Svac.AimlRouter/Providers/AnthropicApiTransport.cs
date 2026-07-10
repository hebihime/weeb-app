using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Svac.AimlRouter.Contracts;

namespace Svac.AimlRouter.Providers;

/// <summary>
/// The Anthropic Messages API transport (SLICE_S2_CONTRACT.md §1b) — THE ONLY provider-SDK reference in
/// the backend (ProviderSdkArchTest.cs). Key comes from Key Vault via S1's IFieldKeyVault/secret path
/// (SLICE_S1_CONTRACT.md §1b); this transport accepts the resolved API key string at construction so it
/// stays ignorant of WHERE the key came from — the composing host's DI wiring owns that resolution, per
/// the "one interface per vendor" seam.
///
/// Fail-closed keys (L18 analog, §1b): <see cref="AnthropicApiKeyGuard.Enforce"/> is the startup-time
/// half of this law — call it from whichever host's Program.cs first wires this transport into
/// production DI; S2 ships the guard, not a caller, because S2 has zero consumers (§0).
/// </summary>
internal sealed class AnthropicApiTransport : IModelProvider, IDisposable
{
    private readonly AnthropicClient _client;

    public AnthropicApiTransport(AnthropicApiKey apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey.Value))
        {
            throw new ArgumentException("AnthropicApiTransport requires a non-empty API key.", nameof(apiKey));
        }
        _client = new AnthropicClient(apiKey.Value);
    }

    public void Dispose() => _client.Dispose();

    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId: AnthropicProviderConstants.ProviderId,
        Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
        Transport: ProviderTransport.Api,
        CredentialRequirement: true);

    public async Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct)
    {
        var messages = (invocation.Payload.Messages ?? Array.Empty<AimlMessage>())
            .Select(m => new Message(m.Role == AimlMessageRole.User ? RoleType.User : RoleType.Assistant, m.Content))
            .ToList();

        if (messages.Count == 0)
        {
            throw new ArgumentException("AimlPayload must carry at least one message for the API transport.", nameof(invocation));
        }

        var parameters = new MessageParameters
        {
            Messages = messages,
            MaxTokens = invocation.Payload.MaxTokens ?? 1024,
            Model = invocation.Model,
            Stream = false,
            Temperature = invocation.Payload.Temperature is { } t ? (decimal)t : 1.0m,
        };
        if (!string.IsNullOrWhiteSpace(invocation.Payload.System))
        {
            parameters.System = new List<SystemMessage> { new(invocation.Payload.System) };
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(invocation.Timeout);

        MessageResponse response;
        try
        {
            response = await _client.Messages.GetClaudeMessageAsync(parameters, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Anthropic API transport exceeded its {invocation.Timeout} timeout.");
        }

        var outputText = string.Concat(response.Content.OfType<TextContent>().Select(c => c.Text));
        return new ProviderExecutionResult(
            Output: AimlPayload.ForOutput(outputText),
            InputTokens: response.Usage?.InputTokens ?? 0,
            OutputTokens: response.Usage?.OutputTokens ?? 0);
    }
}

/// <summary>
/// Typed DI wrapper around the resolved Anthropic API key (TRUST-BREAK-3, SECURITY_REVIEW_S2.md):
/// registering this AS a constructor dependency — rather than handing a bare <see cref="string"/> to a
/// singleton FACTORY lambda — lets the standard DI dependency-resolvability pass
/// (<c>ServiceProviderOptions.ValidateOnBuild</c>) catch a missing key AT BUILD. Empirically verified:
/// <c>ValidateOnBuild</c> proves constructor-DEPENDENCY resolvability only — it never invokes a factory
/// delegate, and never invokes a throwing constructor body either — so modeling "no key configured" as an
/// unresolvable typed dependency is the only mechanism that makes <c>BuildServiceProvider</c> itself fail
/// for this cause, rather than the first <see cref="IAimlRouter.InvokeAsync"/> call.
/// </summary>
internal sealed record AnthropicApiKey(string Value);

/// <summary>
/// Fail-closed prod boot throw for the API transport (SLICE_S2_CONTRACT.md §1b): "in prod, if the
/// resolved policy can reach the `anthropic` API transport and no key is configured (Key Vault ref,
/// 2A), the host throws at startup." Same shape as S1's IPaymentService prod-throw
/// (ProdFieldKeyVaultGuard) — a static, host-agnostic guard ready for whichever host first wires
/// AimlRouter into production DI (no consumer exists at S2, so nothing calls this yet).
///
/// TRUST-BREAK-2 (SECURITY_REVIEW_S2.md, Trust-F1 parity): the guard takes the environment NAME and
/// allowlists exactly "Development" — the SAME law <c>ProdFieldKeyVaultGuard.Enforce</c> already applies
/// (SECURITY_REVIEW_S1.md Trust-F1): "fail-closed means ALLOWLIST the one safe environment (Development),
/// never BLOCKLIST / never trust a flag." A caller that only ever has a bare <c>devSeamsEnabled</c>
/// boolean cannot satisfy this law — it cannot see which environment it is actually running in.
/// </summary>
public static class AnthropicApiKeyGuard
{
    /// <param name="policyCanReachApiTransport">True whenever any lawful chain member can resolve to the API transport.</param>
    /// <param name="environmentName">The hosting environment's EnvironmentName (e.g. ASPNETCORE_ENVIRONMENT) — never a bare DevSeams flag.</param>
    /// <param name="apiKeyConfigured">True once a real Key Vault-backed key is resolved.</param>
    public static void Enforce(bool policyCanReachApiTransport, string environmentName, bool apiKeyConfigured)
    {
        var isDevelopment = string.Equals(environmentName, Microsoft.Extensions.Hosting.Environments.Development, StringComparison.OrdinalIgnoreCase);
        if (isDevelopment)
        {
            return; // Development-only DI resolves the LocalProcess transport instead; the API transport is never reachable in practice.
        }

        if (policyCanReachApiTransport && !apiKeyConfigured)
        {
            throw new InvalidOperationException(
                $"aiml.routing_policy can reach the anthropic API transport in environment \"{environmentName}\" " +
                "but no Key Vault-backed API key is configured (SLICE_S2_CONTRACT.md §1b, L18 fail-closed " +
                "analog) — configure the key before deploying to any environment other than Development.");
        }
    }
}
