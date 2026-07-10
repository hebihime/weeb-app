using System.Text.Json;
using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Xunit;

namespace Svac.AimlRouter.Evals;

/// <summary>
/// The periodic lane's first real member (SLICE_S2_CONTRACT.md §10.4): "this harness is the reusable
/// one every later latent surface runs on" — S12/S13 evals become fixture files + thresholds against
/// THIS harness, not new ones. Runs via the LOCAL Claude Code CLI transport: no API key, no Key Vault
/// dependency, billed (if at all) to whatever the CLI's own authenticated session is — never a router
/// secret. Tagged <c>Category=Eval</c> so a trait filter (`--filter Category=Eval`, the periodic/nightly
/// job) is what runs this, never the gate lane — <see cref="Svac.Tests.AimlRouter"/>'s gate tests use
/// <see cref="SeedProvider"/> instead, exactly so they stay free, deterministic, and &lt;2s.
///
/// Scaffold-phase scope (SLICE_PLAYBOOK.md Phase 1): proves the harness shape end to end against ONE
/// checked-in canary fixture. Build phase (Phase 2) extends <c>canary-prompts.json</c> to the full N=20
/// set and adds the ≥95% structured-output-schema-conformance threshold, the refusal-honesty probe, and
/// the failover-under-real-backend drill §10.4 names as sign-off evidence — none of which exist yet.
/// </summary>
public sealed class EvalProbeConformanceTests
{
    [Fact]
    [Trait("Category", "Eval")]
    public async Task LocalTransport_AnswersCanaryPrompt_WithExpectedSubstring()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "canary-prompts.json");
        if (!File.Exists(fixturePath))
        {
            Assert.Fail($"eval fixture not found at {fixturePath} — this project must ship its own copy under evals/fixtures/.");
        }

        var doc = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
        var first = doc.RootElement.GetProperty("prompts")[0];
        var systemPrompt = first.GetProperty("system").GetString()!;
        var userText = first.GetProperty("userText").GetString()!;
        var expectedSubstring = first.GetProperty("expectedSubstring").GetString()!;

        var transport = new AnthropicLocalTransport();
        var payload = AimlPayload.ForUserTurn(userText, system: systemPrompt, maxTokens: 32, temperature: 0.0);
        var invocation = new ProviderInvocation(Model: "claude-opus-4-8", Payload: payload, Timeout: TimeSpan.FromSeconds(60));

        ProviderExecutionResult result;
        try
        {
            result = await transport.ExecuteAsync(invocation, CancellationToken.None);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Fail(
                "the local Claude Code CLI ('claude' on PATH) is required for this eval and was not found — " +
                "install it or run this suite on a dev box that has it (SLICE_S2_CONTRACT.md §9: 'evals " +
                "require the CLI and say so in their skip message').");
            return;
        }

        Assert.Contains(expectedSubstring, result.Output.OutputText, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.OutputTokens > 0, "expected the local transport to report a non-zero output token count.");
    }
}
