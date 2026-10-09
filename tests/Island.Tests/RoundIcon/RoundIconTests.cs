using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-10 §1: the eight tests the work order names, on the six icons drawn in code.</summary>
public class RoundIconTests
{
    private static RoundIconPlan Plan(IconImage icon) => RoundIconRule.Analyse(icon.Width, icon.Height, icon.Bgra);

    [Fact]
    public void A_Plate_Icon_Gets_A_Disc_In_Its_Plates_Colour()
    {
        var plan = Plan(RoundIconSamples.PlateWithWhiteSquare);

        Assert.Equal(RoundIconKind.Plate, plan.Kind);
        Assert.Equal(new RoundIconColour(47, 99, 224), plan.Disc); // the plate's blue, not the white square's
        Assert.False(plan.DrawnWhite); // drawn as it is
    }

    [Fact]
    public void A_Flat_Shape_Is_Drawn_White_On_Its_Own_Colour()
    {
        var plan = Plan(RoundIconSamples.OrangeEllipse);

        Assert.Equal(RoundIconKind.FlatShape, plan.Kind);
        Assert.Equal(new RoundIconColour(245, 131, 18), plan.Disc);
        Assert.True(plan.DrawnWhite);
    }

    [Fact]
    public void A_Nearly_White_Flat_Shape_Gets_A_Dark_Disc()
    {
        var plan = Plan(RoundIconSamples.NearlyWhiteEllipse);

        Assert.Equal(RoundIconKind.NearlyWhiteFlatShape, plan.Kind);
        Assert.Equal(new RoundIconColour(40, 42, 50), plan.Disc);
        Assert.False(plan.DrawnWhite); // the shape stays as it is
    }

    [Fact]
    public void A_Many_Coloured_Logo_Stays_Visible_On_A_Darker_Disc()
    {
        var ring = RoundIconSamples.FourColourRing;
        var plan = Plan(ring);

        Assert.Equal(RoundIconKind.Other, plan.Kind);
        Assert.False(plan.DrawnWhite);
        var red = new RoundIconColour(220, 60, 60);
        Assert.True(plan.Disc.R < red.R && plan.Disc.G < red.G && plan.Disc.B < red.B, "darker than the first quarter's red");
        Assert.True(plan.Disc.R > plan.Disc.G && plan.Disc.G == plan.Disc.B, "still a red: the hue is kept");
        Assert.True(RoundIconRule.StaysVisibleOn(plan.Disc, ring.Width, ring.Height, ring.Bgra));
        Assert.False(RoundIconRule.StaysVisibleOn(red, ring.Width, ring.Height, ring.Bgra), "on the plain red the red quarter would vanish");

        // All four quarters stay visible: each of the four colours differs from the disc by 60 or more in some channel.
        foreach (var quarter in new[] { (220, 60, 60), (240, 190, 40), (60, 170, 90), (70, 130, 240) })
        {
            var difference = new[] { quarter.Item1 - plan.Disc.R, quarter.Item2 - plan.Disc.G, quarter.Item3 - plan.Disc.B }.Max(d => Math.Abs(d));
            Assert.True(difference >= RoundIconConstants.VisibleDifference, $"{quarter}: {difference}");
        }
    }

    [Fact]
    public void A_Small_Icon_Is_Never_Stretched()
    {
        var small = RoundIconSamples.SmallPlate; // 16 x 16
        var tile = LookConstants.ItemSize;

        var own = RoundIconLayout.Place(small.Width, small.Height, tile, 1.0);
        Assert.Equal(new RoundIconBox(12, 12, 16, 16), own);

        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
        {
            var box = RoundIconLayout.Place(small.Width, small.Height, tile, scale);
            Assert.Equal(small.Width / scale, box.Width, 9);
            Assert.Equal(small.Height / scale, box.Height, 9);
            Assert.Equal((tile - box.Width) / 2, box.X, 9);
            Assert.Equal((tile - box.Height) / 2, box.Y, 9);
        }

        // A large picture is scaled down to the 66% box, keeping its aspect, centred.
        var large = RoundIconLayout.Place(64, 32, tile, 1.0);
        Assert.Equal(26.4, large.Width, 9);
        Assert.Equal(13.2, large.Height, 9);
        Assert.Equal((tile - 26.4) / 2, large.X, 9);
        Assert.Equal((tile - 13.2) / 2, large.Y, 9);
    }

    [Fact]
    public void No_Opaque_Pixels_Means_Letters()
    {
        var nothing = RoundIconSamples.Nothing;
        Assert.Equal(RoundIconKind.Letters, Plan(nothing).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(null).Kind);
        Assert.Equal(RoundIconKind.Letters, RoundIconRule.Analyse(0, 0, ReadOnlySpan<byte>.Empty).Kind);

        // Colour without alpha is still nothing: alpha 127 is below the line.
        var faint = new IconImage(4, 4, Enumerable.Range(0, 64).Select(i => i % 4 == 3 ? (byte)127 : (byte)200).ToArray());
        Assert.Equal(RoundIconKind.Letters, Plan(faint).Kind);
    }

    [Fact]
    public void Transparency_Is_Kept_When_A_Shape_Turns_White()
    {
        // The orange ellipse with a mix of alphas: every 3rd pixel half solid, every 5th faint, the rest as they were.
        var source = (byte[])RoundIconSamples.OrangeEllipse.Bgra.Clone();
        for (var pixel = 0; pixel < 64 * 64; pixel++)
        {
            if (source[pixel * 4 + 3] == 0) continue;
            if (pixel % 3 == 0) source[pixel * 4 + 3] = 140;
            if (pixel % 5 == 0) source[pixel * 4 + 3] = 40;
        }

        var plan = RoundIconRule.Analyse(64, 64, source);
        Assert.Equal(RoundIconKind.FlatShape, plan.Kind);
        Assert.True(plan.DrawnWhite);

        var white = RoundIconRule.Whiten(source);

        Assert.Equal(source.Length, white.Length);
        for (var i = 0; i < source.Length; i += 4)
        {
            Assert.Equal((byte)255, white[i]);
            Assert.Equal((byte)255, white[i + 1]);
            Assert.Equal((byte)255, white[i + 2]);
            Assert.Equal(source[i + 3], white[i + 3]); // each pixel's own alpha
        }

        Assert.NotEqual(source, white); // a copy: the original is not touched
        Assert.Equal((byte)18, source[(32 * 64 + 32) * 4]); // the blue channel of the orange is still there
    }

    [Fact]
    public void The_Same_Icon_Always_Gets_The_Same_Colour()
    {
        foreach (var icon in RoundIconSamples.All)
        {
            var first = Plan(icon);
            for (var again = 0; again < 3; again++)
                Assert.Equal(first, RoundIconRule.Analyse(icon.Width, icon.Height, (byte[])icon.Bgra.Clone()));
        }

        // A tie between two groups goes to the one met first, reading row by row: the same two halves, mirrored, give the other colour.
        var leftRed = Halves((200, 20, 20), (20, 20, 200));
        var leftBlue = Halves((20, 20, 200), (200, 20, 20));
        Assert.Equal(new RoundIconColour(200, 20, 20), Plan(leftRed).Disc);
        Assert.Equal(new RoundIconColour(20, 20, 200), Plan(leftBlue).Disc);

        // The ring's four quarters tie exactly; the first opaque pixel (top row, leftmost) is in the red quarter.
        Assert.Equal(RoundIconKind.Other, Plan(RoundIconSamples.FourColourRing).Kind);
        Assert.Equal(Plan(RoundIconSamples.FourColourRing), Plan(RoundIconSamples.FourColourRing));
    }

    /// <summary>8 x 8, the left half one colour and the right half another (each 32 pixels): a perfect tie.</summary>
    private static IconImage Halves((byte R, byte G, byte B) left, (byte R, byte G, byte B) right)
    {
        var bgra = new byte[8 * 8 * 4];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            var c = x < 4 ? left : right;
            var i = (y * 8 + x) * 4;
            (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (c.B, c.G, c.R, 255);
        }

        return new IconImage(8, 8, bgra);
    }
}
