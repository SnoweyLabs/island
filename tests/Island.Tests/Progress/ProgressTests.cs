using Island.Core;

namespace Island.Tests;

public class ProgressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static ProgressReport Playing(double position, double length, double speed = 1, double reportedAt = 0) =>
        new(position, length, At(reportedAt), IsPlaying: true, speed);

    [Fact]
    public void Lit_Share_Equals_What_Is_Left()
    {
        // 25% played: three quarters are left (the self-test's own figure).
        Assert.Equal(0.75, LitShare.Of(Playing(25, 100), At(0))!.Value, 9);
        Assert.Equal(1, LitShare.Of(Playing(0, 100), At(0))!.Value, 9);
        Assert.Equal(0, LitShare.Of(Playing(100, 100), At(0))!.Value, 9);

        // Simulated reports over time, a steady player: what is left shrinks by one second per second.
        for (var t = 0; t <= 100; t += 10)
            Assert.Equal(Math.Max(0, (100 - (25 + t)) / 100.0), LitShare.Of(Playing(25, 100), At(t))!.Value, 9);
    }

    [Fact]
    public void Follows_A_Seek()
    {
        var before = Playing(10, 200);
        Assert.Equal(0.95, LitShare.Of(before, At(0))!.Value, 9);

        // The person jumps to 150: the new report replaces the old one, and the share moves at once.
        var after = Playing(150, 200, reportedAt: 5);
        Assert.Equal(0.25, LitShare.Of(after, At(5))!.Value, 9);

        // Back to the start.
        Assert.Equal(1, LitShare.Of(Playing(0, 200, reportedAt: 6), At(6))!.Value, 9);
    }

    [Fact]
    public void Works_Out_The_Position_Between_Reports_Only_While_Playing()
    {
        var playing = Playing(20, 100);
        Assert.Equal(0.7, LitShare.Of(playing, At(10))!.Value, 9);

        // Double speed.
        Assert.Equal(0.6, LitShare.Of(Playing(20, 100, speed: 2), At(10))!.Value, 9);

        // Paused: it stands where the report says, however long it is since.
        var paused = playing with { IsPlaying = false };
        Assert.Equal(0.8, LitShare.Of(paused, At(0))!.Value, 9);
        Assert.Equal(0.8, LitShare.Of(paused, At(3600))!.Value, 9);

        // Speed zero stands still too.
        Assert.Equal(0.8, LitShare.Of(Playing(20, 100, speed: 0), At(500))!.Value, 9);
    }

    [Fact]
    public void Never_Outside_Zero_To_One()
    {
        // Played past the end by time alone: ends at 0.
        Assert.Equal(0, LitShare.Of(Playing(90, 100), At(10_000))!.Value);

        // Time before the report (clock went backwards, report from the future): no time has passed.
        Assert.Equal(0.5, LitShare.Of(Playing(50, 100, reportedAt: 100), At(0))!.Value, 9);

        // Rewind runs backwards and stops at the start.
        Assert.Equal(1, LitShare.Of(Playing(10, 100, speed: -1), At(1000))!.Value);

        // Huge speed and huge length stay inside 0..1 and are numbers.
        Assert.Equal(0, LitShare.Of(Playing(1, 100, speed: 1e308), At(1e6))!.Value);
        Assert.Equal(1, LitShare.Of(Playing(1, 100, speed: -1e308), At(1e6))!.Value);
        var huge = LitShare.Of(Playing(5e11, 1e12), At(10))!.Value;
        Assert.InRange(huge, 0, 1);
        Assert.Equal(0.5, huge, 6);

        // An enormous gap between report and now (a report a year old, still "playing").
        Assert.Equal(0, LitShare.Of(Playing(1, 100), DateTimeOffset.MaxValue)!.Value);
        Assert.Equal(1, LitShare.Of(Playing(0, 100, reportedAt: 0), DateTimeOffset.MinValue)!.Value);

        // A wide sweep: always a real number inside 0..1, or null.
        var rng = new Random(7);
        double[] odd = [0, 1, -1, 0.5, 1e-9, 1e12, 1e308, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        foreach (var p in odd)
        foreach (var l in odd)
        foreach (var s in odd)
        foreach (var t in new[] { -1e9, -1, 0, 1, 1e9 })
        {
            var share = LitShare.Of(new ProgressReport(p, l, At(0), rng.Next(2) == 0, s), At(t));
            if (share is { } v) Assert.True(double.IsFinite(v) && v >= 0 && v <= 1, $"p={p} l={l} s={s} t={t} -> {v}");
        }
    }

    [Fact]
    public void Zero_Date_Or_A_Position_Past_The_End_Shows_No_Progress()
    {
        // Windows' way of saying "no timeline": zero date, whatever the numbers say.
        Assert.Null(LitShare.Of(new ProgressReport(10, 100, ProgressReport.WindowsZeroDate, true), At(0)));
        Assert.Null(LitShare.Of(new ProgressReport(0, 0, ProgressReport.WindowsZeroDate, true), At(0)));
        Assert.Null(LitShare.Of(new ProgressReport(10, 100, default, true), At(0)));
        Assert.Equal(new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero), ProgressReport.WindowsZeroDate);
        Assert.Equal(DateTimeOffset.FromFileTime(0).ToUniversalTime(), ProgressReport.WindowsZeroDate);

        // One tick after the zero date is a real moment.
        Assert.NotNull(LitShare.Of(new ProgressReport(10, 100, ProgressReport.WindowsZeroDate.AddTicks(1), false), At(0)));

        // A reported position past the length: no ring. Exactly at the length is a finished item, not an error.
        Assert.Null(LitShare.Of(Playing(100.5, 100), At(0)));
        Assert.Equal(0, LitShare.Of(Playing(100, 100), At(0))!.Value);
    }

    [Fact]
    public void Unusable_Numbers_Show_No_Progress()
    {
        Assert.Null(LitShare.Of(Playing(-1, 100), At(0)));
        Assert.Null(LitShare.Of(Playing(double.NaN, 100), At(0)));
        Assert.Null(LitShare.Of(Playing(double.PositiveInfinity, 100), At(0)));
        Assert.Null(LitShare.Of(Playing(10, double.NaN), At(0)));
        Assert.Null(LitShare.Of(Playing(10, double.PositiveInfinity), At(0)));
        Assert.Null(LitShare.Of(Playing(10, -5), At(0)));
        Assert.Null(LitShare.Of(Playing(10, 100, speed: double.NaN), At(0)));
        Assert.Null(LitShare.Of(Playing(10, 100, speed: double.PositiveInfinity), At(0)));
        Assert.Null(LitShare.Of(new ProgressReport(null, 100, At(0), true), At(0)));
    }

    [Fact]
    public void Speed_Zero_And_A_Default_Speed_Of_One()
    {
        Assert.Equal(1.0, new ProgressReport(1, 2, At(0), true).Speed);
        Assert.Equal(0.5, LitShare.Of(Playing(0, 100, speed: 0.5), At(100))!.Value, 9);
    }

    [Fact]
    public void Redraw_Only_For_A_Visible_Change()
    {
        const double outline = 400;
        // Less than one pixel of outline: no.
        Assert.False(RedrawGate.ShouldRedraw(0.5, 0.5 - 0.5 / outline, outline));
        // One pixel or more: yes.
        Assert.True(RedrawGate.ShouldRedraw(0.5, 0.5 - 1.0 / outline - 1e-9, outline));
        Assert.True(RedrawGate.ShouldRedraw(0.5, 0.4, outline));
        // Same share: no. A seek: yes.
        Assert.False(RedrawGate.ShouldRedraw(0.5, 0.5, outline));
        Assert.True(RedrawGate.ShouldRedraw(0.5, 0.9, outline));
        // Ring to moving light and back: always.
        Assert.True(RedrawGate.ShouldRedraw(0.5, null, outline));
        Assert.True(RedrawGate.ShouldRedraw(null, 0.5, outline));
        Assert.False(RedrawGate.ShouldRedraw(null, null, outline));
        // The very end is drawn even when it is a sub-pixel away.
        Assert.True(RedrawGate.ShouldRedraw(0.0001, 0, outline));
        // Bad input does not throw and does not draw for nothing.
        Assert.False(RedrawGate.ShouldRedraw(double.NaN, null, outline));
        Assert.True(RedrawGate.ShouldRedraw(0.5, 0.5001, 0));
        Assert.True(RedrawGate.ShouldRedraw(0.5, 0.5001, double.NaN));
    }

    [Fact]
    public void A_Playing_Item_Needs_A_Redraw_Roughly_Every_Few_Seconds_Not_Every_Frame()
    {
        // A 1 hour video on a 400 px outline: count the redraws over 60 s at 60 frames per second.
        var report = Playing(0, 3600);
        double? drawn = LitShare.Of(report, At(0));
        var redraws = 0;
        for (var frame = 1; frame <= 3600; frame++)
        {
            var next = LitShare.Of(report, At(frame / 60.0));
            if (!RedrawGate.ShouldRedraw(drawn, next, 400)) continue;
            redraws++;
            drawn = next;
        }

        Assert.InRange(redraws, 5, 8); // 400 px / 3600 s: one pixel every 9 s
    }

    [Fact]
    public void A_Tab_Message_Becomes_A_Report()
    {
        var arrived = At(100);
        var ms = At(98).ToUnixTimeMilliseconds();

        var report = TabMediaTiming.ToReport(42.5, 3600, true, 1.25, ms, arrived);
        Assert.Equal(At(98), report.ReportedAt);
        Assert.Equal(1.25, report.Speed);
        // 2 s between the reading and now, at speed 1.25: 42.5 + 2.5.
        Assert.Equal(1 - 45.0 / 3600, LitShare.Of(report, arrived)!.Value, 9);

        // An older add-on sends neither field: as arrived, normal speed.
        var old = TabMediaTiming.ToReport(42.5, 3600, true, null, null, arrived);
        Assert.Equal(arrived, old.ReportedAt);
        Assert.Equal(1, old.Speed);

        // Nonsense: from the future, zero, negative, NaN, huge, a bad rate.
        foreach (double? bad in new double?[] { At(200).ToUnixTimeMilliseconds(), 0, -5, double.NaN, double.PositiveInfinity, 1e300 })
            Assert.Equal(arrived, TabMediaTiming.ToReport(1, 10, true, null, bad, arrived).ReportedAt);
        Assert.Equal(1, TabMediaTiming.ToReport(1, 10, true, double.NaN, null, arrived).Speed);
        Assert.Null(LitShare.Of(TabMediaTiming.ToReport(null, null, true, 1, ms, arrived), arrived)); // null means unknown
    }

    [Fact]
    public void A_Now_Playing_View_Gives_A_Report_That_Matches_Its_Own_Progress()
    {
        var np = new NowPlaying();
        var policy = new NowPlayingPolicy([], DesktopPlayersCount: true, BrowserSessionCounts: false);
        var session = new MediaSessionInfo("a", "alpha.exe", false, "Song", "Artist", PlaybackState.Playing, 25, 100);
        var view = np.Update(At(0), [session], [], false, policy)!;

        var share = LitShare.Of(ProgressReport.FromView(view, At(0)), At(0))!.Value;
        Assert.Equal(1 - view.Progress!.Value, share, 9);

        // A view with no length (EVALS N11) gives no progress.
        var unknown = new MediaSessionInfo("b", "beta.exe", false, "Live", null, PlaybackState.Playing, null, null);
        var live = new NowPlaying().Update(At(0), [unknown], [], false, policy)!;
        Assert.Null(LitShare.Of(ProgressReport.FromView(live, At(0)), At(0)));
    }
}
