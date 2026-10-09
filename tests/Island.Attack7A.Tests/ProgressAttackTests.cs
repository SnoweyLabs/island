using Island.Core;

namespace Island.Attack7A.Tests;

/// <summary>The pill's progress: reports beyond the length, negative, NaN, infinite, a length that changes every report, zero length.</summary>
public class ProgressAttackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly double?[] Odd =
    [
        null, 0, -0.0, 1, -1, 0.5, 100, 1e-300, 5e-324, -5e-324, 1e15, 1e300, double.MaxValue, double.MinValue, double.Epsilon,
        double.NaN, double.PositiveInfinity, double.NegativeInfinity,
    ];

    private static readonly double[] OddSpeed = [0, 1, -1, 2, 1e-300, 1e300, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1e300];

    private static readonly DateTimeOffset[] OddTimes =
    [
        T0, T0.AddHours(-1), T0.AddHours(1), T0.AddYears(-50), T0.AddYears(500), ProgressReport.WindowsZeroDate, ProgressReport.WindowsZeroDate.AddTicks(1),
        DateTimeOffset.MinValue, DateTimeOffset.MaxValue, default,
    ];

    [Fact]
    public void Holds_LitShare_Is_Null_Or_In_Zero_To_One_Never_NaN_Never_Throws_For_Every_Odd_Combination()
    {
        var count = 0;
        foreach (var pos in Odd)
        foreach (var len in Odd)
        foreach (var speed in OddSpeed)
        foreach (var at in OddTimes)
        foreach (var playing in new[] { true, false })
        foreach (var now in new[] { T0, DateTimeOffset.MaxValue, DateTimeOffset.MinValue, T0.AddDays(-400) })
        {
            var share = LitShare.Of(new ProgressReport(pos, len, at, playing, speed), now);
            count++;
            if (share is { } s) Assert.True(double.IsFinite(s) && s >= 0 && s <= 1, $"pos={pos} len={len} speed={speed} at={at:O} playing={playing} now={now:O} -> {s}");
        }

        Assert.True(count > 100_000);
    }

    [Fact]
    public void Holds_No_Invented_Progress_Cases_Of_Section_2()
    {
        foreach (var len in new double?[] { null, 0, -3, double.NaN, double.PositiveInfinity })
            Assert.Null(LitShare.Of(new ProgressReport(10, len, T0, true), T0));
        Assert.Null(LitShare.Of(new ProgressReport(10, 100, ProgressReport.WindowsZeroDate, true), T0));
        Assert.Null(LitShare.Of(new ProgressReport(101, 100, T0, true), T0)); // beyond the length
        Assert.Null(LitShare.Of(new ProgressReport(-1, 100, T0, true), T0));
        Assert.Null(LitShare.Of(new ProgressReport(null, 100, T0, true), T0));
        Assert.Equal(0.75, LitShare.Of(new ProgressReport(25, 100, T0, false), T0.AddHours(5))!.Value, 12); // paused stands still
        Assert.Equal(0.5, LitShare.Of(new ProgressReport(50, 100, T0, true), T0.AddHours(-1))!.Value, 12); // a clock that went backwards counts as no time
        Assert.Equal(0.0, LitShare.Of(new ProgressReport(50, 100, T0, true), T0.AddHours(1))!.Value, 12); // a stale playing report ends at 0
    }

    [Fact]
    public void Holds_A_Length_That_Changes_Every_Report_Stays_In_Range_And_Follows_Each_Report()
    {
        var r = new Random(7);
        for (var i = 0; i < 20_000; i++)
        {
            var len = r.NextDouble() < 0.1 ? 0 : r.NextDouble() * 10_000;
            var pos = r.NextDouble() * len * (r.NextDouble() < 0.2 ? 1.5 : 1);
            var report = new ProgressReport(pos, len, T0, r.Next(2) == 0, 1 + r.Next(-2, 3));
            var share = LitShare.Of(report, T0.AddSeconds(r.NextDouble() * 50));
            if (share is { } s) Assert.InRange(s, 0.0, 1.0);
            if (len <= 0 || pos > len) Assert.Null(share);
        }
    }

    [Fact]
    public void Holds_RedrawGate_Never_Throws_And_A_Non_Number_Counts_As_No_Progress()
    {
        double?[] shares = [null, 0, 1, 0.5, 0.5000001, double.NaN, double.PositiveInfinity, -3, 7];
        foreach (var a in shares)
        foreach (var b in shares)
        foreach (var px in new[] { 0, -1, 1, 1000, double.NaN, double.PositiveInfinity })
            _ = RedrawGate.ShouldRedraw(a, b, px);
        Assert.False(RedrawGate.ShouldRedraw(double.NaN, null, 100)); // both "no progress"
        Assert.True(RedrawGate.ShouldRedraw(null, 0.5, 100));
        Assert.False(RedrawGate.ShouldRedraw(0.5, 0.50001, 1000)); // under a pixel
        Assert.True(RedrawGate.ShouldRedraw(0.5, 0.0, 1000));
    }

    [Fact]
    public void Holds_TabMediaTiming_Gives_A_Report_That_Never_Comes_From_The_Future()
    {
        double?[] bad = [null, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -5, 1e300, 253402300799000d, 253402300799001d, 1e20];
        foreach (var readAt in bad)
        foreach (var rate in new double?[] { null, double.NaN, double.PositiveInfinity, 0, -1, 1e300 })
        {
            var report = TabMediaTiming.ToReport(10, 100, true, rate, readAt, T0);
            Assert.True(report.ReportedAt <= T0, $"readAt={readAt}");
            Assert.True(double.IsFinite(report.Speed));
            var share = LitShare.Of(report, T0.AddSeconds(1));
            if (share is { } s) Assert.True(double.IsFinite(s) && s is >= 0 and <= 1);
        }

        var future = TabMediaTiming.ToReport(10, 100, true, 1, T0.AddHours(3).ToUnixTimeMilliseconds(), T0);
        Assert.Equal(T0, future.ReportedAt);
    }

    private static readonly MediaSessionInfo Base = new("s1", "Spotify.exe", false, "Song", "Artist", PlaybackState.Playing, 10, 100);

    [Fact]
    public void Holds_NowPlaying_Fed_Odd_Numbers_For_A_Thousand_Reports_Keeps_Progress_In_Range()
    {
        var np = new NowPlaying();
        var policy = new NowPlayingPolicy([], true, false);
        var r = new Random(3);
        var now = T0;
        for (var i = 0; i < 3000; i++)
        {
            now = now.AddMilliseconds(r.Next(0, 3000));
            double? Pick() => Odd[r.Next(Odd.Length)];
            var timeline = r.Next(3) == 0 ? new ProgressReport(Pick(), Pick(), OddTimes[r.Next(OddTimes.Length)], r.Next(2) == 0, OddSpeed[r.Next(OddSpeed.Length)]) : (ProgressReport?)null;
            var session = Base with { SessionId = "s" + r.Next(3), PositionSeconds = Pick(), LengthSeconds = Pick(), State = (PlaybackState)r.Next(3), Timeline = timeline };
            var view = np.Update(now, [session], [], false, policy);
            if (view is null) continue;
            if (view.Progress is { } p) Assert.True(double.IsFinite(p) && p is >= 0 and <= 1, $"progress {p}");
            if (view.PositionSeconds is { } pos && view.LengthSeconds is { } len) Assert.True(pos >= 0 && pos <= len);
            if (view.Report is { } rep && LitShare.Of(rep, now.AddSeconds(r.Next(0, 99))) is { } share) Assert.InRange(share, 0.0, 1.0);
        }
    }

    [Fact]
    public void Defect_A_Negative_Position_With_No_Length_Reaches_The_Line_As_A_Negative_Position()
    {
        var np = new NowPlaying();
        var view = np.Update(T0, [Base with { PositionSeconds = -42, LengthSeconds = null }], [], false, new NowPlayingPolicy([], true, false));
        Assert.NotNull(view);
        Assert.True(view!.PositionSeconds is null or >= 0, $"position {view.PositionSeconds}");
    }

    [Fact]
    public void Holds_A_Tab_Report_With_Position_Beyond_Length_Gives_No_Ring_And_Full_Progress_Clamped()
    {
        var np = new NowPlaying();
        var tab = new TabInfo("k1", 1, 1, "t", "www.youtube.com", true, true, 1, new TabMedia("v", "a", PlaybackState.Playing, 500, 100, 1, null));
        var view = np.Update(T0, [], [tab], true, new NowPlayingPolicy(["youtube.com"], true, false))!;
        Assert.True(view.Progress is 1.0);
        Assert.Null(LitShare.Of(view.Report!.Value, T0)); // beyond the length: the moving light, not a made-up ring
    }

    [Fact]
    public void Holds_A_Thousand_Distinct_Sessions_Do_Not_Leave_Memory_Behind_Once_They_Vanish()
    {
        var np = new NowPlaying();
        var policy = new NowPlayingPolicy([], true, false);
        var now = T0;
        for (var i = 0; i < 1000; i++)
            np.Update(now = now.AddSeconds(1), [Base with { SessionId = "s" + i }], [], false, policy);
        for (var i = 0; i < 20; i++) np.Update(now = now.AddSeconds(2), [], [], false, policy);
        Assert.Null(np.Current(now));
    }
}
