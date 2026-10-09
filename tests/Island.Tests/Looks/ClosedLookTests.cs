using Island.Core;

namespace Island.Tests;

public class ClosedLookTests
{
    private static IconImage Sample()
    {
        // 3 x 1 pixels, BGRA straight: opaque red, half-transparent green, fully transparent blue.
        byte[] bgra = [0, 0, 255, 255, 0, 255, 0, 128, 255, 0, 0, 0];
        return new IconImage(3, 1, bgra);
    }

    [Fact]
    public void Grey_Copy_Has_No_Colour_And_Keeps_Transparency()
    {
        var icon = Sample();
        var grey = GreyIcons.Make(icon);

        Assert.Equal((icon.Width, icon.Height), (grey.Width, grey.Height));
        for (var i = 0; i < grey.Bgra.Length; i += 4)
        {
            Assert.Equal(grey.Bgra[i], grey.Bgra[i + 1]); // blue = green
            Assert.Equal(grey.Bgra[i + 1], grey.Bgra[i + 2]); // green = red
            Assert.Equal(icon.Bgra[i + 3], grey.Bgra[i + 3]); // alpha untouched
        }

        // A little darker than the plain luma of pure white: 255 x 0.95.
        var white = GreyIcons.Make(new IconImage(1, 1, [255, 255, 255, 255]));
        Assert.Equal((byte)Math.Round(255 * ChoiceConstants.ClosedBrightness), white.Bgra[0]);
    }

    [Fact]
    public void Open_Pick_Is_Untouched()
    {
        var icon = Sample();
        var before = (byte[])icon.Bgra.Clone();

        var grey = GreyIcons.Of(icon);
        var again = GreyIcons.Of(icon);

        Assert.Equal(before, icon.Bgra); // making the grey copy never changes the open icon
        Assert.NotSame(icon, grey);
        Assert.NotSame(icon.Bgra, grey.Bgra);
        Assert.Same(grey, again); // made once
    }

    [Fact]
    public void The_Numbers_Of_The_Closed_Look_Are_Pinned()
    {
        Assert.Equal(0.95, ChoiceConstants.ClosedBrightness);
        Assert.Equal(0.85, ChoiceConstants.ClosedOpacity);
        Assert.Equal(200, ChoiceConstants.ClosedFadeMs);
    }
}
