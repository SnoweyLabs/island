using Island.Core;

namespace Island.Tests;

public class ColorMathTests
{
    [Fact]
    public void OkLab_Of_Red_Matches_Published_Values()
    {
        // Published reference for sRGB red in OKLab: L 0.62796, a 0.22486, b 0.12585.
        var (l, a, b) = ColorMath.ToOkLab(new Rgb(255, 0, 0));
        Assert.InRange(l, 0.6279, 0.6281);
        Assert.InRange(a, 0.2248, 0.2250);
        Assert.InRange(b, 0.1258, 0.1259);
    }

    [Fact]
    public void OkLab_Round_Trip_Is_Identity()
    {
        foreach (var hex in new[] { "#FF4055", "#FFB81C", "#1F6FFF", "#19E6B3", "#E9A0FF", "#000000", "#FFFFFF" })
        {
            var c = Rgb.FromHex(hex);
            var (l, a, b) = ColorMath.ToOkLab(c);
            Assert.Equal(hex, ColorMath.FromOkLab(l, a, b).ToHex());
        }
    }

    [Fact]
    public void Mix_Endpoints_Return_The_Inputs()
    {
        var red = Rgb.FromHex("#FF4055");
        Assert.Equal("#FF4055", ColorMath.MixOkLab(red, Rgb.White, 0).ToHex());
        Assert.Equal("#FFFFFF", ColorMath.MixOkLab(red, Rgb.White, 1).ToHex());
    }

    [Fact]
    public void Arc_Color_Is_Lighter_Than_Category_Color()
    {
        foreach (var cat in Pages.BuiltIn)
        {
            var c = Rgb.FromHex(cat.Color);
            var arc = ColorMath.ArcColor(c);
            Assert.True(ColorMath.ToOkLab(arc).L > ColorMath.ToOkLab(c).L, cat.Name);
        }
    }

    [Fact]
    public void Hsl_Primary_Colors()
    {
        Assert.Equal("#FF0000", ColorMath.FromHsl(0, 100, 50).ToHex());
        Assert.Equal("#00FF00", ColorMath.FromHsl(120, 100, 50).ToHex());
        Assert.Equal("#0000FF", ColorMath.FromHsl(240, 100, 50).ToHex());
    }
}
