using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-13, Dan's P4: the two letters of a tile reach 4.5:1 on every hue a tile can have, at the opacity of an unselected tile.</summary>
public class TileLabelTests
{
    [Fact]
    public void The_Letters_Reach_4_5_To_1_On_Every_Hue_At_The_Unselected_Opacity()
    {
        for (var hue = 0; hue < 360; hue++)
        {
            var plan = TileLabel.ForHue(hue);
            var top = ColorMath.FromHsl(hue, LookConstants.ItemTopSaturation, LookConstants.ItemTopLightness);
            var bottom = ColorMath.FromHsl(hue, LookConstants.ItemBottomSaturation, LookConstants.ItemBottomLightness);
            var ratio = TileLabel.RatioOf(plan, ColorMath.LerpSrgb(top, bottom, 0.5), LookConstants.ItemFillAlpha);
            Assert.True(ratio >= 4.5, $"hue {hue}: {ratio:0.00}:1 with {plan}");
            Assert.InRange(plan.ScrimAlpha, 0, TileLabel.MaxScrim);
        }
    }

    [Fact]
    public void White_Is_Kept_On_The_Tiles_Where_It_Already_Reads_And_Black_Is_Used_On_The_Light_Ones()
    {
        var blue = TileLabel.ForHue(225); // a deep blue tile: white reads
        Assert.Equal(Rgb.White, blue.Text);
        Assert.InRange(blue.ScrimAlpha, 0, 0.3); // a light glass needs a little help even there
        var gold = TileLabel.ForHue(45); // gold: white does not
        Assert.Equal(new Rgb(0, 0, 0), gold.Text);
    }

    [Fact]
    public void A_Helpers_Own_Disc_At_Full_Opacity_Gets_Letters_That_Read_Too()
    {
        foreach (var disc in new[] { new Rgb(0xE0, 0x7A, 0x3D), new Rgb(0x2E, 0xB8, 0x5C), new Rgb(0x3A, 0x7B, 0xFF), new Rgb(0xA8, 0x5C, 0xF0) })
        {
            var plan = TileLabel.For(disc, 1);
            Assert.True(TileLabel.RatioOf(plan, disc, 1) >= 4.5, $"{disc.ToHex()}: {plan}");
        }
    }

    [Fact]
    public void Contrast_Is_The_WCAG_Ratio()
    {
        Assert.Equal(21, TileLabel.Contrast(Rgb.White, new Rgb(0, 0, 0)), 3);
        Assert.Equal(1, TileLabel.Contrast(new Rgb(120, 120, 120), new Rgb(120, 120, 120)), 6);
    }
}
