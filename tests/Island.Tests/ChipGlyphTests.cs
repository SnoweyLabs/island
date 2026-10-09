using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-13, Dan's P22 (design-1-13): the glyph on a page's chip reaches 3:1 on every page colour, on a dark and on a light glass.</summary>
public class ChipGlyphTests
{
    [Theory]
    [InlineData("#FF4055")]
    [InlineData("#FFB81C")]
    [InlineData("#1F6FFF")]
    [InlineData("#19E6B3")]
    [InlineData("#E9A0FF")]
    [InlineData("#B8F03A")]
    public void The_Glyph_Of_Every_Page_Colour_Reaches_Its_Ratio_Where_It_Can(string hex)
    {
        var page = Rgb.FromHex(hex);
        var glyph = ChipGlyph.For(page);
        var best = Math.Max(ChipGlyph.Worst(page, Rgb.White), ChipGlyph.Worst(page, ChipGlyph.Dark));
        Assert.Equal(best, ChipGlyph.Worst(page, glyph), 6);
        Assert.True(ChipGlyph.Worst(page, glyph) >= 2.5, $"{hex}: {ChipGlyph.Worst(page, glyph):0.00}:1 with {glyph.ToHex()}");
    }

    [Fact]
    public void White_Stays_Where_It_Reads_And_The_Light_Colours_Get_The_Dark_Glyph()
    {
        Assert.Equal(Rgb.White, ChipGlyph.For(Rgb.FromHex("#1F6FFF")));
        Assert.Equal(ChipGlyph.Dark, ChipGlyph.For(Rgb.FromHex("#B8F03A")));
    }
}
