using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-10 §1, "Sharpness": the 66% box, own size when it fits, scaled down keeping the aspect when it does not.</summary>
public class RoundIconLayoutTests
{
    private const double Tile = LookConstants.ItemSize;
    private const double Box = Tile * 0.66; // 26.4

    [Theory]
    [InlineData(16, 16, 1.0, 12.0, 12.0, 16.0, 16.0)]
    [InlineData(32, 32, 2.0, 12.0, 12.0, 16.0, 16.0)] // 32 pixels at 200 percent are 16 DIP
    [InlineData(64, 64, 1.0, 6.8, 6.8, 26.4, 26.4)] // larger than the box: fitted to it
    [InlineData(256, 128, 1.0, 6.8, 13.4, 26.4, 13.2)] // aspect kept
    [InlineData(100, 20, 2.0, 6.8, 17.36, 26.4, 5.28)]
    public void Picture_Is_Centred_At_Its_Own_Size_Or_Fitted_To_The_Box(int w, int h, double scale, double x, double y, double width, double height)
    {
        var box = RoundIconLayout.Place(w, h, Tile, scale);

        Assert.Equal(x, box.X, 9);
        Assert.Equal(y, box.Y, 9);
        Assert.Equal(width, box.Width, 9);
        Assert.Equal(height, box.Height, 9);
    }

    [Fact]
    public void A_Picture_Exactly_The_Size_Of_The_Box_Is_Not_Scaled()
    {
        var box = RoundIconLayout.Place(264, 264, Tile, 10.0); // 26.4 DIP at 1000 percent
        Assert.Equal(Box, box.Width, 9);
        Assert.Equal((Tile - Box) / 2, box.X, 9);
    }

    [Fact]
    public void A_Picture_That_Fits_Is_Never_Larger_Than_Its_Own_Size()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0 })
        foreach (var size in new[] { 1, 8, 16, 24, 32, 48, 64, 96, 128, 256, 512 })
        {
            var box = RoundIconLayout.Place(size, size, Tile, scale);
            Assert.True(box.Width <= size / scale + 1e-9, $"{size} at {scale}");
            Assert.True(box.Width <= Box + 1e-9);
            Assert.Equal(box.Width, box.Height, 9);
            Assert.Equal((Tile - box.Width) / 2, box.X, 9);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_Nonsense_Scale_Counts_As_One(double scale) =>
        Assert.Equal(RoundIconLayout.Place(32, 32, Tile, 1.0), RoundIconLayout.Place(32, 32, Tile, scale));

    [Fact]
    public void Odd_Scales_Sizes_And_Tiles_Never_Give_NaN_Infinity_Or_A_Box_Outside_The_Tile()
    {
        var scales = new[] { 0.0, -3, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300, 1e-300, 1e-310, double.MaxValue, double.Epsilon, 1.5 };
        var sizes = new[] { int.MinValue, -1, 0, 1, 16, 40, 4096, int.MaxValue };
        foreach (var scale in scales)
        foreach (var w in sizes)
        foreach (var h in sizes)
        foreach (var tile in new[] { Tile, 0, -5, double.NaN, double.PositiveInfinity })
        {
            var b = RoundIconLayout.Place(w, h, tile, scale);
            var label = $"{w}x{h} scale {scale} tile {tile}";
            Assert.True(double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Width) && double.IsFinite(b.Height), label);
            var side = tile > 0 && double.IsFinite(tile) ? tile : 0;
            Assert.InRange(b.Width, 0, side * 0.66 + 1e-9);
            Assert.InRange(b.Height, 0, side * 0.66 + 1e-9);
            Assert.InRange(b.X, 0, side);
            Assert.InRange(b.Y, 0, side);
        }
    }
}
