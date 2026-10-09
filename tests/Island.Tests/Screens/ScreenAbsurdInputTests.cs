using Island.Core;

namespace Island.Tests;

/// <summary>The chooser meets garbage and must neither throw nor return nonsense (the documented fallbacks).</summary>
public class ScreenAbsurdInputTests
{
    private static ScreenInfo Plain(int left, int top, int w, int h, double scale = 1.0, bool primary = false) =>
        new(new PixelRect(left, top, left + w, top + h), new PixelRect(left, top, left + w, top + h), scale, primary);

    [Fact]
    public void No_Screens_Gives_The_Stand_In()
    {
        var empty = ScreenChooser.Choose(new ScreenPoint(5, 5), [], 600, 120);
        var none = ScreenChooser.Choose(new ScreenPoint(5, 5), null, 600, 120);

        Assert.True(empty.IsFallback);
        Assert.True(none.IsFallback);
        Assert.Equal(-1, empty.Index);
        Assert.Equal(new PixelRect((1920 - 600) / 2, 0, (1920 - 600) / 2 + 600, 120), empty.Window);
    }

    [Fact]
    public void A_Zero_Size_Screen_Is_Skipped()
    {
        var screens = new[] { Plain(0, 0, 0, 0, primary: true), Plain(0, 0, 1920, 0), Plain(100, 100, 1280, 720) };

        var placement = ScreenChooser.Choose(new ScreenPoint(0, 0), screens, 600, 120);

        Assert.Equal(2, placement.Index); // the nearest of the usable ones
        Assert.False(placement.IsFallback);
    }

    [Fact]
    public void Only_Zero_Size_Screens_Gives_The_Stand_In() =>
        Assert.True(ScreenChooser.Choose(new ScreenPoint(0, 0), [Plain(0, 0, 0, 0), Plain(5, 5, 10, 0)], 600, 120).IsFallback);

    [Fact]
    public void An_Empty_Work_Area_Is_Replaced_By_The_Full_Screen()
    {
        var s = new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 0, 0), 1.0, true);

        var placement = ScreenChooser.Choose(new ScreenPoint(1, 1), [s], 600, 120);

        Assert.Equal(660, placement.Window.Left);
        Assert.Equal(0, placement.Window.Top);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Absurd_Scaling_Reads_As_One_Hundred_Percent(double scale)
    {
        var placement = ScreenChooser.Choose(new ScreenPoint(1, 1), [Plain(0, 0, 1920, 1080, scale, true)], 600, 120);

        Assert.Equal(600, placement.Window.Width);
        Assert.Equal(120, placement.Window.Height);
    }

    [Theory]
    [InlineData(double.NaN, 120)]
    [InlineData(600, double.NaN)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(double.PositiveInfinity, double.PositiveInfinity)]
    public void An_Absurd_Window_Size_Gives_A_Small_Positive_Window(double w, double h)
    {
        var placement = ScreenChooser.Choose(new ScreenPoint(1, 1), [Plain(0, 0, 1920, 1080, 1.0, true)], w, h);

        Assert.True(placement.Window.Width >= 1);
        Assert.True(placement.Window.Height >= 1);
    }

    [Fact]
    public void Extreme_Coordinates_Do_Not_Overflow()
    {
        var s = Plain(int.MaxValue - 1000, int.MaxValue - 1000, 1000, 1000, 2.0, true);

        var placement = ScreenChooser.Choose(new ScreenPoint(int.MinValue, int.MinValue), [s], 600, 120);

        Assert.Equal(0, placement.Index);
        Assert.True(placement.Window.Left <= placement.Window.Right);
        var far = new ScreenInfo(new PixelRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), new PixelRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), 1.0); // wider than int can count
        var p2 = ScreenChooser.Choose(new ScreenPoint(int.MaxValue, int.MaxValue), [far], 600, 120);
        Assert.Equal(0, p2.Index);
    }

    [Fact]
    public void Forty_Screens_Pick_The_Right_One()
    {
        var screens = Enumerable.Range(0, 40).Select(i => Plain(i * 1000, 0, 1000, 800, 1.0, i == 0)).ToList();

        var placement = ScreenChooser.Choose(new ScreenPoint(25_500, 400), screens, 600, 120);

        Assert.Equal(25, placement.Index);
    }

    [Fact]
    public void Screens_Beyond_The_Cap_Are_Ignored_And_Cost_Nothing()
    {
        var screens = Enumerable.Range(0, 100_000).Select(i => Plain(i * 10, 0, 10, 10)).ToList();

        var placement = ScreenChooser.Choose(new ScreenPoint(900_000, 5), screens, 600, 120);

        Assert.Equal(ScreenChooser.MaxScreens - 1, placement.Index); // the nearest of the first 64
    }

    [Fact]
    public void Overlapping_Screens_The_First_Wins()
    {
        var screens = new[] { Plain(0, 0, 1000, 1000, primary: true), Plain(500, 0, 1000, 1000) };

        Assert.Equal(0, ScreenChooser.Choose(new ScreenPoint(700, 10), screens, 600, 120).Index);
    }

    [Fact]
    public void An_Unreadable_Pointer_Takes_The_Primary_Screen()
    {
        var screens = new[] { Plain(1920, 0, 1920, 1080), Plain(0, 0, 1920, 1080, primary: true) };

        Assert.Equal(1, ScreenChooser.Choose(null, screens, 600, 120).Index);
    }

    [Fact]
    public void An_Unreadable_Pointer_With_No_Primary_Takes_The_First_Usable()
    {
        var screens = new[] { Plain(0, 0, 0, 0), Plain(1920, 0, 1920, 1080), Plain(0, 0, 100, 100) };

        Assert.Equal(1, ScreenChooser.Choose(null, screens, 600, 120).Index);
    }

    [Fact]
    public void A_Window_Wider_Than_The_Work_Area_Stays_Centred()
    {
        var placement = ScreenChooser.Choose(new ScreenPoint(1, 1), [Plain(0, 0, 400, 300, 1.0, true)], 600, 120);

        Assert.Equal(-100, placement.Window.Left);
        Assert.Equal(500, placement.Window.Right);
    }

    [Fact]
    public void The_Same_Input_Gives_The_Same_Answer()
    {
        var screens = new[] { Plain(0, 0, 1920, 1080, 1.25, true), Plain(1920, -200, 2560, 1440, 1.5) };

        var a = ScreenChooser.Choose(new ScreenPoint(3000, 0), screens, 600, 120);
        var b = ScreenChooser.Choose(new ScreenPoint(3000, 0), screens, 600, 120);

        Assert.Equal(a, b);
    }

    [Fact]
    public void The_Pretend_Source_Raises_And_Holds_Its_Lists()
    {
        var pretend = new PretendScreens { Screens = [Plain(0, 0, 100, 100)], Pointer = new ScreenPoint(1, 1) };
        var raised = 0;
        IScreenSource source = pretend;
        source.Changed += () => raised++;

        pretend.Raise();

        Assert.Equal(1, raised);
        Assert.Single(source.Screens);
        Assert.Equal(new ScreenPoint(1, 1), source.Pointer);
    }

    private static int Wild(Random r) => r.Next(8) switch
    {
        0 => int.MinValue,
        1 => int.MaxValue,
        2 => 0,
        3 => r.Next(-100, 100),
        _ => r.Next(-5000, 5000),
    };

    private static readonly double[] WildDoubles = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1, 1, 1.25, 1.5, 2, 1e300, 1e-300];

    [Fact]
    public void Random_Garbage_Never_Throws_And_Keeps_Its_Promises()
    {
        var r = new Random(20261006);
        for (var round = 0; round < 20_000; round++)
        {
            var screens = Enumerable.Range(0, r.Next(0, 12)).Select(_ => new ScreenInfo(
                new PixelRect(Wild(r), Wild(r), Wild(r), Wild(r)),
                new PixelRect(Wild(r), Wild(r), Wild(r), Wild(r)),
                WildDoubles[r.Next(WildDoubles.Length)], r.Next(2) == 0)).ToList();
            ScreenPoint? pointer = r.Next(5) == 0 ? null : new ScreenPoint(Wild(r), Wild(r));
            var w = WildDoubles[r.Next(WildDoubles.Length)] * (r.Next(2) == 0 ? 1 : 300);
            var h = WildDoubles[r.Next(WildDoubles.Length)] * (r.Next(2) == 0 ? 1 : 100);

            var placement = ScreenChooser.Choose(pointer, screens, w, h);

            Assert.True(placement.Window.Width >= 1 && placement.Window.Height >= 1 || placement.Window.Right == int.MaxValue || placement.Window.Bottom == int.MaxValue);
            Assert.True(placement.Window.Left <= placement.Window.Right && placement.Window.Top <= placement.Window.Bottom);
            if (placement.IsFallback)
            {
                Assert.Equal(-1, placement.Index);
                Assert.DoesNotContain(screens, s => s.IsUsable);
            }
            else
            {
                Assert.InRange(placement.Index, 0, Math.Min(screens.Count, ScreenChooser.MaxScreens) - 1);
                Assert.True(screens[placement.Index].IsUsable);
                if (pointer is { } p && screens.Take(ScreenChooser.MaxScreens).Any(s => s.IsUsable && s.Full.Contains(p)))
                    Assert.True(screens[placement.Index].Full.Contains(p)); // a screen that holds the pointer always wins
            }

            Assert.InRange(ScreenFit.Tiles(w, r.Next(-2, 20)), 0, 20);
        }
    }
}
