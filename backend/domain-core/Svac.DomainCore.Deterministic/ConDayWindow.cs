namespace Svac.DomainCore.Deterministic;

/// <summary>Reset cadence for a quota window (SLICE_S1_CONTRACT.md §5).</summary>
public enum ResetCadence
{
    Daily,
    Weekly,
}

/// <summary>Whether a window resets at the convention-local cutoff or the user's own local midnight.</summary>
public enum WindowLocality
{
    ConLocal,
    UserLocal,
}

/// <summary>
/// Immutable declaration of a quota's reset semantics, pinned beside the key per §5 ("The declaration
/// (reset semantics: daily/weekly × con-local/user-local) is code beside the key; the cap is 9A config").
/// </summary>
public sealed record ResetSpec(ResetCadence Cadence, WindowLocality Locality);

/// <summary>
/// Pure con-day / quota-window math (SLICE_S1_CONTRACT.md §5, §1b). Every function takes "now" and the
/// timezone/cutoff as explicit parameters — no wall-clock read, no ambient IConDayResolver call inside
/// this library. Golden vectors (DST spring/fall transitions, con-midnight rollover, con-local vs
/// user-local divergence) are the Build-phase (Phase 2) test suite; this is the pure math they exercise.
/// </summary>
public static class ConDayWindow
{
    /// <summary>
    /// Builds the deterministic window key a quota counter row is keyed by:
    /// windowKey(quotaKey, reset{daily|weekly, con-local|user-local}, tz, con_day_cutoff).
    /// Same (quotaKey, spec, tz, cutoff, instant) always yields the same key — the atomic
    /// UPSERT-WHERE quota counter (§2) depends on that determinism for idempotent-under-race writes.
    /// </summary>
    public static string WindowKey(string quotaKey, ResetSpec spec, TimeZoneInfo tz, TimeOnly conDayCutoff, DateTimeOffset instant)
    {
        var windowStart = WindowStart(spec, tz, conDayCutoff, instant);
        var locality = spec.Locality == WindowLocality.ConLocal ? "con" : "usr";
        var cadence = spec.Cadence == ResetCadence.Daily ? "d" : "w";
        return $"{quotaKey}:{cadence}:{locality}:{windowStart:yyyy-MM-ddTHH:mm:ssK}";
    }

    /// <summary>
    /// Resolves the start-of-window instant for the window that <paramref name="instant"/> falls in.
    /// Con-day rolls over at <paramref name="conDayCutoff"/> local time in <paramref name="tz"/>, not at
    /// literal midnight — the con_day_cutoff config default is 04:00 (§4). DST transitions are absorbed
    /// by converting through TimeZoneInfo, never by fixed-offset arithmetic.
    /// </summary>
    public static DateTimeOffset WindowStart(ResetSpec spec, TimeZoneInfo tz, TimeOnly conDayCutoff, DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, tz);
        var conDay = ConDayFor(local, conDayCutoff);

        var dayStartLocalWall = conDay.ToDateTime(conDayCutoff);
        var windowAnchorDay = spec.Cadence == ResetCadence.Weekly
            ? conDay.AddDays(-DaysSinceWeekAnchor(conDay))
            : conDay;
        var anchorWallClock = windowAnchorDay.ToDateTime(conDayCutoff);

        return LocalWallClockToOffset(anchorWallClock, tz);
    }

    /// <summary>
    /// The next instant this window resets, given the current instant is inside it. Used to populate
    /// LimitReached.resets_at (canonical timestamptz — client localizes per DR-7.7).
    /// </summary>
    public static DateTimeOffset NextResetAt(ResetSpec spec, TimeZoneInfo tz, TimeOnly conDayCutoff, DateTimeOffset instant)
    {
        var start = WindowStart(spec, tz, conDayCutoff, instant);
        var local = TimeZoneInfo.ConvertTime(start, tz);
        var nextWallClock = spec.Cadence == ResetCadence.Weekly
            ? local.AddDays(7)
            : local.AddDays(1);
        return LocalWallClockToOffset(DateTime.SpecifyKind(nextWallClock.DateTime, DateTimeKind.Unspecified), tz);
    }

    /// <summary>The con-day (calendar date under the cutoff convention) that a local instant falls on.</summary>
    private static DateOnly ConDayFor(DateTimeOffset local, TimeOnly cutoff)
    {
        var date = DateOnly.FromDateTime(local.DateTime);
        return TimeOnly.FromDateTime(local.DateTime) < cutoff ? date.AddDays(-1) : date;
    }

    /// <summary>ISO-8601 week anchor: Monday. Days elapsed since the most recent Monday for this con-day.</summary>
    private static int DaysSinceWeekAnchor(DateOnly conDay)
    {
        var iso = (int)conDay.DayOfWeek == 0 ? 7 : (int)conDay.DayOfWeek; // Sunday=0 -> 7
        return iso - 1; // Monday(1) -> 0
    }

    private static DateTimeOffset LocalWallClockToOffset(DateTime wallClock, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }
}
