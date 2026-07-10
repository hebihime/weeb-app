using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Svac.AimlRouter.Contracts;
using Svac.DomainCore.Contracts;

namespace Svac.AimlRouter.Providers;

/// <summary>
/// Local Claude Code CLI transport (SLICE_S2_CONTRACT.md §1b): "no key, no per-call cost (15A)."
/// Development-only DI, tagged <see cref="DevSeamsOnlyAttribute"/> and arch-tested never-in-prod (the
/// IPaymentService family's pattern) — a desk edit can never route production traffic to a keyless
/// local process because this transport is never itself a 9A allowlist value (§12.2), only a
/// DI-selected implementation of <c>anthropic</c>'s <see cref="ProviderTransport.LocalProcess"/> leg.
///
/// Shells out to the `claude` CLI's non-interactive print mode (`-p --output-format json`), which this
/// module's evals run against directly (backend/modules/AimlRouter/evals/): the eval harness's own
/// prerequisite IS this transport working end to end against a real local install.
/// </summary>
[DevSeamsOnly]
internal sealed class AnthropicLocalTransport : IModelProvider
{
    private readonly string _executablePath;

    public AnthropicLocalTransport(string executablePath = "claude")
    {
        _executablePath = executablePath;
    }

    public ProviderDescriptor Descriptor { get; } = new(
        ProviderId: AnthropicProviderConstants.ProviderId,
        Capabilities: new[] { AimlTaskKind.Generate, AimlTaskKind.ModerateText, AimlTaskKind.Translate, AimlTaskKind.EvalProbe },
        Transport: ProviderTransport.LocalProcess,
        CredentialRequirement: false);

    public async Task<ProviderExecutionResult> ExecuteAsync(ProviderInvocation invocation, CancellationToken ct)
    {
        var userTurn = LastUserTurn(invocation.Payload);

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(userTurn);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(invocation.Model);
        if (!string.IsNullOrWhiteSpace(invocation.Payload.System))
        {
            psi.ArgumentList.Add("--system-prompt");
            psi.ArgumentList.Add(invocation.Payload.System);
        }

        using var process = new Process { StartInfo = psi };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(invocation.Timeout);

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"local Claude Code CLI transport exited {process.ExitCode}: {stderr}");
            }

            var parsed = JsonSerializer.Deserialize<ClaudeCliResult>(stdout)
                ?? throw new InvalidOperationException("local Claude Code CLI transport returned an empty/unparseable result.");

            if (parsed.IsError)
            {
                throw new InvalidOperationException($"local Claude Code CLI transport reported an error result: {parsed.Result}");
            }

            return new ProviderExecutionResult(
                Output: AimlPayload.ForOutput(parsed.Result ?? string.Empty),
                InputTokens: parsed.Usage?.InputTokens ?? 0,
                OutputTokens: parsed.Usage?.OutputTokens ?? 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested == false)
        {
            // Our own CancelAfter fired, not the caller's token — this IS the Timeout failure kind
            // (AimlRouterService maps this exception type to AimlFailure.Timeout).
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
            }
            throw new TimeoutException($"local Claude Code CLI transport exceeded its {invocation.Timeout} timeout.");
        }
    }

    private static string LastUserTurn(AimlPayload payload) =>
        payload.Messages?.LastOrDefault(m => m.Role == AimlMessageRole.User)?.Content
        ?? throw new ArgumentException("AimlPayload must carry at least one User message for the local transport.", nameof(payload));

    /// <summary>Shape of `claude -p --output-format json`'s stdout — verified against a real local invocation while building this module.</summary>
    private sealed record ClaudeCliResult(
        [property: JsonPropertyName("is_error")] bool IsError,
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("usage")] ClaudeCliUsage? Usage);

    private sealed record ClaudeCliUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
        [property: JsonPropertyName("output_tokens")] int OutputTokens);
}

/// <summary>Shared constants between the `anthropic` provider's two transports.</summary>
internal static class AnthropicProviderConstants
{
    public const string ProviderId = "anthropic";
}
