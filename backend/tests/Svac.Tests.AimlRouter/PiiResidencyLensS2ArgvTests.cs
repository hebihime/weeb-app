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

        // Drains stdin first (a real `claude -p` reads the prompt from stdin to EOF — the double must
        // model that, else it can exit before the transport finishes writing and race a broken pipe;
        // exactly the CI flake fixed alongside this test), dumps its argv, then answers like
        // `claude -p --output-format json`.
        var stub = "#!/bin/sh\n"
                 + "cat >/dev/null\n"
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

    // A prompt larger than the OS pipe buffer (16-64KB on macOS/Linux) — WriteAsync CANNOT complete
    // until the child drains stdin, so a child that exits without reading guarantees a broken pipe on
    // the write. This is the CI flake made deterministic and local (a short prompt fits the buffer and
    // hides the race). Both tests below run the transport, never a real CLI, <2s, gate-lane clean.
    private const int BeyondPipeBuffer = 256 * 1024;

    [Fact]
    public async Task LocalTransport_ChildExitsWithoutDrainingStdin_MustNotSurfaceABrokenPipe()
    {
        if (OperatingSystem.IsWindows()) { return; } // stub is /bin/sh; gate lane is unix.

        // Answers with a valid result and exits IMMEDIATELY — never reads stdin. Pre-fix: the transport's
        // stdin write races the child's exit and throws IOException: Broken pipe. Post-fix: swallowed,
        // the exit-0 result stands.
        var stubPath = await WriteStubAsync("#!/bin/sh\n"
            + "printf '%s' '{\"is_error\":false,\"result\":\"ok\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}'\n");

        var transport = new AnthropicLocalTransport(stubPath);
        var result = await transport.ExecuteAsync(
            new ProviderInvocation(
                Model: "claude-opus-4-8",
                Payload: AimlPayload.ForUserTurn(new string('x', BeyondPipeBuffer)),
                Timeout: TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        Assert.Equal("ok", result.Output.OutputText);
    }

    [Fact]
    public async Task LocalTransport_ChildExitsNonZeroWithoutDrainingStdin_MustSurfaceRealStderr_NotBrokenPipe()
    {
        if (OperatingSystem.IsWindows()) { return; }

        // Writes a diagnostic to stderr and exits 1 — never reads stdin. The transport must report the
        // CLI's OWN verdict (exit 1 + "boom"), never let the incidental broken pipe from the un-drained
        // write bury it. This is the production robustness half of the fix, not just test hygiene.
        var stubPath = await WriteStubAsync("#!/bin/sh\nprintf 'boom-real-cli-error' 1>&2\nexit 1\n");

        var transport = new AnthropicLocalTransport(stubPath);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.ExecuteAsync(
                new ProviderInvocation(
                    Model: "claude-opus-4-8",
                    Payload: AimlPayload.ForUserTurn(new string('x', BeyondPipeBuffer)),
                    Timeout: TimeSpan.FromSeconds(10)),
                CancellationToken.None));

        Assert.Contains("exited 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("boom-real-cli-error", ex.Message, StringComparison.Ordinal);
    }

    private static async Task<string> WriteStubAsync(string script)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "svac-pii-lens-argv", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var stubPath = Path.Combine(workDir, "claude-stub.sh");
        await File.WriteAllTextAsync(stubPath, script);
        if (!OperatingSystem.IsWindows()) // callers return early on Windows; this satisfies CA1416 in the helper.
        {
            File.SetUnixFileMode(stubPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return stubPath;
    }
}
