using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>The settings pictures the self-test drew (1920 by 1080, 1 pixel to a dp), measured: what lines up and what does not.</summary>
public class SettingsPictureTests(ITestOutputHelper output)
{
    /// <summary>The right-most pixel that differs from the card's own fill within a band: where the controls of a card end.</summary>
    private static int RightInk(Pic p, int y0, int y1, int x0, int x1)
    {
        var right = -1;
        for (var y = y0; y <= y1; y++)
        {
            var baseline = p.Lum(x1, y); // the card's fill at the far right of the band (the card's own padding, no ink)
            for (var x = x1; x >= x0; x--)
                if (Math.Abs(p.Lum(x, y) - baseline) > 0.02) { right = Math.Max(right, x); break; }
        }

        return right;
    }

    [Fact]
    public void The_Controls_Of_The_General_Cards_End_On_One_Line()
    {
        var p = Pic.TryLoad("review/settings/general.png");
        if (p is null) return;
        // The six cards that have a control or a status at the right (their rows, from the picture): start with Windows, idle time, notice time, pill, browser add-on, run the setup again.
        // WORK-ORDER-13: the "Moving light" card is gone (Dan's Q1), so the rows after notice time moved up by one card; the add-on card is taller by its button.
        var bands = new[] { (265, 305), (368, 408), (471, 511), (574, 614), (702, 742), (830, 870) };
        var ends = bands.Select(b => RightInk(p, b.Item1, b.Item2, 1100, 1300)).ToList();
        output.WriteLine("right edge of the controls: " + string.Join(", ", ends));
        Assert.True(ends.All(e => e > 0));
        Assert.InRange(ends.Max() - ends.Min(), 0, 2);
    }

    /// <summary>
    /// The heading's top, read from the pictures themselves: the first row below the step island with a run of near-white pixels the width of a 34 px heading. Agrees with the layout test (82 on a page that
    /// scrolls, up to 427 on a short one).
    /// </summary>
    [Fact]
    public void The_Heading_Top_In_The_Settings_Pictures_Is_Not_Where_The_Last_One_Was()
    {
        var tops = new List<(string, int)>();
        foreach (var name in new[] { "general", "island", "key", "pages", "scenes", "mode", "glass", "agents" })
        {
            var p = Pic.TryLoad($"review/settings/{name}.png");
            if (p is null) continue;
            var top = -1;
            for (var y = 75; y < 700 && top < 0; y++)
            {
                var white = 0;
                for (var x = 600; x < 1320; x++)
                {
                    var c = p.At(x, y);
                    if (c.R > 235 && c.G > 235 && c.B > 235) white++;
                }

                if (white >= 12) top = y;
            }

            tops.Add((name, top));
            output.WriteLine($"{name,-8} heading ink starts at y {top}");
        }

        if (tops.Count < 2) return;
        Assert.InRange(tops.Max(t => t.Item2) - tops.Min(t => t.Item2), 150, 600);
    }

    private static int HeadingTop(Pic p)
    {
        for (var y = 75; y < 700; y++)
        {
            var white = 0;
            for (var x = 600; x < 1320; x++)
            {
                var c = p.At(x, y);
                if (c.R > 235 && c.G > 235 && c.B > 235) white++;
            }

            if (white >= 12) return y;
        }

        return -1;
    }

    /// <summary>The page title is centred on the screen's own middle, but a scroll bar takes room on the right and the centred lines move left by about half of it (a page that scrolls against one that does not).</summary>
    [Fact]
    public void The_Title_Centre_Moves_When_A_Scroll_Bar_Is_Shown()
    {
        double Centre(string name)
        {
            var p = Pic.TryLoad($"review/settings/{name}.png")!;
            var top = HeadingTop(p);
            return p.Bounds(new Box(500, top, 1420, top + 36), (r, g, b, a) => r > 235 && g > 235 && b > 235)!.Value.CentreX;
        }

        if (Pic.TryLoad("review/settings/general.png") is null || Pic.TryLoad("review/settings/pages.png") is null) return;
        var withBar = Centre("general");
        var without = Centre("pages");
        output.WriteLine($"title centre with a scroll bar {withBar}; without {without}; difference {without - withBar}");
        Assert.InRange(without - withBar, 0, 15);
    }
}
