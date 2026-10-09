using Island.Core;

namespace Island.Tests;

public class WindowDotsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-3)]
    public void None_For_Zero_Or_One(int windows) => Assert.Equal(0, WindowDots.For(windows));

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    public void Two_To_Five_Show_That_Many(int windows, int dots) => Assert.Equal(dots, WindowDots.For(windows));

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Six_Or_More_Show_Five(int windows) => Assert.Equal(5, WindowDots.For(windows));

    [Fact]
    public void The_Numbers_Of_The_Dots_Are_Pinned()
    {
        Assert.Equal(4, ChoiceConstants.DotSize);
        Assert.Equal(3, ChoiceConstants.DotGap);
        Assert.Equal(7, ChoiceConstants.DotCentreBelowTile);
        Assert.Equal(4, ChoiceConstants.DotGlow);
        Assert.Equal(5, ChoiceConstants.MaxDots);
        // The dots lie inside the capsule: the tile's lower edge is at 59 of 76 as laid out (the work order says 58: it leaves out the 1 px border).
        var tileBottom = LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth + LookConstants.ItemSize / 2;
        Assert.Equal(59, tileBottom);
        Assert.True(tileBottom + ChoiceConstants.DotCentreBelowTile + ChoiceConstants.DotSize / 2 < LookConstants.CapsuleHeight);
    }
}
