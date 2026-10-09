using Island.Core;

namespace Island.Tests;

public class RoundIconsCacheTests
{
    [Fact]
    public void The_Look_Of_An_Icon_Is_Made_Once()
    {
        var icon = RoundIconSamples.OrangeEllipse;
        Assert.Same(RoundIcons.Of(icon), RoundIcons.Of(icon));
    }

    [Fact]
    public void A_Flat_Shape_Is_Drawn_From_A_White_Copy_And_A_Plate_From_Itself()
    {
        var flat = RoundIcons.Of(RoundIconSamples.OrangeEllipse);
        Assert.NotSame(RoundIconSamples.OrangeEllipse, flat.Drawn);
        Assert.Equal(RoundIconSamples.OrangeEllipse.Bgra.Length, flat.Drawn.Bgra.Length);

        var plate = RoundIcons.Of(RoundIconSamples.PlateWithWhiteSquare);
        Assert.Same(RoundIconSamples.PlateWithWhiteSquare, plate.Drawn);
    }

    [Fact]
    public void A_Closed_Disc_Has_No_Colour()
    {
        var grey = RoundIcons.Grey(new RoundIconColour(230, 40, 60));
        Assert.Equal(grey.R, grey.G);
        Assert.Equal(grey.G, grey.B);
        Assert.True(grey.R < 160);
    }

    [Fact]
    public void An_Icon_With_Nothing_To_Draw_Is_Letters_And_Its_Picture_Is_Left_Alone()
    {
        var look = RoundIcons.Of(RoundIconSamples.Nothing);
        Assert.Equal(RoundIconKind.Letters, look.Plan.Kind);
        Assert.Same(RoundIconSamples.Nothing, look.Drawn);
    }
}
