using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-7 section 3: how the capsule is laid out while search is open, and how search opens and ends.</summary>
public class SearchMachineTests
{
    private static Clock Open()
    {
        var c = new Clock(new IslandMachine(5));
        c.M.MainKey(c.Now);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.HasKeyboard);
        return c;
    }

    [Fact]
    public void Search_Lays_The_Capsule_Out_By_Its_Own_Width_And_Page_Comes_Back_On_Close()
    {
        var c = Open();
        var pageWidth = c.M.Width.Target;

        c.M.OpenSearch(SearchLayout.Width(1, 2), c.Now);
        Assert.True(c.M.SearchOpen);
        Assert.False(c.M.ContentsVisible); // the contents go out first
        c.Run(2500);
        Assert.True(c.M.ContentsVisible);
        Assert.Equal(SearchLayout.Width(1, 2), c.M.Width.Target);
        Assert.NotEqual(pageWidth, c.M.Width.Target);
        Assert.True(c.M.HasKeyboard);

        // The text and the matches change the width through the spring.
        c.M.SetSearchWidth(SearchLayout.Width(6, 5), c.Now);
        c.Run(1500);
        Assert.InRange(c.M.DrawnWidth, SearchLayout.Width(6, 5) - 0.2, SearchLayout.Width(6, 5) + 0.2);

        c.M.CloseSearch(c.Now);
        Assert.False(c.M.SearchOpen);
        c.Run(2500);
        Assert.Equal(pageWidth, c.M.Width.Target);
        Assert.True(c.M.IsAtRest);
    }

    [Fact]
    public void The_Pills_Search_Button_Grows_It_Into_The_Capsule_Already_In_Search()
    {
        var c = new Clock(new IslandMachine(5));
        c.M.SetPill(true, true, c.Now);
        c.Run(2500);
        Assert.True(c.M.ShowsPill);

        c.M.OpenSearch(SearchLayout.Width(0, 0), c.Now, keyboard: true);

        Assert.False(c.M.ShowsPill);
        Assert.True(c.M.SearchOpen);
        Assert.True(c.M.HasKeyboard);
        c.Run(2500);
        Assert.Equal(SearchLayout.Width(0, 0), c.M.Width.Target);

        // Windows did not grant the keyboard: the island says so, and a click in the field asks again.
        c.M.FocusLost(c.Now);
        Assert.False(c.M.HasKeyboard);
        c.M.TakeKeyboard(c.Now);
        Assert.True(c.M.HasKeyboard);
        Assert.True(c.M.SearchOpen);
    }

    [Fact]
    public void Search_Ends_When_The_Island_Leaves_Turns_Into_The_Pill_Or_A_Page_Key_Is_Pressed()
    {
        var c = Open();
        c.M.OpenSearch(SearchLayout.Width(0, 0), c.Now);
        c.Run(2500);
        c.M.MainKey(c.Now); // the main key while search is open sends the island away, as always
        Assert.False(c.M.SearchOpen);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);

        var page = Open();
        page.M.OpenSearch(SearchLayout.Width(0, 0), page.Now);
        page.Run(2500);
        page.M.PageKey(PageIds.Vibe, page.Now);
        Assert.False(page.M.SearchOpen);
        Assert.Equal(PageIds.Vibe, page.M.PageId);

        var pill = Open();
        pill.M.SetPill(true, true, pill.Now);
        pill.M.OpenSearch(SearchLayout.Width(0, 0), pill.Now);
        pill.Run(2500);
        pill.M.EscapeKey(pill.Now); // the capsule would leave: it shrinks to the pill, and search is over
        Assert.True(pill.M.ShowsPill);
        Assert.False(pill.M.SearchOpen);
    }

    [Fact]
    public void Search_Does_Nothing_When_Nothing_Is_Shown_Or_Numbers_Are_Nonsense()
    {
        var c = new Clock(new IslandMachine(5));
        c.M.OpenSearch(300, c.Now);
        Assert.False(c.M.SearchOpen);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);

        var open = Open();
        open.M.OpenSearch(double.NaN, open.Now);
        open.M.OpenSearch(300, double.NaN);
        open.M.SetSearchWidth(double.PositiveInfinity, open.Now);
        Assert.False(open.M.SearchOpen);
    }

    [Fact]
    public void The_Layout_Is_The_Pages_Capsule_With_A_Field_Where_The_Chip_And_Picks_Were()
    {
        // 120 wide at least and 240 at most; at most seven tiles show, however many match.
        Assert.Equal(SearchLayout.FieldMinWidth, SearchLayout.FieldWidth(0));
        Assert.Equal(SearchLayout.FieldMaxWidth, SearchLayout.FieldWidth(500));
        Assert.Equal(ChoiceConstants.MaxVisibleTiles, SearchLayout.ShownTiles(40));
        Assert.Equal(SearchLayout.Width(3, 7), SearchLayout.Width(3, 400));
        Assert.True(SearchLayout.Width(3, 4) > SearchLayout.Width(3, 3));
        Assert.True(SearchLayout.Width(30, 3) > SearchLayout.Width(0, 3));
        Assert.Equal(40, SearchLayout.FieldHeight);
        Assert.Equal(20, SearchLayout.FieldRadius);
    }
}
