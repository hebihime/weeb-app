using Svac.DomainCore.Deterministic;
using Xunit;

namespace Svac.Tests.DomainCore;

public sealed class ConDayWindowTests
{
    private static readonly TimeOnly DefaultCutoff = new(4, 0); // core.con_day_cutoff v0 (§4)
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void WindowKey_IsDeterministic_SameInputsSameKey()
    {
        var spec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var now = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

        var a = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, now);
        var b = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, now);

        Assert.Equal(a, b);
    }

    [Fact]
    public void WindowKey_BeforeCutoff_BelongsToThePreviousConDay()
    {
        var spec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var beforeCutoff = new DateTimeOffset(2026, 7, 10, 2, 0, 0, TimeSpan.Zero); // 02:00, cutoff is 04:00
        var afterCutoff = new DateTimeOffset(2026, 7, 10, 5, 0, 0, TimeSpan.Zero); // 05:00

        var beforeKey = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, beforeCutoff);
        var afterKey = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, afterCutoff);

        Assert.NotEqual(beforeKey, afterKey);
        Assert.Contains("2026-07-09", beforeKey, StringComparison.Ordinal); // rolled back a con-day
        Assert.Contains("2026-07-10", afterKey, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowKey_JustBeforeAndJustAfterMidnight_BothBelongToTheSameConDay()
    {
        // 04:00 cutoff means 23:59 and 00:01 the next calendar day are still the SAME con-day.
        var spec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var lateNight = new DateTimeOffset(2026, 7, 10, 23, 59, 0, TimeSpan.Zero);
        var earlyMorning = new DateTimeOffset(2026, 7, 11, 0, 1, 0, TimeSpan.Zero);

        var lateKey = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, lateNight);
        var earlyKey = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, earlyMorning);

        Assert.Equal(lateKey, earlyKey);
    }

    [Fact]
    public void WindowKey_WeeklyCadence_AnchorsToMonday()
    {
        var spec = new ResetSpec(ResetCadence.Weekly, WindowLocality.ConLocal);
        var wednesday = new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero); // a Wednesday
        var friday = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero); // same week's Friday

        var wednesdayKey = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, wednesday);
        var fridayKey = ConDayWindow.WindowKey("quota.test", spec, Utc, DefaultCutoff, friday);

        Assert.Equal(wednesdayKey, fridayKey); // same window (both anchor to Monday 2026-07-06)
    }

    [Fact]
    public void WindowKey_ConLocalVsUserLocal_ProduceDifferentKeys()
    {
        var conSpec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var userSpec = new ResetSpec(ResetCadence.Daily, WindowLocality.UserLocal);
        var now = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

        var conKey = ConDayWindow.WindowKey("quota.test", conSpec, Utc, DefaultCutoff, now);
        var userKey = ConDayWindow.WindowKey("quota.test", userSpec, Utc, DefaultCutoff, now);

        Assert.NotEqual(conKey, userKey);
    }

    [Fact]
    public void NextResetAt_DailyWindow_IsExactly24HoursAfterWindowStart()
    {
        var spec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var now = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

        var start = ConDayWindow.WindowStart(spec, Utc, DefaultCutoff, now);
        var resetsAt = ConDayWindow.NextResetAt(spec, Utc, DefaultCutoff, now);

        Assert.Equal(start.AddDays(1), resetsAt);
    }

    [Fact]
    public void NextResetAt_IsAlwaysInTheFuture_RelativeToNow()
    {
        var spec = new ResetSpec(ResetCadence.Daily, WindowLocality.ConLocal);
        var now = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);

        var resetsAt = ConDayWindow.NextResetAt(spec, Utc, DefaultCutoff, now);

        Assert.True(resetsAt > now);
    }
}
