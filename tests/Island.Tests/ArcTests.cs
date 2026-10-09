using Island.Core;

namespace Island.Tests;

public class ArcTests
{
    [Fact]
    public void Position_Depends_Only_On_Time()
    {
        // However the time is reached — frames of any size, irregular frames, one jump — the light
        // is in the same place at the same moment.
        var irregular = new[] { 0.003, 0.025, 0.011, 0.007, 0.0166, 0.04 };
        var patterns = new (string Name, Func<int, double> Frame)[]
        {
            ("30 Hz", _ => 1.0 / 30),
            ("60 Hz", _ => 1.0 / 60),
            ("144 Hz", _ => 1.0 / 144),
            ("irregular", i => irregular[i % irregular.Length]),
        };

        foreach (var (name, frame) in patterns)
        {
            double t = 0, head = ArcClock.Head(0);
            for (var i = 0; i < 4000; i++)
            {
                var dt = frame(i);
                t += dt;
                head += dt * LookConstants.ArcSpeedPerSecond; // the light moves with time and nothing else
                if (i % 250 == 0)
                    Assert.True(Math.Abs(WrapDelta(ArcClock.Head(t), head)) < 1e-9, $"{name} at t={t}");
            }
        }

        Assert.Equal(ArcClock.Head(123.456), ArcClock.Head(123.456));
        Assert.InRange(ArcClock.Head(987654.321), 0, 1 - 1e-12);
    }

    [Fact]
    public void Light_Covers_Eighteen_Percent_Of_The_Outline_Per_Second()
    {
        for (var t = 0.0; t < 40; t += 0.37)
        {
            var delta = ArcClock.Head(t + 1) - ArcClock.Head(t);
            Assert.Equal(0.18, delta < 0 ? delta + 1 : delta, 1e-9);
        }
    }

    [Fact]
    public void Second_Arc_Is_Half_A_Perimeter_Away()
    {
        for (var t = 0.0; t < 10; t += 0.31)
        {
            var d = ArcClock.SecondHead(t) - ArcClock.Head(t);
            Assert.Equal(0.5, d < 0 ? d + 1 : d, 1e-9);
        }
    }

    [Theory]
    [InlineData(30, 30, 15)]
    [InlineData(466, 76, 38)]
    [InlineData(502, 76, 38)]
    [InlineData(250, 60, 30)]
    public void Speed_Along_The_Outline_Is_The_Same_Fraction_At_Any_Size(double w, double h, double r)
    {
        // At every size the head moves 18% of that outline per second: the distance covered in a
        // second is 0.18 of its length, so the light is constant in fraction, and constant in
        // speed along the true outline at any one size, including a size that is changing.
        var p = new RoundedPerimeter(0, 0, w, h, r);
        var from = p.PointAt(ArcClock.Head(3.0));
        var to = p.PointAt(ArcClock.Head(3.0 + 1e-3));
        var chord = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
        Assert.Equal(0.18 * p.Length * 1e-3, chord, 0.18 * p.Length * 1e-3 * 0.02);
    }

    private static double WrapDelta(double a, double b)
    {
        var d = (a - b) % 1;
        if (d > 0.5) d -= 1;
        if (d < -0.5) d += 1;
        return d;
    }
}
