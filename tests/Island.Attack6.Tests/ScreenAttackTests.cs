using Island.Core;

namespace Island.Attack6.Tests;

/// <summary>WO6 section 1: ScreenChooser, ScreenPicker, ScreenFit, ScreenInfo and StripLayout.VisibleLimit against absurd layouts.</summary>
public class ScreenAttackTests
{
    private const double W = 560; // an island-sized window in dip
    private const double H = 120;

    private static ScreenInfo Screen(int l, int t, int r, int b, double scale = 1.0, bool primary = false, int taskbarTop = 0, int taskbarBottom = 0) =>
        new(new PixelRect(l, t, r, b), new PixelRect(l, t + taskbarTop, r, b - taskbarBottom), scale, primary);

    private static bool Inside(PixelRect inner, PixelRect outer) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    // ---- never throws, always usable ----------------------------------------------------------------------------

    public static IEnumerable<object?[]> AbsurdLists()
    {
        yield return [null];
        yield return [Array.Empty<ScreenInfo>()];
        yield return [new[] { new ScreenInfo(new PixelRect(0, 0, 0, 0), new PixelRect(0, 0, 0, 0), 1) }];
        yield return [new[] { new ScreenInfo(new PixelRect(10, 10, 5, 5), new PixelRect(10, 10, 5, 5), 1) }];
        yield return [new[] { new ScreenInfo(new PixelRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), new PixelRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue), double.NaN) }];
        yield return [new[] { new ScreenInfo(new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 100), double.PositiveInfinity) }];
        yield return [new[] { new ScreenInfo(new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 100), -2) }];
        yield return [new[] { new ScreenInfo(new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 100), 0) }];
        yield return [new[] { new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(int.MaxValue - 5, int.MaxValue - 5, int.MaxValue, int.MaxValue), 1) }];
        yield return [Enumerable.Range(0, 40).Select(i => Screen(i * 1920, 0, (i + 1) * 1920, 1080)).ToArray()];
        yield return [Enumerable.Range(0, 4000).Select(i => Screen(i * 10, 0, i * 10 + 10, 10)).ToArray()];
    }

    public static IEnumerable<object?[]> Pointers()
    {
        yield return [null];
        yield return [new ScreenPoint(0, 0)];
        yield return [new ScreenPoint(int.MinValue, int.MinValue)];
        yield return [new ScreenPoint(int.MaxValue, int.MaxValue)];
        yield return [new ScreenPoint(int.MinValue, int.MaxValue)];
        yield return [new ScreenPoint(-1, -1)];
        yield return [new ScreenPoint(1919, 1079)];
        yield return [new ScreenPoint(1920, 1080)];
    }

    [Theory]
    [MemberData(nameof(AbsurdLists))]
    public void Holds_The_Chooser_Never_Throws_And_Always_Gives_A_Usable_Window_For_Absurd_Lists(IReadOnlyList<ScreenInfo>? screens)
    {
        foreach (var pointerRow in Pointers())
        {
            var pointer = (ScreenPoint?)pointerRow[0];
            foreach (var (w, h) in new[] { (W, H), (0.0, 0.0), (-5.0, -5.0), (double.NaN, double.NaN), (double.PositiveInfinity, double.PositiveInfinity), (1e300, 1e300), (1.0, 1.0) })
            {
                var p = ScreenChooser.Choose(pointer, screens, w, h);
                Assert.True(p.Window.HasArea, $"window {p.Window} has no pixels");
                Assert.True(p.Window.Width <= 1_000_000 && p.Window.Height <= 1_000_000);
                if (screens is null || !screens.Take(ScreenChooser.MaxScreens).Any(s => s.IsUsable)) Assert.True(p.IsFallback && p.Index == -1);
                else
                {
                    Assert.False(p.IsFallback);
                    Assert.InRange(p.Index, 0, Math.Min(screens.Count, ScreenChooser.MaxScreens) - 1);
                    Assert.True(screens[p.Index].IsUsable);
                }
            }
        }
    }

    [Fact]
    public void Holds_No_Screen_At_All_Gives_The_Flagged_Fallback_Centred_At_The_Top()
    {
        var p = ScreenChooser.Choose(new ScreenPoint(50, 50), [], W, H);
        Assert.True(p.IsFallback);
        Assert.Equal(-1, p.Index);
        Assert.Equal(ScreenInfo.Fallback, p.Screen);
        Assert.Equal(new PixelRect(680, 0, 1240, 120), p.Window);
    }

    [Fact]
    public void Holds_A_Screen_Of_Zero_Or_Negative_Size_Is_Skipped_For_A_Usable_One()
    {
        var screens = new[]
        {
            new ScreenInfo(new PixelRect(0, 0, 0, 1080), new PixelRect(0, 0, 0, 1080), 1, true),
            new ScreenInfo(new PixelRect(500, 500, 100, 100), new PixelRect(500, 500, 100, 100), 1),
            Screen(2000, 0, 3920, 1080),
        };
        foreach (var pointer in new ScreenPoint?[] { null, new ScreenPoint(0, 0), new ScreenPoint(500, 500) })
            Assert.Equal(2, ScreenChooser.Choose(pointer, screens, W, H).Index);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    public void Holds_A_Bad_Scale_Reads_As_One_Hundred_Percent(double scale)
    {
        var screens = new[] { Screen(0, 0, 1920, 1080, scale) };
        var bad = ScreenChooser.Choose(null, screens, W, H);
        var good = ScreenChooser.Choose(null, [Screen(0, 0, 1920, 1080, 1.0)], W, H);
        Assert.Equal(good.Window, bad.Window);
        Assert.Equal(1.0, screens[0].SafeScale);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.33)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    [InlineData(2.25)]
    [InlineData(3.0)]
    [InlineData(4.0)]
    [InlineData(5.0)]
    [InlineData(1.0001)]
    public void Holds_The_Window_Is_The_Same_Size_In_Units_On_Every_Scale_And_Fits_The_Work_Area_Centred(double scale)
    {
        var screen = Screen(-3840, -200, 0, 1960, scale, taskbarTop: 48);
        var p = ScreenChooser.Choose(new ScreenPoint(-100, 100), [screen], W, H);
        Assert.True(Inside(p.Window, screen.Work), $"{p.Window} not inside {screen.Work}");
        Assert.Equal(screen.Work.Top, p.Window.Top); // below a top bar, never over it
        Assert.InRange(p.Window.Width / scale, W - 1 / scale, W + 1 / scale);
        Assert.InRange(p.Window.Height / scale, H - 1 / scale, H + 1 / scale);
        var left = p.Window.Left - screen.Work.Left;
        var right = screen.Work.Right - p.Window.Right;
        Assert.InRange(left - right, -1, 1);
    }

    [Fact]
    public void Holds_Pointer_Edges_Are_Half_Open_And_Overlap_Takes_The_First()
    {
        var a = Screen(0, 0, 1920, 1080);
        var b = Screen(1920, 0, 3840, 1080);
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(1919, 5), [a, b]));
        Assert.Equal(1, ScreenChooser.ChooseIndex(new ScreenPoint(1920, 5), [a, b]));
        var overlap = Screen(1000, 0, 2000, 1080);
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(1500, 5), [a, overlap]));
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(1500, 5), [a, overlap, b]));
    }

    [Fact]
    public void Holds_A_Pointer_On_No_Screen_Takes_The_Nearest_And_A_Tie_The_First()
    {
        var a = Screen(0, 0, 1000, 1000);
        var b = Screen(3001, 0, 4000, 1000);
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(1500, 500), [a, b]));
        Assert.Equal(1, ScreenChooser.ChooseIndex(new ScreenPoint(2600, 500), [a, b]));
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(2000, 500), [a, b]));   // 1001 from the last pixel of each: a tie, the first
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(2000, 500), [a, b]));
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(2000, 500), [b, a])); // reversed list: the tie keeps the first, now b
    }

    [Fact]
    public void Holds_A_Pointer_At_Int_Extremes_Takes_The_Nearest_Without_Overflow()
    {
        var a = Screen(0, 0, 1000, 1000);
        var b = Screen(2_000_000_000, 2_000_000_000, 2_100_000_000, 2_100_000_000);
        Assert.Equal(1, ScreenChooser.ChooseIndex(new ScreenPoint(int.MaxValue, int.MaxValue), [a, b]));
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(int.MinValue, int.MinValue), [a, b]));
    }

    [Fact]
    public void Holds_A_Window_Wider_Than_Every_Screen_Stays_Centred_And_Overhangs_Both_Sides_Equally()
    {
        var screen = Screen(0, 0, 400, 800);
        var p = ScreenChooser.Choose(null, [screen], 1000, 100);
        Assert.Equal(1000, p.Window.Width);
        Assert.InRange(p.Window.Left - 0 + (p.Window.Right - 400), -1, 1); // left overhang = -(Left), right overhang = Right-400; Left+Right-400 ~ 0
        Assert.Equal(0, p.Window.Top);
    }

    [Fact]
    public void Holds_A_Screen_That_Vanishes_Between_Two_Calls_Gives_A_Fresh_Valid_Choice()
    {
        var two = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920, 0, 3840, 1080) };
        var one = new[] { two[0] };
        var first = ScreenChooser.Choose(new ScreenPoint(2000, 5), two, W, H);
        var second = ScreenChooser.Choose(new ScreenPoint(2000, 5), one, W, H);
        Assert.Equal(1, first.Index);
        Assert.Equal(0, second.Index);
        Assert.True(Inside(second.Window, one[0].Work));
        Assert.False(second.IsFallback);
    }

    [Fact]
    public void Holds_Forty_Screens_Pick_By_Pointer()
    {
        var screens = Enumerable.Range(0, 40).Select(i => Screen(i * 1920, 0, (i + 1) * 1920, 1080)).ToArray();
        for (var i = 0; i < 40; i++) Assert.Equal(i, ScreenChooser.ChooseIndex(new ScreenPoint(i * 1920 + 7, 9), screens));
    }

    [Fact]
    public void Holds_More_Than_MaxScreens_Looks_Only_At_The_First_MaxScreens_As_Documented()
    {
        var screens = Enumerable.Range(0, 100).Select(i => Screen(i * 100, 0, i * 100 + 100, 100)).ToArray();
        Assert.Equal(ScreenChooser.MaxScreens - 1, ScreenChooser.ChooseIndex(new ScreenPoint(9950, 5), screens));
    }

    [Fact]
    public void Holds_Pointer_Unreadable_Takes_Primary_Else_First_Usable()
    {
        var screens = new[] { Screen(0, 0, 100, 100), Screen(100, 0, 200, 100, primary: true), Screen(200, 0, 300, 100, primary: true) };
        Assert.Equal(1, ScreenChooser.ChooseIndex(null, screens));
        Assert.Equal(0, ScreenChooser.ChooseIndex(null, [screens[0], screens[0]]));
    }

    // ---- a work area that is not inside the full rectangle ------------------------------------------------------

    [Fact]
    public void Defect_A_Work_Area_Outside_The_Full_Rectangle_Puts_The_Window_Off_The_Screen()
    {
        // Windows never reports this today; the chooser trusts the work area completely. The window should still
        // land on the chosen screen (work area cut to the full rectangle).
        var screen = new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(5000, 0, 6920, 1080), 1.0, true);
        var p = ScreenChooser.Choose(new ScreenPoint(10, 10), [screen], W, H);
        Assert.True(Inside(p.Window, screen.Full), $"window {p.Window} is not inside the screen {screen.Full}");
    }

    [Fact]
    public void Defect_A_Work_Area_Larger_Than_The_Full_Rectangle_Hangs_The_Window_Over_Its_Edge()
    {
        var screen = new ScreenInfo(new PixelRect(0, 0, 400, 400), new PixelRect(-2000, -50, 2000, 400), 1.0, true);
        var p = ScreenChooser.Choose(null, [screen], 300, 100);
        Assert.True(Inside(p.Window, screen.Full), $"window {p.Window} is not inside the screen {screen.Full}");
    }

    [Fact]
    public void Holds_A_Taller_Window_Than_The_Work_Area_Is_Only_Top_Aligned()
    {
        var screen = Screen(0, 0, 800, 100, taskbarBottom: 10);
        var p = ScreenChooser.Choose(null, [screen], 300, 500);
        Assert.Equal(0, p.Window.Top);
        Assert.Equal(500, p.Window.Height);
    }

    [Fact]
    public void Holds_A_Work_Area_Without_Pixels_Is_Replaced_By_The_Full_Rectangle()
    {
        var screen = new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 0, 0), 1.0, true);
        var p = ScreenChooser.Choose(null, [screen], W, H);
        Assert.True(Inside(p.Window, screen.Full));
    }

    [Fact]
    public void Holds_A_Window_At_The_Top_Of_A_Work_Area_Near_Int_Max_Is_Clamped_Not_Wrapped()
    {
        var screen = new ScreenInfo(new PixelRect(int.MaxValue - 1000, int.MaxValue - 1000, int.MaxValue, int.MaxValue), new PixelRect(int.MaxValue - 1000, int.MaxValue - 1000, int.MaxValue, int.MaxValue), 1.0, true);
        var p = ScreenChooser.Choose(null, [screen], 500, 500);
        Assert.True(p.Window.HasArea);
        Assert.True(p.Window.Right <= int.MaxValue && p.Window.Bottom <= int.MaxValue);
    }

    // ---- the picker ---------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_While_Visible_The_Placement_Never_Changes_Whatever_The_Screens_And_Pointer_Do()
    {
        var picker = new ScreenPicker();
        var two = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920, 0, 3840, 1080) };
        var first = picker.Update(new ScreenPoint(10, 10), two, visible: false, W, H);
        for (var i = 0; i < 5000; i++)
        {
            var screens = i % 3 == 0 ? two : i % 3 == 1 ? [] : (IReadOnlyList<ScreenInfo>?)null;
            var got = picker.Update(new ScreenPoint(i % 4000, 5), screens, visible: true, W * (1 + i % 5), H);
            Assert.Equal(first, got);
        }

        Assert.Equal(1, picker.Choices);
    }

    [Fact]
    public void Holds_Alternating_Visible_And_Hidden_Chooses_Exactly_On_Every_Hidden_Frame()
    {
        var picker = new ScreenPicker();
        var two = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920, 0, 3840, 1080) };
        for (var i = 0; i < 4000; i++)
        {
            var hiddenAt = new ScreenPoint(i % 2 == 0 ? 10 : 2000, 5);
            var shown = picker.Update(hiddenAt, two, visible: false, W, H);
            Assert.Equal(i % 2 == 0 ? 0 : 1, shown.Index);
            var kept = picker.Update(new ScreenPoint(i % 2 == 0 ? 2000 : 10, 5), two, visible: true, W, H);
            Assert.Equal(shown, kept);
        }

        Assert.Equal(4000, picker.Choices);
    }

    [Fact]
    public void Holds_Visible_With_Nothing_Chosen_Yet_Chooses_Once_Then_Keeps_It()
    {
        var picker = new ScreenPicker();
        var two = new[] { Screen(0, 0, 1920, 1080, primary: true), Screen(1920, 0, 3840, 1080) };
        var a = picker.Update(new ScreenPoint(2000, 5), two, visible: true, W, H);
        var b = picker.Update(new ScreenPoint(10, 5), two, visible: true, W, H);
        Assert.Equal(1, a.Index);
        Assert.Equal(a, b);
        Assert.Equal(1, picker.Choices);
    }

    [Fact]
    public void Holds_The_Picker_Survives_Absurd_Input_On_Every_Frame()
    {
        var picker = new ScreenPicker();
        foreach (var row in AbsurdLists())
            for (var i = 0; i < 20; i++)
            {
                var p = picker.Update(i % 2 == 0 ? null : new ScreenPoint(int.MinValue, int.MaxValue), (IReadOnlyList<ScreenInfo>?)row[0], visible: i % 3 == 0, double.NaN, -1);
                Assert.True(p.Window.HasArea);
            }
    }

    // ---- fit ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Tile_Count_Is_Monotone_In_Width_And_Within_One_And_Wanted()
    {
        var last = 0;
        for (var width = 0.0; width <= 3000; width += 7)
        {
            var n = ScreenFit.Tiles(width, 7);
            Assert.InRange(n, 1, 7);
            Assert.True(n >= last, $"count fell from {last} to {n} at width {width}");
            last = n;
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-5)]
    [InlineData(0)]
    public void Holds_A_Bad_Or_Narrowest_Width_Gives_One_Tile(double width) => Assert.Equal(1, ScreenFit.Tiles(width, 7));

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    public void Holds_Nothing_Wanted_Gives_Zero(int wanted, int expected) => Assert.Equal(expected, ScreenFit.Tiles(1920, wanted));

    [Fact]
    public void Holds_Huge_Width_Or_Huge_Wanted_Is_Bounded_And_Never_Throws()
    {
        Assert.InRange(ScreenFit.Tiles(double.PositiveInfinity, int.MaxValue), 7, 200);
        Assert.Equal(1, ScreenFit.Tiles(1920, 7, double.MaxValue));
        Assert.Equal(ScreenFit.Tiles(1920, 7), ScreenFit.Tiles(1920, 7, double.NaN));
        Assert.Equal(ScreenFit.Tiles(1920, 7), ScreenFit.Tiles(1920, 7, double.NegativeInfinity));
        Assert.Equal(ScreenFit.Tiles(1920, 7), ScreenFit.Tiles(1920, 7, double.PositiveInfinity));
    }

    [Fact]
    public void Holds_Tiles_Of_A_Chosen_Screen_Follow_Its_Work_Width_In_Units()
    {
        var narrow = Screen(0, 0, 600, 800, scale: 1.0);
        var sameInUnits = Screen(0, 0, 1200, 1600, scale: 2.0);
        Assert.Equal(ScreenFit.Tiles(narrow, 7), ScreenFit.Tiles(sameInUnits, 7));
        Assert.Equal(1, ScreenFit.Tiles(new ScreenInfo(default, default, double.NaN), 7));
    }

    [Fact]
    public void Holds_Every_Count_The_Fit_Allows_Really_Fits_The_Capsule_In_The_Work_Width()
    {
        for (var width = 100; width <= 2500; width += 13)
        {
            var n = ScreenFit.Tiles(width, 7);
            if (n > 1)
            {
                Assert.True(CapsuleLayout.Width(n, isMedia: false) <= width, $"{n} tiles do not fit {width}");
                Assert.True(CapsuleLayout.Width(n, isMedia: true) <= width, $"{n} tiles (media) do not fit {width}");
            }
        }
    }

    [Fact]
    public void Holds_VisibleLimit_Is_Clamped_To_One_To_Seven_And_Is_Safe_From_Many_Threads()
    {
        var before = StripLayout.VisibleLimit;
        try
        {
            StripLayout.VisibleLimit = int.MinValue;
            Assert.Equal(1, StripLayout.VisibleLimit);
            StripLayout.VisibleLimit = int.MaxValue;
            Assert.Equal(ChoiceConstants.MaxVisibleTiles, StripLayout.VisibleLimit);
            StripLayout.VisibleLimit = 0;
            Assert.Equal(1, StripLayout.VisibleLimit);
            Parallel.For(0, 2000, i => StripLayout.VisibleLimit = i - 1000);
            Assert.InRange(StripLayout.VisibleLimit, 1, 7);
            StripLayout.VisibleLimit = 3;
            Assert.Equal(3, StripLayout.VisiblePicks(50));
            Assert.Equal(0, StripLayout.VisiblePicks(-4));
            Assert.Equal(2, StripLayout.VisiblePicks(50, 2));
            Assert.Equal(0, StripLayout.VisiblePicks(50, -9));
        }
        finally
        {
            StripLayout.VisibleLimit = before;
        }
    }
}
