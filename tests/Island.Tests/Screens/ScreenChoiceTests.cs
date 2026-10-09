using Island.Core;

namespace Island.Tests;

/// <summary>EVALS W1, W2 (the choosing half) and M8 (the mixed-scaling half). Invented screens only.</summary>
public class ScreenChoiceTests
{
    private const double WindowW = 600;
    private const double WindowH = 120;

    private static ScreenInfo Screen(int left, int top, int width, int height, double scale = 1.0, int taskbarTop = 0, int taskbarBottom = 0, bool primary = false) =>
        new(new PixelRect(left, top, left + width, top + height),
            new PixelRect(left, top + taskbarTop, left + width, top + height - taskbarBottom),
            scale, primary);

    [Fact]
    public void Pointer_Screen_Is_Chosen()
    {
        var screens = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920, 0, 2560, 1440) };

        var onFirst = ScreenChooser.Choose(new ScreenPoint(100, 100), screens, WindowW, WindowH);
        var onSecond = ScreenChooser.Choose(new ScreenPoint(2000, 700), screens, WindowW, WindowH);
        var onEdge = ScreenChooser.Choose(new ScreenPoint(1920, 0), screens, WindowW, WindowH); // first pixel of the second

        Assert.Equal(0, onFirst.Index);
        Assert.Equal(1, onSecond.Index);
        Assert.Equal(1, onEdge.Index);
        Assert.False(onSecond.IsFallback);
        // Centred on the second screen's work area, at its top.
        Assert.Equal(1920 + (2560 - 600) / 2, onSecond.Window.Left);
        Assert.Equal(0, onSecond.Window.Top);
    }

    [Fact]
    public void Negative_Coordinates_Work()
    {
        // A screen left of the main one, and one above it.
        var screens = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(-1600, 100, 1600, 900), Screen(0, -1080, 1920, 1080) };

        var left = ScreenChooser.Choose(new ScreenPoint(-5, 500), screens, WindowW, WindowH);
        var above = ScreenChooser.Choose(new ScreenPoint(900, -1), screens, WindowW, WindowH);

        Assert.Equal(1, left.Index);
        Assert.Equal(-1600 + (1600 - 600) / 2, left.Window.Left);
        Assert.Equal(100, left.Window.Top);
        Assert.Equal(2, above.Index);
        Assert.Equal(-1080, above.Window.Top);
        Assert.Equal(-1080 + 120, above.Window.Bottom);
        Assert.Equal(600, left.Window.Width);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Mixed_Scaling_Gives_The_Same_Size_In_Units(double scale)
    {
        var screens = new[] { Screen(0, 0, 1920, 1080, 1.0, primary: true), Screen(1920, 0, 3840, 2160, scale) };

        var placement = ScreenChooser.Choose(new ScreenPoint(2500, 500), screens, WindowW, WindowH);

        Assert.Equal(1, placement.Index);
        // Size in pixels is the units times that screen's own scaling (rounded), so in units it is the same everywhere.
        Assert.Equal(Math.Round(WindowW * scale, MidpointRounding.AwayFromZero), placement.Window.Width);
        Assert.Equal(Math.Round(WindowH * scale, MidpointRounding.AwayFromZero), placement.Window.Height);
        Assert.InRange(placement.Window.Width / scale, WindowW - 1, WindowW + 1);
        // Still centred on that screen's work area.
        var centre = (placement.Window.Left + placement.Window.Right) / 2.0;
        Assert.InRange(centre, 1920 + 3840 / 2.0 - 1, 1920 + 3840 / 2.0 + 1);
    }

    [Fact]
    public void Top_Taskbar_Is_Not_Covered()
    {
        // A 48 px bar docked at the top: the window starts under it, not at the screen's top.
        var screens = new[] { Screen(0, 0, 1920, 1080, taskbarTop: 48, primary: true) };

        var placement = ScreenChooser.Choose(new ScreenPoint(10, 10), screens, WindowW, WindowH);

        Assert.Equal(48, placement.Window.Top);
        Assert.Equal(48 + 120, placement.Window.Bottom);
    }

    [Fact]
    public void A_Side_Bar_Moves_The_Centre_With_The_Work_Area()
    {
        // A bar on the left takes 100 px: the middle of the work area is 50 px right of the screen's middle.
        var s = new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(100, 0, 1920, 1080), 1.0, true);

        var placement = ScreenChooser.Choose(new ScreenPoint(10, 10), [s], WindowW, WindowH);

        Assert.Equal(100 + (1820 - 600) / 2, placement.Window.Left);
    }

    [Fact]
    public void No_Screen_Under_The_Pointer_Takes_The_Nearest()
    {
        var screens = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920 + 200, 0, 1920, 1080) }; // a gap of 200 px

        Assert.Equal(0, ScreenChooser.Choose(new ScreenPoint(2000, 500), screens, WindowW, WindowH).Index);
        Assert.Equal(1, ScreenChooser.Choose(new ScreenPoint(2100, 500), screens, WindowW, WindowH).Index);
        Assert.Equal(1, ScreenChooser.Choose(new ScreenPoint(9000, 9000), screens, WindowW, WindowH).Index);
        Assert.Equal(0, ScreenChooser.Choose(new ScreenPoint(-9000, -9000), screens, WindowW, WindowH).Index);
        // Exactly halfway (a gap of 201 px has a middle pixel): the first in the list.
        var odd = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920 + 201, 0, 1920, 1080) };
        Assert.Equal(0, ScreenChooser.Choose(new ScreenPoint(2020, 500), odd, WindowW, WindowH).Index);
    }

    [Fact]
    public void A_Narrow_Screen_Shows_Fewer_Picks()
    {
        var wide = ScreenFit.Tiles(1920, wantedTiles: 8);
        var narrow = ScreenFit.Tiles(500, wantedTiles: 8);
        var narrowest = ScreenFit.Tiles(100, wantedTiles: 8);

        Assert.Equal(8, wide);
        Assert.True(narrow < 8);
        // The capsule with that many tiles fits, with one more tile it would not (media is the wider capsule).
        Assert.True(CapsuleLayout.Width(narrow, isMedia: true) <= 500);
        Assert.True(CapsuleLayout.Width(narrow + 1, isMedia: true) > 500);
        Assert.Equal(1, narrowest); // never none: the capsule is clipped, not absent
    }

    [Fact]
    public void A_Narrow_Screen_Counts_Its_Scaling_In_Units()
    {
        // 1280 px at 200% is 640 units: the same tiles as a 640 px screen at 100%.
        var scaled = new ScreenInfo(new PixelRect(0, 0, 1280, 720), new PixelRect(0, 0, 1280, 680), 2.0);
        var plain = new ScreenInfo(new PixelRect(0, 0, 640, 480), new PixelRect(0, 0, 640, 440), 1.0);

        Assert.Equal(ScreenFit.Tiles(plain, 8), ScreenFit.Tiles(scaled, 8));
        Assert.Equal(640, scaled.WorkWidthDip);
    }

    [Fact]
    public void Reserved_Room_Lowers_The_Count()
    {
        var without = ScreenFit.Tiles(700, 8);
        var with = ScreenFit.Tiles(700, 8, reservedDip: 150);

        Assert.True(with < without);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void Nothing_Wanted_Gives_Nothing(int wanted, int expected) => Assert.Equal(expected, ScreenFit.Tiles(1920, wanted));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(double.NegativeInfinity)]
    public void An_Absurd_Width_Gives_One_Tile(double width) => Assert.Equal(1, ScreenFit.Tiles(width, 8));

    [Fact]
    public void An_Infinite_Width_Gives_What_Was_Wanted() => Assert.Equal(8, ScreenFit.Tiles(double.PositiveInfinity, 8));
}
