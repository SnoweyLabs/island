using Island.Core;

namespace Island.Attack10.Tests;

/// <summary>WORK-ORDER-10 section 1 attacked: the colour rule, the layout, the cache. Icons are drawn in code; nothing here is a real icon.</summary>
public class RoundIconAttackTests
{
    private static bool InEllipse(int x, int y, double rx, double ry)
    {
        var dx = (x + 0.5 - 32) / rx;
        var dy = (y + 0.5 - 32) / ry;
        return dx * dx + dy * dy <= 1;
    }

    private static IconImage Ellipse(byte r, byte g, byte b) =>
        Make.Icon(64, 64, (x, y) => InEllipse(x, y, 22, 15) ? (r, g, b, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));

    // ---- Defects -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_A_Pale_Flat_Shape_Is_Drawn_White_On_A_Disc_Almost_As_Pale()
    {
        // A flat shape is turned white and only a main colour with EVERY channel at 230 or more gets the dark disc. rgb(240,240,200) has a channel below 230, so the shape
        // is drawn white on a disc rgb(240,240,200): no channel differs by 60 (the rule's own measure of "visible", VisibleDifference), so the logo all but vanishes.
        var icon = Ellipse(240, 240, 200);
        var plan = RoundIconRule.Analyse(icon);

        Assert.Equal(RoundIconKind.FlatShape, plan.Kind);
        Assert.True(plan.DrawnWhite);
        var drawn = RoundIconRule.Whiten(icon.Bgra);
        Assert.True(RoundIconRule.StaysVisibleOn(plan.Disc, icon.Width, icon.Height, drawn), $"white on disc ({plan.Disc.R},{plan.Disc.G},{plan.Disc.B}) is not visible by the rule's own measure");
    }

    [Fact]
    public void Defect_Pale_Flat_Shapes_Across_The_Colour_Cube_Vanish_On_Their_Own_Disc()
    {
        // Sweep: every flat shape whose white drawing would not stay visible on its own disc. The rule never checks this for a flat shape (it checks it only for "any other icon").
        var failing = new List<string>();
        for (var r = 150; r <= 255; r += 15)
        for (var g = 150; g <= 255; g += 15)
        for (var b = 150; b <= 255; b += 15)
        {
            var icon = Ellipse((byte)r, (byte)g, (byte)b);
            var plan = RoundIconRule.Analyse(icon);
            if (plan.Kind != RoundIconKind.FlatShape) continue;
            if (!RoundIconRule.StaysVisibleOn(plan.Disc, 64, 64, RoundIconRule.Whiten(icon.Bgra))) failing.Add($"{r},{g},{b}");
        }

        Assert.True(failing.Count == 0, $"{failing.Count} pale colours, first {string.Join(" | ", failing.Take(3))}");
    }

    [Fact]
    public void Defect_A_Closed_Tile_Loses_An_Icon_Whose_Colours_Share_One_Brightness()
    {
        // A plate of green with a red square of the same brightness (luma 113). Open, the square is plainly visible (208 apart in red). Closed, the disc and the picture are both
        // turned grey by luma, so square and disc come out the same grey: the closed tile is a blank grey disc.
        var icon = Make.Icon(64, 64, (x, y) => x is >= 17 and < 47 && y is >= 17 and < 47 ? ((byte)238, (byte)60, (byte)60, (byte)255) : ((byte)30, (byte)160, (byte)90, (byte)255));
        var look = RoundIcons.Of(icon);
        Assert.Equal(RoundIconKind.Plate, look.Plan.Kind);
        var centreOpen = (32 * 64 + 32) * 4;
        Assert.True(Math.Abs(icon.Bgra[centreOpen + 2] - look.Plan.Disc.R) >= RoundIconConstants.VisibleDifference); // open: the square is plainly told from the disc

        var discGrey = look.ClosedDisc.G;
        var grey = GreyIcons.Of(look.Drawn);
        var centre = (32 * 64 + 32) * 4;
        var difference = Math.Abs(grey.Bgra[centre + 1] - discGrey);
        Assert.True(difference > 6, $"closed: the square is grey {grey.Bgra[centre + 1]}, the disc grey {discGrey}");
    }

    [Fact]
    public void Holds_A_White_Grey_And_Black_Logo_Stays_Visible_On_Its_Disc()
    {
        // A ring that is 40 percent white, 30 percent mid grey and 30 percent black: the hardest ordinary case for the step rule (the disc must keep 60 away from all three). It does:
        // white darkens to 194, which is 61 from white, 66 from grey and far from black.
        var icon = Make.Icon(64, 64, (x, y) =>
        {
            var dx = x + 0.5 - 32;
            var dy = y + 0.5 - 32;
            var d2 = dx * dx + dy * dy;
            if (d2 < 12 * 12 || d2 > 26 * 26) return (0, 0, 0, (byte)0);
            var turn = (Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI); // 0..1
            var tone = turn < 0.4 ? (byte)255 : turn < 0.7 ? (byte)128 : (byte)0;
            return (tone, tone, tone, (byte)255);
        });
        var plan = RoundIconRule.Analyse(icon);
        Assert.Equal(RoundIconKind.Other, plan.Kind);
        Assert.True(RoundIconRule.StaysVisibleOn(plan.Disc, 64, 64, icon.Bgra), $"disc ({plan.Disc.R},{plan.Disc.G},{plan.Disc.B}) hides part of the logo");
    }

    [Fact]
    public void Defect_An_Icon_Drawn_At_Its_Own_Size_Can_Sit_On_Half_A_Device_Pixel()
    {
        // "Drawn at its own size, centred, never stretched" (sharpness, EVALS I5). A 17 pixel icon on the 40 wide tile at 100 percent gets x = 11.5: the picture is sampled across two pixels
        // and blurs. Every size the fits-in-the-box case can meet should land on a whole device pixel.
        var half = new List<string>();
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 3.0 })
        for (var size = 1; size <= 26; size++)
        {
            var box = RoundIconLayout.PlaceOnPixels(size, size, LookConstants.ItemSize, scale);
            if (box.Width * scale < size - 1e-9) continue; // fitted, not own size
            var device = box.X * scale;
            if (Math.Abs(device - Math.Round(device)) > 1e-6) half.Add($"{size}px at {scale}");
        }

        Assert.True(half.Count == 0, $"{half.Count} own-size icons on a fractional pixel, first {string.Join(" | ", half.Take(3))}");
    }

    // ---- Holds -------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_One_Pixel_Icons()
    {
        Assert.Equal(RoundIconKind.Plate, RoundIconRule.Analyse(Make.Solid(1, 1, 10, 200, 30)).Kind);
        Assert.Equal(new RoundIconColour(10, 200, 30), RoundIconRule.Analyse(Make.Solid(1, 1, 10, 200, 30)).Disc);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(Make.Solid(1, 1, 10, 200, 30, 0)).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(Make.Solid(1, 1, 10, 200, 30, 127)).Kind);
        Assert.Equal(RoundIconKind.Plate, RoundIconRule.Analyse(Make.Solid(1, 1, 10, 200, 30, 128)).Kind);
        Assert.NotNull(RoundIcons.Of(Make.Solid(1, 1, 255, 255, 255)));
    }

    [Fact]
    public void Holds_Half_Transparent_Edges_Of_The_Line()
    {
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(Make.Solid(32, 32, 200, 50, 50, 127)).Kind);
        Assert.Equal(RoundIconKind.Plate, RoundIconRule.Analyse(Make.Solid(32, 32, 200, 50, 50, 128)).Kind);
        // alternate 127 / 128 by column: half the pixels count, they fill every second column of the box, so it is a flat shape or a plate, never a crash
        var stripes = Make.Icon(32, 32, (x, _) => (200, 50, 50, (byte)(x % 2 == 0 ? 127 : 128)));
        var plan = RoundIconRule.Analyse(stripes);
        Assert.NotEqual(RoundIconKind.Letters, plan.Kind);
    }

    [Fact]
    public void Holds_Alpha_Ramp_And_Fully_Transparent_And_Huge_Legal_Icons()
    {
        var ramp = Make.Icon(256, 8, (x, _) => (30, 120, 220, (byte)x));
        Assert.NotEqual(RoundIconKind.Letters, RoundIconRule.Analyse(ramp).Kind);

        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(Make.Solid(512, 512, 1, 2, 3, 0)).Kind);

        var rng = new Random(7);
        var big = new byte[2048 * 2048 * 4];
        rng.NextBytes(big);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var plan = RoundIconRule.Analyse(2048, 2048, big);
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds < 5000, $"{clock.ElapsedMilliseconds} ms");
        Assert.NotEqual(RoundIconKind.Letters, plan.Kind);
    }

    [Fact]
    public void Holds_Wrong_Sizes_Negative_Sizes_And_Overflowing_Claims_Give_Letters()
    {
        var four = new byte[16];
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(2, 2, []).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(2, 2, new byte[15]).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(2, 2, new byte[17]).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(-2, -2, four).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(0, 4, four).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(int.MinValue, int.MinValue, four).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(int.MaxValue, int.MaxValue, four).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(65536, 65536, four).Kind); // 2^32 pixels: width * height * 4 wraps to 0 in an int
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(46341, 46341, four).Kind); // width * height wraps in an int
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(1 << 30, 1, four).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(2049, 2048, new byte[2049 * 2048 * 4]).Kind); // one row past the cap
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse((IconImage?)null).Kind);
        Assert.True(RoundIconRule.StaysVisibleOn(new RoundIconColour(1, 2, 3), 2, 2, new byte[3]));
        Assert.Equal(new RoundIconColour(9, 9, 9), RoundIconRule.MakeVisible(new RoundIconColour(9, 9, 9), -1, 5, four));
        Assert.Empty(RoundIconRule.Whiten([]));
        Assert.Equal(3, RoundIconRule.Whiten(new byte[3]).Length); // a stray tail is left alone, no exception
    }

    [Fact]
    public void Holds_Random_Noise_Never_Throws_And_Gives_The_Same_Answer_Twice()
    {
        var rng = new Random(20261007);
        for (var n = 0; n < 400; n++)
        {
            var w = rng.Next(1, 70);
            var h = rng.Next(1, 70);
            var bytes = new byte[w * h * 4];
            rng.NextBytes(bytes);
            var mode = rng.Next(4);
            for (var i = 3; i < bytes.Length; i += 4)
            {
                if (mode == 1) bytes[i] = (byte)(rng.Next(2) == 0 ? 0 : 255);
                else if (mode == 2) bytes[i] = (byte)(126 + rng.Next(4));
                else if (mode == 3) bytes[i] = 255;
            }

            var first = RoundIconRule.Analyse(w, h, bytes);
            var second = RoundIconRule.Analyse(w, h, bytes);
            Assert.Equal(first, second);
            var icon = new IconImage(w, h, bytes);
            var look = RoundIcons.Of(icon);
            Assert.Equal(first, look.Plan);
            Assert.Equal(w * h * 4, look.Drawn.Bgra.Length);
            Assert.Same(look, RoundIcons.Of(icon));
            if (first.Kind == RoundIconKind.Letters)
                Assert.DoesNotContain(Enumerable.Range(0, w * h).Select(p => bytes[p * 4 + 3]), a => a >= RoundIconConstants.OpaqueAlphaLine);
        }
    }

    [Fact]
    public void Holds_The_Rule_Run_Twice_And_On_Its_Own_Output_Does_Not_Change_The_Input()
    {
        foreach (var icon in RoundIconSamples.All)
        {
            var before = (byte[])icon.Bgra.Clone();
            var first = RoundIconRule.Analyse(icon);
            var second = RoundIconRule.Analyse(icon);
            Assert.Equal(first, second);
            Assert.Equal(before, icon.Bgra);
            var look = RoundIcons.Of(icon);
            Assert.Equal(before, icon.Bgra); // the white copy is a copy
            if (first.DrawnWhite) Assert.NotSame(icon.Bgra, look.Drawn.Bgra);
        }
    }

    [Fact]
    public void Holds_Many_Threads_Asking_For_One_Icon_Get_One_Answer()
    {
        var icon = Make.Icon(64, 64, (x, y) => ((byte)(x * 4), (byte)(y * 4), (byte)128, (byte)255));
        var looks = new RoundIconLook[64];
        Parallel.For(0, looks.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i => looks[i] = RoundIcons.Of(icon));
        Assert.Single(looks.Distinct());
        Assert.Same(looks[0].Drawn, RoundIcons.Of(icon).Drawn);

        // and many icons at once, each its own
        var icons = Enumerable.Range(0, 200).Select(i => Make.Solid(8, 8, (byte)i, (byte)(255 - i), 40)).ToArray();
        var made = new RoundIconLook[icons.Length];
        Parallel.For(0, icons.Length, i => made[i] = RoundIcons.Of(icons[i]));
        for (var i = 0; i < icons.Length; i++) Assert.Equal(new RoundIconColour((byte)i, (byte)(255 - i), 40), made[i].Plan.Disc);
    }

    [Fact]
    public void Holds_The_Grey_Disc_Stays_Grey_And_In_Range()
    {
        foreach (var r in new byte[] { 0, 1, 127, 128, 254, 255 })
        foreach (var g in new byte[] { 0, 127, 255 })
        foreach (var b in new byte[] { 0, 255 })
        {
            var grey = RoundIcons.Grey(new RoundIconColour(r, g, b));
            Assert.Equal(grey.R, grey.G);
            Assert.Equal(grey.G, grey.B);
        }

        Assert.Equal(0, RoundIcons.Grey(new RoundIconColour(0, 0, 0)).R);
        Assert.True(RoundIcons.Grey(new RoundIconColour(255, 255, 255)).R <= 255 * ChoiceConstants.ClosedBrightness + 1);
    }

    [Fact]
    public void Holds_Layout_Is_Finite_And_Inside_The_Tile_For_Every_Odd_Input()
    {
        var sizes = new[] { int.MinValue, -5, -1, 0, 1, 2, 7, 16, 17, 33, 255, 256, 4096, int.MaxValue };
        var doubles = new[] { double.NaN, double.NegativeInfinity, -3.0, -0.0, 0.0, double.Epsilon, 1e-300, 0.5, 1.0, 1.25, 1.75, 2.0, 7.9, 1e9, 1e300, double.MaxValue, double.PositiveInfinity };
        var tiles = new[] { double.NaN, -1, 0, 1, 40, 1e6, double.MaxValue, double.PositiveInfinity };
        foreach (var w in sizes)
        foreach (var h in sizes)
        foreach (var scale in doubles)
        foreach (var tile in tiles)
        {
            var box = RoundIconLayout.Place(w, h, tile, scale);
            foreach (var v in new[] { box.X, box.Y, box.Width, box.Height })
                Assert.True(double.IsFinite(v), $"{w}x{h} scale {scale} tile {tile}");
            var largest = double.IsFinite(tile) && tile > 0 ? tile * RoundIconConstants.IconBoxFraction : 0;
            Assert.InRange(box.Width, 0, largest + 1e-9);
            Assert.InRange(box.Height, 0, largest + 1e-9);
            Assert.True(box.X >= -1e-9 && box.Y >= -1e-9, $"{w}x{h} scale {scale} tile {tile}");
        }
    }

    [Fact]
    public void Holds_Layout_Never_Stretches_And_Keeps_The_Aspect()
    {
        var rng = new Random(11);
        for (var n = 0; n < 5000; n++)
        {
            var w = rng.Next(1, 600);
            var h = rng.Next(1, 600);
            var scale = 0.5 + rng.NextDouble() * 4;
            var box = RoundIconLayout.Place(w, h, 40, scale);
            Assert.True(box.Width * scale <= w + 1e-6 && box.Height * scale <= h + 1e-6, $"{w}x{h} at {scale}");
            Assert.True(Math.Abs((double)w / h - box.Width / box.Height) < 1e-9, $"{w}x{h} at {scale}");
            Assert.Equal((40 - box.Width) / 2, box.X, 9);
            Assert.Equal((40 - box.Height) / 2, box.Y, 9);
        }
    }
}
