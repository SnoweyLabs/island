using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-10 §1: the corners of the colour rule beyond the eight named tests. Invented pixels only.</summary>
public class RoundIconRuleTests
{
    private static RoundIconPlan Plan(IconImage icon) => RoundIconRule.Analyse(icon.Width, icon.Height, icon.Bgra);

    private static IconImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (b, g, r, a);
        return new IconImage(width, height, bgra);
    }

    /// <summary>The ring of the fourth sample in other colours: top left, top right, bottom right, bottom left.</summary>
    private static IconImage Ring((byte R, byte G, byte B) tl, (byte R, byte G, byte B) tr, (byte R, byte G, byte B) br, (byte R, byte G, byte B) bl)
    {
        var bgra = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            double dx = x + 0.5 - 32, dy = y + 0.5 - 32, d2 = dx * dx + dy * dy;
            if (d2 < 144 || d2 > 676) continue;
            var c = dx < 0 ? (dy < 0 ? tl : bl) : (dy < 0 ? tr : br);
            var i = (y * 64 + x) * 4;
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (c.B, c.G, c.R, 255);
        }

        return new IconImage(64, 64, bgra);
    }

    [Fact]
    public void The_Six_Sample_Icons_Get_Exactly_These_Plans()
    {
        var expected = new[]
        {
            new RoundIconPlan(RoundIconKind.Plate, new RoundIconColour(47, 99, 224), false),
            new RoundIconPlan(RoundIconKind.FlatShape, new RoundIconColour(245, 131, 18), true),
            new RoundIconPlan(RoundIconKind.NearlyWhiteFlatShape, new RoundIconColour(40, 42, 50), false),
            new RoundIconPlan(RoundIconKind.Other, new RoundIconColour(150, 41, 41), false), // red darkened four steps of 8 percent
            new RoundIconPlan(RoundIconKind.Plate, new RoundIconColour(30, 160, 90), false),
            RoundIconPlan.Letters,
        };

        Assert.Equal(6, RoundIconSamples.All.Count);
        for (var i = 0; i < 6; i++) Assert.Equal(expected[i], Plan(RoundIconSamples.All[i]));
    }

    [Fact]
    public void The_Samples_Have_The_Sizes_And_Pixel_Counts_Of_The_Work_Order()
    {
        Assert.Equal([(64, 64), (64, 64), (64, 64), (64, 64), (16, 16), (32, 32)], RoundIconSamples.All.Select(s => (s.Width, s.Height)).ToArray());
        Assert.All(RoundIconSamples.All, s => Assert.Equal(s.Width * s.Height * 4, s.Bgra.Length));

        // Hard edges: every pixel is fully opaque or fully transparent; the four ring quarters are exactly equal.
        Assert.All(RoundIconSamples.All, s => Assert.All(Enumerable.Range(0, s.Width * s.Height), p => Assert.Contains(s.Bgra[p * 4 + 3], new byte[] { 0, 255 })));
        var ring = RoundIconSamples.FourColourRing.Bgra;
        var counts = new Dictionary<(byte, byte, byte), int>();
        for (var i = 0; i < ring.Length; i += 4)
            if (ring[i + 3] == 255) counts[(ring[i], ring[i + 1], ring[i + 2])] = counts.GetValueOrDefault((ring[i], ring[i + 1], ring[i + 2])) + 1;
        Assert.Equal(4, counts.Count);
        Assert.Single(counts.Values.Distinct());
        Assert.Equal(0, RoundIconSamples.Nothing.Bgra.Count(v => v != 0));
    }

    [Theory]
    [InlineData(255, 12, 130, 250, RoundIconKind.Plate)] // a 1 x 1 pixel is its own bounding square
    [InlineData(128, 12, 130, 250, RoundIconKind.Plate)] // alpha 128 is the line: it counts
    [InlineData(127, 0, 0, 0, RoundIconKind.Letters)] // alpha 127 does not
    [InlineData(0, 12, 130, 250, RoundIconKind.Letters)]
    public void A_One_Pixel_Icon_Is_A_Plate_Or_Letters(int alpha, int r, int g, int b, RoundIconKind kind)
    {
        var plan = Plan(Solid(1, 1, (byte)r, (byte)g, (byte)b, (byte)alpha));

        Assert.Equal(kind, plan.Kind);
        if (kind == RoundIconKind.Plate) Assert.Equal(new RoundIconColour((byte)r, (byte)g, (byte)b), plan.Disc);
    }

    [Theory]
    [InlineData(127, RoundIconKind.Letters)]
    [InlineData(128, RoundIconKind.Plate)]
    [InlineData(255, RoundIconKind.Plate)]
    public void Every_Pixel_Half_Transparent_Is_Split_By_The_128_Line(int alpha, RoundIconKind kind) =>
        Assert.Equal(kind, Plan(Solid(8, 8, 20, 90, 200, (byte)alpha)).Kind);

    [Fact]
    public void A_One_Colour_Icon_Is_A_Plate_When_It_Fills_Its_Square_And_A_Flat_Shape_When_It_Does_Not()
    {
        Assert.Equal(RoundIconKind.Plate, Plan(Solid(8, 8, 90, 20, 160)).Kind);

        // An L of one colour: 5 + 4 = 9 of the 25 pixels of its square (36 percent).
        var l = new byte[5 * 5 * 4];
        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
            if (x == 0 || y == 4) (l[(y * 5 + x) * 4], l[(y * 5 + x) * 4 + 1], l[(y * 5 + x) * 4 + 2], l[(y * 5 + x) * 4 + 3]) = (160, 20, 90, 255);
        var plan = RoundIconRule.Analyse(5, 5, l);
        Assert.Equal(new RoundIconPlan(RoundIconKind.FlatShape, new RoundIconColour(90, 20, 160), true), plan);
    }

    [Fact]
    public void The_Plate_Line_Is_70_Percent_Of_The_Square_And_The_Flat_Line_Is_90_Percent_Of_The_Pixels()
    {
        // 70 opaque pixels in a 10 x 10 bounding square: a plate. 69: not a plate, and being one colour a flat shape.
        Assert.Equal(RoundIconKind.Plate, Plan(Counted(70)).Kind);
        Assert.Equal(RoundIconKind.FlatShape, Plan(Counted(69)).Kind);

        // 100 pixels in a thin diagonal-free strip is not a plate: 90 of one colour and 10 of another is a flat shape, 89 and 11 is not.
        Assert.Equal(RoundIconKind.FlatShape, Plan(Strip(90, 10)).Kind);
        Assert.Equal(RoundIconKind.Other, Plan(Strip(89, 11)).Kind);
    }

    // 10 x 10 of one colour with exactly `opaque` opaque pixels, always including both far corners so the bounding square is the whole 10 x 10.
    private static IconImage Counted(int opaque)
    {
        var icon = Solid(10, 10, 10, 150, 60, 0);
        var set = new List<int> { 0, 99 };
        for (var i = 1; set.Count < opaque; i++) set.Add(i);
        foreach (var pixel in set) icon.Bgra[pixel * 4 + 3] = 255;
        return icon;
    }

    /// <summary>A strip 100 wide and 1 high: `first` pixels of one colour then `second` of another (each group is far apart in colour).</summary>
    private static IconImage Strip(int first, int second)
    {
        var bgra = new byte[(first + second) * 4];
        for (var i = 0; i < first + second; i++)
        {
            if (i < first) (bgra[i * 4], bgra[i * 4 + 1], bgra[i * 4 + 2]) = (10, 20, 200);
            else (bgra[i * 4], bgra[i * 4 + 1], bgra[i * 4 + 2]) = (200, 20, 10);
            bgra[i * 4 + 3] = 255;
        }

        return new IconImage(first + second, 1, bgra);
    }

    [Fact]
    public void A_Nearly_White_Line_Needs_Every_Channel_At_230_Or_More()
    {
        Assert.Equal(RoundIconKind.NearlyWhiteFlatShape, Plan(Flat(230, 230, 230)).Kind);
        Assert.Equal(RoundIconKind.FlatShape, Plan(Flat(229, 230, 230)).Kind);
        Assert.Equal(RoundIconKind.FlatShape, Plan(Flat(255, 255, 229)).Kind);
    }

    // A one-colour ellipse (a flat shape) in the colour asked for.
    private static IconImage Flat(byte r, byte g, byte b)
    {
        var icon = RoundIconSamples.OrangeEllipse;
        var bgra = (byte[])icon.Bgra.Clone();
        for (var i = 0; i < bgra.Length; i += 4)
            if (bgra[i + 3] != 0) (bgra[i], bgra[i + 1], bgra[i + 2]) = (b, g, r);
        return new IconImage(64, 64, bgra);
    }

    [Fact]
    public void A_Plate_That_Is_Nearly_White_Keeps_Its_Own_Colour_For_The_Disc()
    {
        // The rule says a plate is drawn as it is on a disc of its main colour; the nearly-white exception is for flat shapes only.
        var plan = Plan(Solid(8, 8, 250, 250, 250));
        Assert.Equal(new RoundIconPlan(RoundIconKind.Plate, new RoundIconColour(250, 250, 250), false), plan);
    }

    [Fact]
    public void The_Winning_Group_Gives_The_Mean_Of_Its_Pixels()
    {
        // Two shades in the same 4-bit group (100 and 104 both group 6): the mean is 102. A third colour far away has one pixel (90 percent is in the winning group).
        var bgra = new byte[10 * 1 * 4];
        for (var i = 0; i < 10; i++)
        {
            var shade = (byte)(i < 4 ? 100 : i < 9 ? 104 : 200);
            (bgra[i * 4], bgra[i * 4 + 1], bgra[i * 4 + 2], bgra[i * 4 + 3]) = (shade, shade, shade, 255);
        }

        Assert.Equal(new RoundIconColour(102, 102, 102), RoundIconRule.Analyse(10, 1, bgra).Disc);
    }

    [Fact]
    public void A_Dark_Many_Coloured_Logo_Gets_A_Lighter_Disc_With_The_Same_Hue()
    {
        var dark = Ring((30, 30, 70), (30, 30, 70), (35, 40, 60), (40, 30, 60));
        var start = new RoundIconColour(30, 30, 70);
        Assert.False(RoundIconRule.StaysVisibleOn(start, 64, 64, dark.Bgra));

        var result = RoundIconRule.MakeVisible(start, 64, 64, dark.Bgra);

        Assert.True(result.R > start.R && result.G > start.G && result.B > start.B, "lighter, not darker");
        Assert.Equal(result.R, result.G); // the equal channels stay equal and blue stays the largest: the hue is kept
        Assert.True(result.B > result.R);
        Assert.True(RoundIconRule.StaysVisibleOn(result, 64, 64, dark.Bgra));
        Assert.Equal(result, Plan(dark).Disc);
    }

    [Fact]
    public void A_Disc_That_Already_Works_Is_Not_Moved()
    {
        var ring = RoundIconSamples.FourColourRing;
        var far = new RoundIconColour(20, 20, 20);
        Assert.Equal(far, RoundIconRule.MakeVisible(far, ring.Width, ring.Height, ring.Bgra));
    }

    [Fact]
    public void The_Visibility_Loop_Ends_For_Every_Disc_Colour_And_Stays_Inside_The_Steps()
    {
        var icons = new[] { RoundIconSamples.FourColourRing, Ring((255, 255, 255), (0, 0, 0), (128, 128, 128), (255, 0, 0)) };
        var levels = Enumerable.Range(0, 16).Select(i => (byte)(i * 17)).ToArray(); // 0, 17, ... 255: 4096 colours
        foreach (var icon in icons)
        foreach (var r in levels)
        foreach (var g in levels)
        foreach (var b in levels)
        {
            var start = new RoundIconColour(r, g, b);
            var result = RoundIconRule.MakeVisible(start, icon.Width, icon.Height, icon.Bgra);

            // Never past the steps: all channels moved the same way (down, or up for a dark start), by at most MaxDiscSteps * step percent.
            var lighter = Math.Max(r, Math.Max(g, b)) < RoundIconConstants.DarkDiscMaxChannel;
            var reach = RoundIconConstants.MaxDiscSteps * RoundIconConstants.DiscStepPercent;
            foreach (var (from, to) in new[] { (r, result.R), (g, result.G), (b, result.B) })
            {
                if (lighter) Assert.InRange(to, from, from + (255 - from) * reach / 100 + 1);
                else Assert.InRange(to, from * (100 - reach) / 100 - 1, from);
            }
        }
    }

    [Fact]
    public void A_Logo_That_Cannot_Be_Made_Visible_Still_Ends_With_The_Best_Colour_Of_The_Steps()
    {
        // Every grey level equally often: whatever the disc, about a fifth to a half of the pixels are within 59 of it, so 90 percent never holds.
        var bgra = new byte[64 * 64 * 4];
        for (var p = 0; p < 4096; p++)
            (bgra[p * 4], bgra[p * 4 + 1], bgra[p * 4 + 2], bgra[p * 4 + 3]) = ((byte)(p % 256), (byte)(p % 256), (byte)(p % 256), 255);
        var start = new RoundIconColour(128, 128, 128);

        var result = RoundIconRule.MakeVisible(start, 64, 64, bgra);

        Assert.False(RoundIconRule.StaysVisibleOn(result, 64, 64, bgra));
        Assert.Equal(new RoundIconColour(5, 5, 5), result); // 12 steps of 8 percent down: the one with most pixels told apart
        Assert.Equal(result, RoundIconRule.MakeVisible(start, 64, 64, bgra));
    }

    [Fact]
    public void Odd_Sizes_And_Buffers_Are_Letters_And_Never_Throw()
    {
        var good = Solid(4, 4, 1, 2, 3).Bgra;
        Assert.Equal(RoundIconKind.Plate, RoundIconRule.Analyse(4, 4, good).Kind);

        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(4, 4, good.AsSpan(0, 63)).Kind); // one byte short
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(4, 4, new byte[65]).Kind); // one byte long
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(4, 4, ReadOnlySpan<byte>.Empty).Kind);
        Assert.Equal(RoundIconKind.FlatShape, RoundIconRule.Analyse(2, 8, good).Kind); // the right byte count for another shape is a readable 2 x 8 (a bar is no plate)
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(3, 8, good).Kind);
        foreach (var (w, h) in new[] { (0, 4), (4, 0), (-4, -4), (-1, 16), (int.MinValue, int.MinValue) })
            Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(w, h, good).Kind);
    }

    [Fact]
    public void Huge_Dimensions_Are_Refused_Without_Reading_Or_Overflowing()
    {
        var tiny = new byte[16];
        foreach (var (w, h) in new[] { (10000, 10000), (int.MaxValue, int.MaxValue), (int.MaxValue, 1), (65536, 65536), (1 << 20, 1 << 20) })
            Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(w, h, tiny).Kind);

        // Over the cap with a buffer of the right length: refused too (2049 x 2048 is over 2048 x 2048).
        var big = new byte[2049 * 2048 * 4];
        big[3] = 255;
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(2049, 2048, big).Kind);

        // The size of a large Windows icon is read: 512 x 512 in one colour is a plate.
        var large = Solid(512, 512, 5, 6, 7);
        Assert.Equal(new RoundIconPlan(RoundIconKind.Plate, new RoundIconColour(5, 6, 7), false), Plan(large));
    }

    [Fact]
    public void Colour_Conversion_Keeps_The_Whole_Number_Values()
    {
        Assert.Equal(new Rgb(40, 42, 50), RoundIconConstants.DarkNeutral.ToRgb());
    }
}
