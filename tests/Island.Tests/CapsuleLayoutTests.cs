using Island.Core;

namespace Island.Tests;

public class CapsuleLayoutTests
{
    [Fact]
    public void Three_Media_Items_Are_466_Wide()
    {
        Assert.Equal(466, CapsuleLayout.SizeFor(Pages.Placeholder(PageIds.Media)).Width);
        Assert.Equal(76, CapsuleLayout.SizeFor(Pages.Placeholder(PageIds.Media)).Height);
    }

    [Fact]
    public void Five_Browser_Items_Are_502_Wide()
    {
        Assert.Equal(502, CapsuleLayout.SizeFor(Pages.Placeholder(PageIds.Browser)).Width);
    }

    [Fact]
    public void Placeholder_Data_Matches_The_Reference_Counts()
    {
        Assert.Equal([3, 4, 5, 4, 5, 3], Pages.AllPlaceholders.Select(c => c.Items.Count).ToArray());
        Assert.Equal(6, Pages.BuiltIn.Count);
        Assert.Equal(["media", "folders", "apps", "vibe", "browser", "terminals"], Pages.BuiltIn.Select(p => p.Id).ToArray());
    }
}
