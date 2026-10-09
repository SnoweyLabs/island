using Island.Core;

namespace Island.Tests;

public class SearchKeysTests
{
    private static SearchState Open(string text = "", int selected = 0, int tiles = 0) => new(true, text, selected, tiles);

    [Theory]
    [InlineData("1", 1)]
    [InlineData("5", 5)]
    [InlineData("0", 0)]
    public void Digits_Switch_Pages_Only_While_The_Field_Is_Empty(string digit, int page)
    {
        // Search closed: a digit switches the page.
        var closed = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed(digit));
        Assert.Equal(SearchKeyAction.SwitchPage, closed.Action);
        Assert.Equal(page, closed.Page);
        Assert.False(closed.State.IsOpen);

        // Search open, field empty: still a page switch, the field stays empty.
        var empty = SearchKeys.Apply(Open(), SearchKey.Typed(digit));
        Assert.Equal(SearchKeyAction.SwitchPage, empty.Action);
        Assert.Equal(page, empty.Page);
        Assert.Equal("", empty.State.Text);

        // Search open with text: the digit is text.
        var typed = SearchKeys.Apply(Open("tu", selected: 1, tiles: 3), SearchKey.Typed(digit));
        Assert.Equal(SearchKeyAction.TextChanged, typed.Action);
        Assert.Equal("tu" + digit, typed.State.Text);
        Assert.Equal(-1, typed.Page);
        Assert.Equal(0, typed.State.Selected);
    }

    [Fact]
    public void A_Digit_Typed_After_The_First_Letter_Opened_Search_Is_Text()
    {
        var opened = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("t"));
        var next = SearchKeys.Apply(opened.State, SearchKey.Typed("2"));

        Assert.Equal("t2", next.State.Text);
    }

    [Fact]
    public void Only_Ascii_Digits_Switch_Pages()
    {
        var result = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("\u0663")); // Arabic-Indic three

        Assert.Equal(SearchKeyAction.Opened, result.Action);
        Assert.Equal("\u0663", result.State.Text);
    }

    [Fact]
    public void A_Character_Opens_Search_With_It_In_The_Field()
    {
        var result = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("t"));

        Assert.Equal(SearchKeyAction.Opened, result.Action);
        Assert.True(result.State.IsOpen);
        Assert.Equal("t", result.State.Text);
    }

    [Fact]
    public void A_Surrogate_Pair_Opens_Search_And_Backspace_Removes_It_Whole()
    {
        var opened = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("\U0001F3B5"));
        var deleted = SearchKeys.Apply(opened.State, SearchKey.Backspace);

        Assert.Equal("\U0001F3B5", opened.State.Text);
        Assert.Equal("", deleted.State.Text);
    }

    [Fact]
    public void Space_Or_A_Control_Character_Does_Not_Open_Search()
    {
        foreach (var text in new[] { " ", "\t", "\u0001", "\u001b", "\r", "" })
        {
            var result = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed(text));

            Assert.Equal(SearchKeyAction.None, result.Action);
            Assert.False(result.State.IsOpen);
        }
    }

    [Fact]
    public void A_Space_Is_Text_Once_There_Is_A_Word_But_Not_A_First_Character()
    {
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(Open(), SearchKey.Typed(" ")).Action);
        Assert.Equal("a ", SearchKeys.Apply(Open("a"), SearchKey.Typed(" ")).State.Text);
    }

    [Fact]
    public void Control_Characters_Are_Dropped_From_Typed_Text()
    {
        var result = SearchKeys.Apply(Open("a"), SearchKey.Typed("b\r\n\u0008c"));

        Assert.Equal("abc", result.State.Text);
    }

    [Fact]
    public void Backspace_Deletes_One_Character_And_Does_Nothing_On_An_Empty_Field()
    {
        Assert.Equal("t", SearchKeys.Apply(Open("tu"), SearchKey.Backspace).State.Text);
        Assert.Equal(SearchKeyAction.TextChanged, SearchKeys.Apply(Open("tu"), SearchKey.Backspace).Action);
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(Open(), SearchKey.Backspace).Action);
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(SearchState.Closed, SearchKey.Backspace).Action);
    }

    [Fact]
    public void Escape_Clears_First_Then_Leaves()
    {
        var state = Open("tu", selected: 1, tiles: 3);

        var cleared = SearchKeys.Apply(state, SearchKey.Escape);
        Assert.Equal(SearchKeyAction.TextChanged, cleared.Action);
        Assert.True(cleared.State.IsOpen);
        Assert.Equal("", cleared.State.Text);
        Assert.Equal(0, cleared.State.Selected);

        var left = SearchKeys.Apply(cleared.State, SearchKey.Escape);
        Assert.Equal(SearchKeyAction.Leave, left.Action);
        Assert.False(left.State.IsOpen);
        Assert.Equal("", left.State.Text);
    }

    [Fact]
    public void Escape_While_Search_Is_Closed_Is_Not_Ours()
    {
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(SearchState.Closed, SearchKey.Escape).Action);
    }

    [Fact]
    public void Left_And_Right_Move_The_Selection_And_Stop_At_The_Ends()
    {
        var state = Open("tu", selected: 0, tiles: 3);

        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(state, SearchKey.Left).Action);   // already at the first
        var one = SearchKeys.Apply(state, SearchKey.Right);
        Assert.Equal(SearchKeyAction.SelectionChanged, one.Action);
        Assert.Equal(1, one.State.Selected);
        var two = SearchKeys.Apply(one.State, SearchKey.Right);
        Assert.Equal(2, two.State.Selected);
        var stuck = SearchKeys.Apply(two.State, SearchKey.Right);
        Assert.Equal(SearchKeyAction.None, stuck.Action);
        Assert.Equal(2, stuck.State.Selected);
        Assert.Equal(1, SearchKeys.Apply(two.State, SearchKey.Left).State.Selected);
    }

    [Fact]
    public void With_No_Tile_Nothing_Moves_Or_Opens_And_Nothing_Throws()
    {
        var state = Open("zzz", selected: 4, tiles: 0);

        foreach (var key in new[] { SearchKey.Left, SearchKey.Right, SearchKey.Enter })
        {
            var result = SearchKeys.Apply(state, key);

            Assert.Equal(SearchKeyAction.None, result.Action);
            Assert.Equal(0, result.State.Selected);
        }
    }

    [Fact]
    public void A_Selection_Beyond_The_Tiles_Is_Pulled_Back()
    {
        var result = SearchKeys.Apply(Open("tu", selected: 9, tiles: 2), SearchKey.Enter);

        Assert.Equal(SearchKeyAction.Activate, result.Action);
        Assert.Equal(1, result.State.Selected);
    }

    [Fact]
    public void Enter_Activates_The_Selected_Tile_Only_When_Search_Is_Open()
    {
        var open = SearchKeys.Apply(Open("tu", selected: 1, tiles: 2), SearchKey.Enter);
        Assert.Equal(SearchKeyAction.Activate, open.Action);
        Assert.Equal(1, open.State.Selected);

        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(SearchState.Closed, SearchKey.Enter).Action);
    }

    [Fact]
    public void Left_And_Right_Are_Not_Ours_While_Search_Is_Closed()
    {
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(SearchState.Closed, SearchKey.Left).Action);
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(SearchState.Closed, SearchKey.Right).Action);
    }

    [Fact]
    public void Text_Beyond_The_Field_Limit_Is_Ignored()
    {
        var full = Open(new string('a', SearchKeys.MaxFieldChars));

        var result = SearchKeys.Apply(full, SearchKey.Typed("b"));

        Assert.Equal(SearchKeyAction.None, result.Action);
        Assert.Equal(SearchKeys.MaxFieldChars, result.State.Text.Length);
        // A huge paste into a closed search neither opens nor throws.
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(SearchState.Closed, SearchKey.Typed(new string('a', 100_000))).Action);
    }

    [Fact]
    public void Right_To_Left_And_Path_Like_Text_Is_Just_Text()
    {
        var rtl = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("\u05e9"));
        var path = SearchKeys.Apply(Open(), SearchKey.Typed("C:\\"));

        Assert.Equal("\u05e9", rtl.State.Text);
        Assert.Equal("C:\\", path.State.Text);
    }

    [Fact]
    public void A_Typed_Change_Resets_The_Tile_Count_For_The_Caller_To_Recount()
    {
        var result = SearchKeys.Apply(Open("t", selected: 2, tiles: 4), SearchKey.Typed("u"));

        Assert.Equal(0, result.State.TileCount);
        Assert.Equal(0, result.State.Selected);
    }
}
