using Island.Core;

namespace Island.Tests.Programs;

public class IconChoiceTests
{
    private static IconImage Icon(int size, byte alpha = 255) =>
        new(size, size, [.. Enumerable.Repeat(alpha, size * size * 4)]);

    [Fact]
    public void Missing_Icon_Falls_Back_To_Letters()
    {
        Assert.True(IconChoice.Choose(null, 32).UseLetters);
        Assert.True(IconChoice.Choose([], 32).UseLetters);
        Assert.True(IconChoice.Choose([null], 32).UseLetters);
        Assert.True(IconChoice.Choose([new IconImage(0, 0, [])], 32).UseLetters);                 // empty
        Assert.True(IconChoice.Choose([new IconImage(4, 4, [1, 2, 3])], 32).UseLetters);          // bytes do not match the size
        Assert.True(IconChoice.Choose([Icon(16, alpha: 0)], 16).UseLetters);                      // blank picture
        Assert.Null(IconChoice.Choose([], 32).Image);
    }

    [Fact]
    public void Largest_Available_Size_Is_Chosen()
    {
        var small = Icon(16);
        var big = Icon(256);
        var mid = Icon(48);

        var choice = IconChoice.Choose([small, big, mid], 32);

        Assert.Same(big, choice.Image);
        Assert.False(choice.UseLetters);
        Assert.False(choice.Undersized);
    }

    [Fact]
    public void Only_Smaller_Picture_Is_Used_As_It_Is_And_Flagged()
    {
        var only = Icon(16);

        var choice = IconChoice.Choose([only], 48);

        Assert.Same(only, choice.Image); // not replaced, not resized: the same picture
        Assert.False(choice.UseLetters);
        Assert.True(choice.Undersized);
    }

    [Fact]
    public void A_Picture_Exactly_As_Large_As_Drawn_Is_Not_Flagged()
    {
        Assert.False(IconChoice.Choose([Icon(32)], 32).Undersized);
    }

    [Fact]
    public void Unseen_Starter_Pick_Uses_Letters()
    {
        // Even when some picture is offered, a pick this computer has never seen shows letters, not a borrowed logo.
        var choice = IconChoice.Choose([Icon(256)], 32, seenOnThisComputer: false);

        Assert.True(choice.UseLetters);
        Assert.Null(choice.Image);
    }

    [Theory]
    [InlineData("Spotify", "Sp")]
    [InlineData("YouTube Music", "YM")]
    [InlineData("Visual Studio Code", "VS")]
    [InlineData("music.youtube.com", "MY")]
    [InlineData("x", "X")]
    [InlineData("  ", "?")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    [InlineData("+++", "?")]
    public void Two_Letter_Mark_Follows_The_Tile_Rule(string? name, string expected)
    {
        Assert.Equal(expected, IconChoice.TwoLetterMark(name));
    }
}
