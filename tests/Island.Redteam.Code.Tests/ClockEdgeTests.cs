using Island.Core;
using Island.Core.Speed;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: the pure clocks of the new code (FrameBudget, LightClock, ArcClock, CompositorPath) with odd times: NaN, infinity, negative, going backwards,
/// 2^53, and long random streams. A test named Defect_ fails on purpose (finding id in its comment); every other test passes and is the coverage statement.
/// </summary>
public class ClockEdgeTests
{
    private const double Hz165 = 1000.0 / 165;

    // ---- FrameBudget -------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void FrameBudget_Odd_Times_Never_Throw_And_The_Budget_Works_Again_After_Them()
    {
        var budget = new FrameBudget();
        double[] odd = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, -1e300, 1e300, Math.Pow(2, 53), Math.Pow(2, 53) + 2, 0, double.Epsilon, double.MaxValue, double.MinValue];
        foreach (var t in odd)
        {
            foreach (var interval in new[] { Hz165, 0, -5, double.NaN, double.PositiveInfinity, 1e-300, 1e300 })
            {
                var ex = Record.Exception(() => budget.Admit(t, interval));
                Assert.Null(ex);
            }
        }

        // Whatever happened, a fresh run of ordinary times is paced: at 1000 calls a second on a 165 Hz screen only about 165 a second get through.
        budget.Reset();
        var admitted = 0;
        for (var ms = 0; ms < 10_000; ms++)
            if (budget.Admit(ms, Hz165)) admitted++;
        Assert.InRange(admitted, 1640, 1655);
    }

    [Fact]
    public void FrameBudget_Never_Draws_More_Than_The_Refreshes_Plus_The_Bucket_In_Any_Stretch()
    {
        // Random call streams much faster than the screen (WPF calls the frame callback again when the tree changes); count what gets through in every window of the run.
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            var rng = new Random(seed);
            var budget = new FrameBudget();
            var times = new List<double>();
            var t = 0.0;
            while (t < 20_000)
            {
                t += rng.NextDouble() * 6; // 0 to 6 ms apart: 160 to unbounded calls a second
                if (budget.Admit(t, Hz165)) times.Add(t);
            }

            for (var i = 0; i < times.Count; i++)
            {
                for (var j = i; j < Math.Min(times.Count, i + 400); j += 7)
                {
                    var span = times[j] - times[i];
                    var frames = j - i + 1;
                    Assert.True(frames <= span / Hz165 + 1.5 + 1e-6, $"seed {seed}: {frames} frames in {span:0.0} ms (a screen of 165 Hz shows {span / Hz165:0.0})");
                }
            }
        }
    }

    [Fact]
    public void FrameBudget_Passes_Nearly_Every_Callback_Of_A_Steady_Stream_That_Matches_The_Screen()
    {
        // Callbacks at exactly the screen's rate with a millisecond of jitter must not lose frames (a lost frame is a visible stutter).
        var rng = new Random(11);
        var budget = new FrameBudget();
        var calls = 0;
        var admitted = 0;
        for (var n = 0; n < 6000; n++)
        {
            var at = n * Hz165 + (rng.NextDouble() - 0.5) * 2.0; // plus or minus one millisecond
            calls++;
            if (budget.Admit(at, Hz165)) admitted++;
        }

        Assert.True(admitted >= calls * 0.99, $"{admitted} of {calls} callbacks of a matching stream were drawn");
    }

    [Fact]
    public void FrameBudget_A_Clock_That_Goes_Back_Adds_Nothing_And_Loses_Nothing_Later()
    {
        var budget = new FrameBudget();
        Assert.True(budget.Admit(1000, Hz165));
        Assert.False(budget.Admit(1001, Hz165)); // tokens 0.25 + 0.165
        Assert.False(budget.Admit(500, Hz165)); // went back: nothing is added
        Assert.False(budget.Admit(501, Hz165));
        Assert.True(budget.Admit(510, Hz165)); // 500 -> 510 ms is more than one interval
    }

    // ---- LightClock ----------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LightClock_Holds_One_Place_For_One_Fixed_Interval_Of_25_Ms_And_Never_Longer()
    {
        var clock = new LightClock();
        var first = clock.Head(10_000, LightKind.FixedRate, 165, onlyEdgeMoves: true);
        Assert.Equal(first, clock.Head(10_015, LightKind.FixedRate, 165, true)); // within the fixed interval (25 ms; WORK-ORDER-13)
        Assert.NotEqual(first, clock.Head(10_026, LightKind.FixedRate, 165, true));
        Assert.Equal(ArcClock.Head(10_026 / 1000.0), clock.Head(10_026, LightKind.FixedRate, 165, true));
    }

    [Fact]
    public void LightClock_Never_Holds_For_The_Other_Kinds_Or_While_Something_Else_Moves_Or_Without_A_Rate()
    {
        var clock = new LightClock();
        foreach (var kind in new[] { LightKind.AsBefore, LightKind.GraphicsCard })
        {
            Assert.Equal(ArcClock.Head(1.0), clock.Head(1000, kind, 165, true));
            Assert.Equal(ArcClock.Head(1.004), clock.Head(1004, kind, 165, true));
        }

        Assert.Equal(ArcClock.Head(2.0), clock.Head(2000, LightKind.FixedRate, 165, false));
        Assert.Equal(ArcClock.Head(2.001), clock.Head(2001, LightKind.FixedRate, 165, false));
        Assert.Equal(ArcClock.Head(3.0), clock.Head(3000, LightKind.FixedRate, 0, true));
        Assert.Equal(ArcClock.Head(3.001), clock.Head(3001, LightKind.FixedRate, double.NaN, true));
    }

    [Fact]
    public void LightClock_Recovers_From_Infinity_And_From_A_Clock_That_Went_Back()
    {
        var clock = new LightClock();
        clock.Head(double.PositiveInfinity, LightKind.FixedRate, 165, true);
        Assert.Equal(ArcClock.Head(5.0), clock.Head(5000, LightKind.FixedRate, 165, true));
        clock.Head(9_000_000, LightKind.FixedRate, 165, true);
        Assert.Equal(ArcClock.Head(1.0), clock.Head(1000, LightKind.FixedRate, 165, true)); // far back: not held at the later place
    }

    [Fact]
    public void Defect_LightClock_A_NaN_Time_Poisons_The_Held_Place_Until_Something_Else_Moves()
    {
        // code-1-1 (LATENT): expected, after Head(NaN) the next ordinary time gives the clock's place, finite. Got NaN for as long as the island only moves its edge: Exact(NaN) stores
        // _heldAtMs = NaN, and "nowMs - NaN >= two intervals" is never true, so the NaN place is held for ever (a NaN ArcHead reaches every arc the old drawing draws).
        var clock = new LightClock();
        clock.Head(double.NaN, LightKind.FixedRate, 165, true);
        var next = clock.Head(1000, LightKind.FixedRate, 165, true);
        var later = clock.Head(5000, LightKind.FixedRate, 165, true);
        Assert.True(double.IsFinite(next) && double.IsFinite(later), $"after one NaN time the place was {next}, then {later}");
    }

    // ---- ArcClock and CompositorPath ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ArcClock_Stays_In_Zero_To_One_For_Ordinary_And_Huge_Times()
    {
        var rng = new Random(3);
        for (var i = 0; i < 200_000; i++)
        {
            var seconds = i % 3 == 0 ? rng.NextDouble() * 1e9 : i % 3 == 1 ? rng.NextDouble() * 1e3 : -rng.NextDouble() * 1e3;
            var head = ArcClock.Head(seconds);
            Assert.True(head >= 0 && head < 1, $"Head({seconds}) = {head}");
        }
    }

    [Fact]
    public void Defect_ArcClock_A_Tiny_Negative_Time_Gives_Exactly_One_Not_A_Fraction_Below_One()
    {
        // code-1-2 (LOW, latent): ArcClock's own text says a fraction of the outline, 0 up to 1; f - Math.Floor(f) is 1.0 for a tiny negative f (f = -1.8e-21: floor -1, and
        // -1.8e-21 + 1 rounds to 1.0). The same wrap is written again in CompositorPath.Wrap, where a sum that is -1e-17 by float noise gives 1.0 for the trim offset.
        // Expected: below 1 (0 and 1 are the same place, but a caller that indexes by the fraction gets one past the end).
        Assert.True(ArcClock.Head(-1e-20) < 1, $"Head(-1e-20) = {ArcClock.Head(-1e-20)}");
    }

    [Fact]
    public void CompositorPath_Trim_Offsets_Of_The_Three_Arcs_Keep_Their_Distances_Round_The_Lap_At_Any_Size()
    {
        // The offsets of the two arcs differ by exactly the arcs' own distance on the old light (half a lap less the lengths), whatever the capsule's size (the start never moves them apart).
        var look = ModeMark.Look.Approved;
        var spec = LightSpec.For(new Rgb(30, 144, 255), look)!;
        foreach (var (w, h, r) in new[] { (700.0, 76.0, 38.0), (180.0, 30.0, 15.0), (200.0, 200.0, 100.0), (30.0, 30.0, 15.0), (2.0, 2.0, 1.0), (0.0, 0.0, 0.0) })
        {
            var outline = RimOutline.Of(20, 14, w, h, r);
            for (var head = 0.0; head < 1; head += 0.0731)
            {
                var o1 = CompositorPath.TrimOffset(head, spec.FirstArc!.Value, outline);
                var o2 = CompositorPath.TrimOffset(head, spec.SecondArc!.Value, outline);
                var diff = (o2 - o1 + 1) % 1;
                var expect = ((LookConstants.SecondArcPhase - LookConstants.SecondArcFraction) - (0 - LookConstants.ArcFraction) + 1) % 1;
                // the distance between the two starts: (head + half - second fraction) - (head - first fraction)
                Assert.True(Math.Abs(diff - (expect % 1)) < 1e-9 || Math.Abs(Math.Abs(diff - (expect % 1)) - 1) < 1e-9, $"size {w}x{h}: arcs {diff} apart, expected {expect % 1}");
                Assert.True(double.IsFinite(o1) && double.IsFinite(o2));
            }
        }
    }

    [Fact]
    public void RimOutline_Odd_Sizes_Give_Finite_Numbers_Or_Zero_Never_NaN()
    {
        foreach (var (w, h, r) in new[] { (0.0, 0.0, 0.0), (-5.0, -5.0, -5.0), (0.2, 0.2, 5.0), (1e9, 76.0, 38.0), (700.0, 76.0, 1e9) })
        {
            var o = RimOutline.Of(0, 0, w, h, r);
            Assert.True(double.IsFinite(o.Length) && double.IsFinite(o.CompositorStart) && o.Width >= 0 && o.Height >= 0 && o.Radius >= 0, $"{w}x{h} r{r}: {o}");
            Assert.InRange(o.CompositorStart, 0, 1);
        }
    }

    [Fact]
    public void LightSpec_Is_Null_Exactly_Where_The_Old_Drawing_Draws_Something_Else()
    {
        var c = new Rgb(255, 64, 85);
        Assert.NotNull(LightSpec.For(c, ModeMark.LookOf(Mode.Focus, 0)));
        Assert.NotNull(LightSpec.For(c, ModeMark.LookOf(Mode.Vibe, 0)));
        Assert.Null(LightSpec.For(c, ModeMark.LookOf(Mode.DND, 0)));
        // half way through the cross fade into Do not disturb a dashed rim shows: no graphics-card light then
        Assert.Null(LightSpec.For(c, ModeMark.Blend(ModeMark.LookOf(Mode.Focus, 0), ModeMark.LookOf(Mode.DND, 0), 100)));
    }

    [Fact]
    public void ModeMark_Breath_Is_One_Breath_Periodic_As_The_Compositor_Keyframes_Assume()
    {
        // MovingLight.BreathAnimation makes keyframes from t to t + BreathSeconds and says the last is the first again.
        for (var t = 0.0; t < 50; t += 0.37)
            Assert.True(Math.Abs(ModeMark.Breath(t) - ModeMark.Breath(t + ModeMark.BreathSeconds)) < 1e-9, $"Breath not periodic at {t}");
        Assert.Equal(1.0, ModeMark.Breath(0), 9);
        Assert.Equal(ModeMark.BreathLow, ModeMark.Breath(ModeMark.BreathSeconds / 2), 9);
    }
}
