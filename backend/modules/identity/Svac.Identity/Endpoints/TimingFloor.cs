using System.Diagnostics;

namespace Svac.Identity.Endpoints;

/// <summary>
/// Bounds the wall-clock delta between the enumeration-suppressed branches of an anti-enumeration
/// endpoint (SLICE_S3_CONTRACT.md §1c/§10.3: "normalized timing", "timing delta bounded"). A floor-delay
/// technique, not a claim of perfect constant-time execution: the dominant real-world variance on these
/// endpoints is whether an SMTP send happened, and awaiting UP TO a fixed floor after the real work
/// completes collapses that variance to well under the E2E's bounded-delta assertion, without slowing the
/// legitimate (mail-sent) path, which already meets or exceeds the floor on its own.
/// </summary>
public static class TimingFloor
{
    public static async Task NormalizeAsync(Stopwatch stopwatch, TimeSpan floor, CancellationToken ct)
    {
        var remaining = floor - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, ct);
        }
    }
}
