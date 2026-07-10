using Svac.AimlRouter.Contracts;
using Svac.AimlRouter.Providers;
using Xunit;

namespace Svac.Tests.AimlRouter;

/// <summary>
/// ADVERSARIAL LENS — S2 pass: PII / residency + special-category, finding PII-S2-F4 (blob contents).
/// Companion to backend/tests/Svac.Tests.Architecture/PiiResidencyLensS2Tests.cs (F1-F3); this one
/// finding lives here because <see cref="AnthropicLocalTransport"/> is internal (InternalsVisibleTo).
///
///   PII-S2-F4 (MEDIUM, dev-transport content egress via argv): SLICE_S2_CONTRACT.md §1b — the payload
///       is "in-memory only, NEVER persisted by the router" and §11(P3) — "the router is the only place
///       user data leaves our trust boundary toward a model vendor ... failure unobservability" —
///       yet AnthropicLocalTransport.ExecuteAsync (AnthropicLocalTransport.cs:52-63) passes the LAST
///       USER TURN and the SYSTEM PROMPT as raw process ARGUMENTS (`-p &lt;userTurn&gt;`,
///       `--system-prompt &lt;system&gt;`). Process argv is a world-readable channel on every dev OS
///       (`ps -ef`, /proc/&lt;pid&gt;/cmdline): any other local process/user observes the full prompt
///       content for the lifetime of the CLI call — an unclassified egress with no PayloadClass
///       ceiling, no audit event, no purge story. DevSeams-only DI caps this to dev boxes, but the
///       eval lane pumps real fixture content through it today and §4 already anticipates PERSONAL-
///       class content flowing post-OQ-A. Content must ride stdin (or a 0600 temp file), never argv.
///       (The CLI's on-disk session persistence is a SEPARATE finding, S2P3, in
///       AimlPurgeCompletenessLensTests — this test pins only the argv channel.)
///
/// Test mechanics: the transport's executable is swapped for a stub shell script that dumps its argv
/// to a file and answers with a valid `claude -p --output-format json` result — deterministic, no real
/// CLI, no network, &lt;2s, per this project's gate-lane rules. RED today: the sentinel prompt text
/// appears verbatim in the dumped argv.
/// </summary>
public sealed class PiiResidencyLensS2ArgvTests
{
    [Fact]
    public async Task PiiS2F4_LocalTransport_MustNotPlacePromptOrSystemContent_OnTheProcessArgvChannel()
    {
        if (OperatingSystem.IsWindows())
        {
            // Lens stub is a /bin/sh script; the gate lane runs on unix (every repo gate is .sh).
            // CA1416-satisfying guard: the unix-only calls below are unreachable on Windows.
            return;
        }

        var workDir = Path.Combine(Path.GetTempPath(), "svac-pii-lens-argv", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var argvDump = Path.Combine(workDir, "argv.txt");
        var stubPath = Path.Combine(workDir, "claude-stub.sh");

        // Dumps every argument it received, then answers like `claude -p --output-format json`.
        var stub = "#!/bin/sh\n"
                 + $"printf '%s\\n' \"$@\" > '{argvDump}'\n"
                 + "printf '%s' '{\"is_error\":false,\"result\":\"ok\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}'\n";
        await File.WriteAllTextAsync(stubPath, stub);
        File.SetUnixFileMode(stubPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        const string userSentinel = "PII-SENTINEL-user: melanie.mustermann@example.de reported for grooming";
        const string systemSentinel = "PII-SENTINEL-system: you are triaging a minor-safety report transcript";

        var transport = new AnthropicLocalTransport(stubPath);
        var result = await transport.ExecuteAsync(
            new ProviderInvocation(
                Model: "claude-opus-4-8",
                Payload: AimlPayload.ForUserTurn(userSentinel, system: systemSentinel),
                Timeout: TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        Assert.Equal("ok", result.Output.OutputText); // precondition: the transport round-trip worked.
        Assert.True(File.Exists(argvDump), "stub never ran — transport wiring changed?");

        var argv = await File.ReadAllTextAsync(argvDump);

        // The payload is "in-memory only" (§1b). argv is not memory — it is a channel every local
        // process can read for the duration of the call. FAILS: both sentinels appear verbatim.
        Assert.DoesNotContain(userSentinel, argv, StringComparison.Ordinal);
        Assert.DoesNotContain(systemSentinel, argv, StringComparison.Ordinal);
    }
}
