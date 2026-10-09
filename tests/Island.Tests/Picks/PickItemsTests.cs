using Island.Core;

namespace Island.Tests;

public class PickItemsTests
{
    [Theory]
    [InlineData("Spotify", "Sp")]
    [InlineData("YouTube Music", "YM")]
    [InlineData("Premiere Pro", "PP")]
    [InlineData("Notepad", "No")]
    [InlineData("X", "X")]
    public void The_Tile_Mark_Is_Two_Letters(string name, string mark) => Assert.Equal(mark, PickItems.Mark(name));

    [Fact]
    public void The_Hue_Is_Stable_And_In_Range()
    {
        Assert.Equal(PickItems.Hue("program:spotify"), PickItems.Hue("program:spotify"));
        Assert.All(new[] { "a", "program:x", "site:youtube.com", "folder:downloads" }, id => Assert.InRange(PickItems.Hue(id), 0, 359.99));
    }

    [Fact]
    public void The_Second_Line_Says_What_State_The_Pick_Is_In()
    {
        var program = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var site = Pick.ForSite("Beta", "example.org", PageIds.Media);
        Assert.Equal("closed", PickItems.Subtitle(program, PickStatus.Closed));
        Assert.Equal("open", PickItems.Subtitle(program, new PickStatus(true, true, 1, [1])));
        Assert.Equal("3 windows", PickItems.Subtitle(program, new PickStatus(true, true, 3, [1, 2, 3])));
        Assert.Equal("2 tabs", PickItems.Subtitle(site, new PickStatus(true, true, 2, [1, 2])));
        Assert.Equal("website", PickItems.Subtitle(site, PickStatus.Unknown));
    }

    [Fact]
    public void A_Closed_Pick_Is_Marked_Closed_But_An_Unknown_Site_Is_Not()
    {
        var program = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var site = Pick.ForSite("Beta", "example.org", PageIds.Media);
        Assert.True(PickItems.For(new PickRow(program, PickStatus.Closed), null).IsClosed);
        Assert.False(PickItems.For(new PickRow(site, PickStatus.Unknown), null).IsClosed);
        var open = PickItems.For(new PickRow(program, new PickStatus(true, true, 2, [1, 2])), null);
        Assert.Equal(2, open.Count);
        Assert.Equal(program.Id, open.PickId);
    }

    [Fact]
    public void The_Capsule_Width_For_No_Items_Has_No_Item_Term()
    {
        Assert.Equal(20 + 36 + 12 + 0 + 14 + 130 + 10 + 28 + 20, CapsuleLayout.Width(0, false));
        Assert.Equal(466, CapsuleLayout.Width(3, true));   // unchanged
        Assert.Equal(502, CapsuleLayout.Width(5, false));  // unchanged
    }

    [Fact]
    public void Fit_Gives_At_Least_One_Tile_And_Never_A_Capsule_Wider_Than_The_Space()
    {
        Assert.Equal(1, PageFit.MaxTiles(10, false));
        var n = PageFit.MaxTiles(800, false);
        Assert.True(CapsuleLayout.Width(n, false) <= 800);
        Assert.True(CapsuleLayout.Width(n + 1, false) > 800);
    }
}
