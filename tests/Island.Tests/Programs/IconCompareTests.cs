using Island.Core;

namespace Island.Tests.Programs;

public class IconCompareTests
{
    private static IconImage Flat(int size, byte value) =>
        new(size, size, [.. Enumerable.Repeat(value, size * size * 4)]);

    [Fact]
    public void Identical_Pictures_Are_Alike_Even_At_Another_Size()
    {
        Assert.Equal(0, IconCompare.MeanDifference(Flat(32, 200), Flat(32, 200)));
        Assert.True(IconCompare.AreAlike(Flat(64, 200), Flat(256, 200)));
    }

    [Fact]
    public void Very_Different_Pictures_Are_Not_Alike()
    {
        Assert.False(IconCompare.AreAlike(Flat(32, 10), Flat(32, 240)));
    }

    [Fact]
    public void An_Unusable_Picture_Is_Never_Alike()
    {
        Assert.False(IconCompare.AreAlike(new IconImage(0, 0, []), Flat(32, 0xFF)));
    }
}
