using Island.Core;

namespace Island.Tests;

public class SpringTests
{
    [Fact]
    public void Converges_To_Target_And_Stays_Finite()
    {
        var s = new Spring(-90, 0, 14);
        for (var frame = 0; frame < 60 * 20; frame++)
        {
            s = s.Frame(1.0 / 60);
            Assert.True(double.IsFinite(s.Value) && double.IsFinite(s.Velocity));
        }

        Assert.InRange(s.Value, 14 - 0.001, 14 + 0.001);
        Assert.InRange(s.Velocity, -0.001, 0.001);
    }

    [Fact]
    public void Stays_Finite_Under_Huge_Frame_Times()
    {
        var s = new Spring(-90, 0, 14);
        for (var i = 0; i < 1000; i++) s = s.Frame(i % 2 == 0 ? 5.0 : 0.0);
        Assert.True(double.IsFinite(s.Value));
    }

    [Fact]
    public void Matches_Reference_Trajectory()
    {
        // Computed on 6 Oct 2026 with a line-for-line port of the reference step function.
        var s = new Spring(-90, 0, 14);
        var max = double.MinValue;
        var maxFrame = 0;
        var checkpoints = new Dictionary<int, double> { [6] = -27.640, [12] = 14.277, [18] = 18.746 };

        for (var frame = 1; frame <= 60; frame++)
        {
            s = s.Frame(1.0 / 60);
            if (s.Value > max) { max = s.Value; maxFrame = frame; }
            if (checkpoints.TryGetValue(frame, out var expected))
                Assert.InRange(s.Value, expected - 0.01, expected + 0.01);
        }

        Assert.InRange(max, 19.249 - 0.01, 19.249 + 0.01);
        Assert.Equal(16, maxFrame);
    }

    [Fact]
    public void Same_Path_At_Any_Frame_Rate()
    {
        // EVALS.md M1: four frame patterns, asked for the position at the same four moments.
        var irregular = new[] { 0.003, 0.025, 0.011, 0.007, 0.0166, 0.04, 0.0009 };
        var patterns = new (string Name, Func<int, double> Frame)[]
        {
            ("30 Hz", _ => 1.0 / 30),
            ("60 Hz", _ => 1.0 / 60),
            ("144 Hz", _ => 1.0 / 144),
            ("irregular", i => irregular[i % irregular.Length]),
        };
        double[] moments = [0.10, 0.25, 0.50, 1.00];

        var seen = patterns.ToDictionary(p => p.Name, p => Sample(p.Frame, moments));

        for (var m = 0; m < moments.Length; m++)
        {
            var values = seen.Values.Select(v => v[m]).ToArray();
            Assert.True(values.Max() - values.Min() < 0.01,
                $"at {moments[m]} s the patterns disagree: " + string.Join(", ", seen.Select(kv => $"{kv.Key}={kv.Value[m]:F4}")));
        }
    }

    [Fact]
    public void Drawn_Value_Is_Continuous_Between_Steps()
    {
        // The drawn value is interpolated, so a tiny advance moves it by a tiny amount: no stair-steps.
        var s = new Spring(-90, 0, 14).Frame(0.1);
        var before = s.Drawn;
        var after = s.Frame(0.0001).Drawn;
        Assert.InRange(Math.Abs(after - before), 0, (Math.Abs(s.Velocity) + 50) * 0.0001 * 1.1);
    }

    [Fact]
    public void Long_Stall_Catches_Up()
    {
        // EVALS.md M2: one advance of 0.5 s equals thirty advances of 1/60 s.
        var stalled = new Spring(-90, 0, 14).Frame(0.5);
        var steady = new Spring(-90, 0, 14);
        for (var i = 0; i < 30; i++) steady = steady.Frame(1.0 / 60);

        Assert.InRange(Math.Abs(stalled.Drawn - steady.Drawn), 0, 0.01);
        Assert.InRange(Math.Abs(stalled.Value - steady.Value), 0, 0.01);
        Assert.Equal(steady.Steps, stalled.Steps);
    }

    [Fact]
    public void An_Absurd_Stall_Does_Not_Hang_And_Lands_On_The_Target()
    {
        var s = new Spring(-90, 0, 14).Frame(3600);
        Assert.InRange(s.Drawn, 14 - 1e-6, 14 + 1e-6);
    }

    [Fact]
    public void A_Target_Change_Never_Moves_The_Drawn_Value()
    {
        var s = new Spring(-90, 0, -90).Frame(0.0123);
        var changed = s.WithTarget(14);
        Assert.Equal(s.Drawn, changed.Drawn);
        Assert.Equal(s.Value, changed.Value);
        Assert.Equal(s.Velocity, changed.Velocity);
    }

    [Fact]
    public void Each_Step_Is_Previous_Plus_Velocity_Times_Step()
    {
        var s = new Spring(-90, 0, 14);
        for (var i = 0; i < 300; i++)
        {
            var next = s.Frame(Spring.StepSeconds);
            Assert.Equal(s.Value + next.Velocity * Spring.StepSeconds, next.Value, 1e-12);
            s = next;
        }
    }

    private static double[] Sample(Func<int, double> frame, double[] moments)
    {
        var spring = new Spring(-90, 0, 14);
        double now = 0;
        var frames = 0;
        var result = new double[moments.Length];
        for (var m = 0; m < moments.Length; m++)
        {
            while (now < moments[m] - 1e-12)
            {
                var dt = Math.Min(frame(frames++), moments[m] - now);
                spring = spring.Frame(dt);
                now += dt;
            }

            result[m] = spring.Drawn;
        }

        return result;
    }

    [Fact]
    public void Zero_Frame_Time_Changes_Nothing()
    {
        var s = new Spring(3, 7, 14);
        Assert.Equal(s, s.Frame(0));
    }
}
