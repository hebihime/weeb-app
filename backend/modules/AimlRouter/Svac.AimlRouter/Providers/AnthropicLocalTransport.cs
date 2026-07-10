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
///
/// PII-S2-F4/S2P3 (SECURITY_REVIEW_S2.md): neither the prompt nor the system content ever rides process
/// argv (`ps -ef`/`/proc/&lt;pid&gt;/cmdline` is a world-readable channel on every dev OS) — the user turn
/// rides stdin, the system prompt rides a 0600 file under this call's own isolated scratch root.
///
/// Session persistence — the actual mechanism, not merely a side effect of the isolation below — is
/// killed by the CLI's own <c>--no-session-persistence</c> flag: "sessions will not be saved to disk and
/// cannot be resumed." That flag alone satisfies §1b ("the router holds content for the duration of the
/// call and not one second longer") without touching the CLI's config/auth resolution at all. The scratch
/// root is ADDITIONALLY set as CLAUDE_CONFIG_DIR as defense-in-depth against any other cache/lock file the
/// CLI might otherwise leave under the real, persistent <c>~/.claude</c> — deleted once the call ends, so
/// anything that does land there dies with the call regardless. Verified empirically while building this
/// fix: redirecting CLAUDE_CONFIG_DIR to a genuinely empty directory moves where the CLI looks for its own
/// config/credential file (normally <c>$HOME/.claude.json</c>, NOT inside <c>$HOME/.claude/</c>) and an
/// OAuth/keychain-authenticated session does not carry over to a fresh one — <c>ANTHROPIC_API_KEY</c>
/// (an env var, unaffected by this override, since <see cref="ProcessStartInfo.Environment"/> only adds
/// one key on top of the inherited set) is the auth path this isolation is compatible with; a developer
/// running the eval lane under an OAuth-only login sets that env var for the duration of the eval run.
/// This is a disclosed limitation of the eval lane only — the gate lane never touches a real CLI
/// (SeedProvider), and no production host reaches this transport at all ([DevSeamsOnly], arch-tested).
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

        var scratchDir = Path.Combine(Path.GetTempPath(), "svac-aiml-local-transport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _executablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // S2P3: isolate the CLI's own on-disk cache/lock root to this call's scratch dir, deleted in
            // the `finally` below — defense-in-depth alongside --no-session-persistence below, never the
            // developer's real, persistent ~/.claude.
            psi.Environment["CLAUDE_CONFIG_DIR"] = scratchDir;

            psi.ArgumentList.Add("-p"); // print mode; the prompt itself rides STDIN below, never argv (PII-S2-F4).
            psi.ArgumentList.Add("--output-format");
            psi.ArgumentList.Add("json");
            // S2P3: the actual persistence-killing mechanism (see the type's own doc comment) — the CLI's
            // own flag, auth-path-independent, verified to leave zero session file behind.
            psi.ArgumentList.Add("--no-session-persistence");
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(invocation.Model);
            if (!string.IsNullOrWhiteSpace(invocation.Payload.System))
            {
                // PII-S2-F4: the system prompt rides a 0600 file under the isolated scratch root, never a
                // literal argv value — only the FILE PATH (never its content) is ever visible on argv.
                var systemPromptFile = Path.Combine(scratchDir, "system-prompt.txt");
                await File.WriteAllTextAsync(systemPromptFile, invocation.Payload.System, ct);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(systemPromptFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                psi.ArgumentList.Add("--system-prompt-file");
                psi.ArgumentList.Add(systemPromptFile);
            }

            using var process = new Process { StartInfo = psi };
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(invocation.Timeout);

            try
            {
                process.Start();
                var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

                // PII-S2-F4: the user turn (the last-mile prompt content) rides stdin — never a positional
                // argv value — mirroring `echo "$prompt" | claude -p ...`'s own standard non-interactive
                // usage. Closing stdin sends EOF, telling the CLI the prompt is complete.
                await process.StandardInput.WriteAsync(userTurn);
                process.StandardInput.Close();

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
        finally
        {
            // S2P3: any CLI-side derivative (session history, system-prompt file) dies with the call.
            try
            {
                Directory.Delete(scratchDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // best-effort cleanup; a locked/already-removed scratch dir must never fail the call itself.
            }
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
