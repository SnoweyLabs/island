using System.Diagnostics;
using System.Text;
using Island.Core.Terminals;

namespace Island.Attack11.Tests;

/// <summary>WORK-ORDER-11 section 1 attacked from outside: a hundred windows, windows that vanish, odd titles, odd tables. Invented windows only.</summary>
public class TileAttackTests
{
    private static TerminalPage NewPage() => new(Fact.Tables);

    // ---- A hundred terminal windows ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Hundred_Windows_Are_A_Hundred_Tiles_In_The_Order_They_Were_First_Seen()
    {
        var page = NewPage();
        var windows = Enumerable.Range(1, 100).Select(i => Fact.Term(1000 + i, 5000 + i, $"Alpha {i}", rank: i)).ToList();
        var tiles = page.Read(Fact.Reading(windows));
        Assert.Equal(windows.Select(w => w.Handle), tiles.Select(t => t.WindowHandle));

        // a reading that lists them in another order does not move a tile
        var shuffled = windows.AsEnumerable().Reverse().ToList();
        tiles = page.Read(Fact.Reading(shuffled));
        Assert.Equal(windows.Select(w => w.Handle), tiles.Select(t => t.WindowHandle));

        // one goes in the middle, one new comes on the right
        var fewer = windows.Where(w => w.Handle != 1050).Append(Fact.Term(2000, 6000, "Alpha new")).ToList();
        tiles = page.Read(Fact.Reading(fewer));
        Assert.Equal(windows.Where(w => w.Handle != 1050).Select(w => w.Handle).Append(2000), tiles.Select(t => t.WindowHandle));
    }

    [Fact]
    public void Holds_A_Hundred_Windows_Each_With_A_Helper_Work_Out_In_Bounded_Time()
    {
        var page = NewPage();
        var windows = new List<TermWindowFact>();
        var consoles = new List<ConsoleWindowFact>();
        var processes = new List<Island.Core.Terminals.ProcessFact>();
        for (var i = 1; i <= 100; i++)
        {
            windows.Add(Fact.Term(1000 + i, 5000 + i, $"Alpha {i}", rank: i));
            consoles.Add(Fact.Console(3000 + i, 7000 + i, 1000 + i));
            processes.Add(Fact.Proc(7000 + i, 5000 + i, "powershell.exe"));
            processes.Add(Fact.Proc(8000 + i, 7000 + i, "claude.exe"));
        }

        var reading = Fact.Reading(windows, consoles, processes);
        var clock = Stopwatch.StartNew();
        IReadOnlyList<TerminalTile> tiles = [];
        for (var n = 0; n < 20; n++) tiles = page.Read(reading);
        clock.Stop();

        Assert.Equal(100, tiles.Count);
        Assert.All(tiles, t => Assert.Equal("Claude Code", t.FirstLine));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"20 readings of 100 helpers took {clock.Elapsed}");
    }

    [Fact]
    public void Holds_A_Thousand_Windows_Do_Not_Throw_Or_Grow_Without_Bound()
    {
        var page = NewPage();
        var windows = Enumerable.Range(1, 1000).Select(i => Fact.Term(i, i + 10, "Alpha", rank: i)).ToList();
        var clock = Stopwatch.StartNew();
        var tiles = page.Read(Fact.Reading(windows));
        clock.Stop();
        Assert.Equal(1000, tiles.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"1000 windows took {clock.Elapsed}");
    }

    // ---- A window that closes between being listed and being clicked ------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Window_That_Closed_Leaves_The_Other_Tiles_Where_They_Were()
    {
        var page = NewPage();
        var a = Fact.Term(1, 11, "Alpha");
        var b = Fact.Term(2, 12, "Beta");
        var c = Fact.Term(3, 13, "Gamma");
        var before = page.Read(Fact.Reading([a, b, c]));
        var after = page.Read(Fact.Reading([a, c]));

        Assert.Equal([1L, 2L, 3L], before.Select(t => t.WindowHandle));
        Assert.Equal([1L, 3L], after.Select(t => t.WindowHandle));
        Assert.Equal(before[0], after[0]);
        Assert.Equal(before[2], after[1]);
    }

    [Fact]
    public void Holds_A_Closed_Window_That_Comes_Back_Under_The_Same_Handle_Goes_To_The_Right()
    {
        var page = NewPage();
        var a = Fact.Term(1, 11);
        var b = Fact.Term(2, 12);
        page.Read(Fact.Reading([a, b]));
        page.Read(Fact.Reading([b]));
        var tiles = page.Read(Fact.Reading([a, b]));
        Assert.Equal([2L, 1L], tiles.Select(t => t.WindowHandle));
    }

    // ---- Titles ------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("​​")]
    [InlineData("\u0007\u0001")]
    [InlineData("‮‮")]
    [InlineData("⠀⠀")]
    [InlineData("\uD800")] // a lone surrogate
    public void Holds_A_Title_That_Draws_As_Nothing_Reads_As_The_Program_Name(string title)
    {
        var tile = NewPage().Read(Fact.Reading([Fact.Term(1, 11, title)]))[0];
        Assert.Equal("Windows Terminal", tile.FirstLine);
        Assert.Equal("terminal", tile.SecondLine);
    }

    [Fact]
    public void Holds_A_Title_Of_A_Megabyte_Is_Cut_To_Forty_And_Is_Quick()
    {
        var title = new string('A', 1_000_000);
        var clock = Stopwatch.StartNew();
        var tile = NewPage().Read(Fact.Reading([Fact.Term(1, 11, title)]))[0];
        clock.Stop();
        Assert.Equal(TerminalConstants.MaxTitleChars, tile.FirstLine.Length);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), clock.Elapsed.ToString());
    }

    [Fact]
    public void Holds_A_Megabyte_Title_Of_Astral_Letters_Is_Cut_Without_Splitting_A_Pair()
    {
        var title = string.Concat(Enumerable.Repeat("\U0001D49C", 500_000)); // a letter outside the BMP, 2 chars each
        var tile = NewPage().Read(Fact.Reading([Fact.Term(1, 11, title)]))[0];
        Assert.True(tile.FirstLine.Length <= TerminalConstants.MaxTitleChars);
        AssertNoLoneSurrogate(tile.FirstLine);
    }

    [Fact]
    public void Holds_A_Title_Of_Control_Characters_And_Text_Never_Carries_A_Control_Character_Out()
    {
        var title = Ch(0x1B) + "]0;evil" + Ch(0x07) + "Alpha" + Ch(0) + Ch(13) + Ch(10) + "Beta" + Ch(0x202E) + "gnirts" + Ch(0x2028) + "x" + Ch(0x85) + "y";
        var tile = NewPage().Read(Fact.Reading([Fact.Term(1, 11, title)]))[0];
        Assert.DoesNotContain(tile.FirstLine, c => char.IsControl(c) || c == (char)0x202E || c == (char)0x2028 || c == (char)0x2029);
        Assert.False(string.IsNullOrWhiteSpace(tile.FirstLine));
    }

    [Fact]
    public void Holds_A_Title_That_Changes_On_Every_Reading_Never_Moves_Or_Duplicates_A_Tile()
    {
        var page = NewPage();
        var rng = new Random(11);
        for (var n = 0; n < 500; n++)
        {
            var title = Guid.NewGuid().ToString("N") + new string((char)rng.Next(32, 0x2FFF), rng.Next(0, 20));
            var tiles = page.Read(Fact.Reading([Fact.Term(1, 11, title), Fact.Term(2, 12, "Beta " + n), Fact.Ai(3, 13, "Chat " + n)]));
            Assert.Equal([1L, 2L, 3L], tiles.Select(t => t.WindowHandle));
            Assert.All(tiles, t => Assert.True(t.FirstLine.Length <= TerminalConstants.MaxTitleChars && t.SecondLine.Length <= TerminalConstants.MaxTitleChars + 20));
        }
    }

    [Fact]
    public void Holds_A_Title_That_Changes_Does_Not_Change_The_Letters_Of_The_Face()
    {
        var page = NewPage();
        var first = page.Read(Fact.Reading([Fact.Term(1, 11, "Zeta one")]))[0];
        var second = page.Read(Fact.Reading([Fact.Term(1, 11, "Quartz two")]))[0];
        Assert.Equal(first.Face, second.Face);
    }

    [Fact]
    public void Holds_Null_Fields_From_A_Marshalling_Slip_Never_Throw()
    {
        var page = NewPage();
        var odd = new TermWindowFact(1, 11, null, null, null!, null!, 0);
        var odd2 = new TermWindowFact(2, 12, "WindowsTerminal.exe", null, null!, null!, 0);
        var tiles = page.Read(new TerminalReading([odd, odd2, null!], null!, null!, null!));
        Assert.Single(tiles);
        Assert.Equal(2, tiles[0].WindowHandle);
    }

    [Fact]
    public void Holds_A_Whole_Null_Reading_Never_Throws_And_Is_An_Empty_Page()
    {
        var page = NewPage();
        var tiles = page.Read(Fact.Reading([Fact.Term(1, 11)]));
        Assert.Single(tiles);
        Assert.Empty(page.Read(null)); // a null reading is an empty one
    }

    // ---- Which windows --------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_One_Handle_Listed_Twice_Is_One_Tile()
    {
        var tiles = NewPage().Read(Fact.Reading([Fact.Term(1, 11), Fact.Term(1, 11, "Again"), Fact.Term(2, 12)]));
        Assert.Equal([1L, 2L], tiles.Select(t => t.WindowHandle));
    }

    [Fact]
    public void Holds_A_Window_Whose_Class_Is_A_Terminal_Class_Is_A_Terminal_Whatever_Its_Program_Is_Called()
    {
        var odd = Fact.Term(1, 11, "Alpha", exe: "Claude.exe"); // an AI program's name, but the class says terminal
        var tile = NewPage().Read(Fact.Reading([odd]))[0];
        Assert.Equal(WindowRole.Terminal, tile.Kind);
    }

    // ---- Invented tables ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Empty_Tables_Make_No_Tile_And_Do_Not_Throw()
    {
        var page = new TerminalPage(TerminalTables.Empty);
        Assert.Empty(page.Read(Fact.Reading([Fact.Term(1, 11), Fact.Ai(2, 12)], [Fact.Console(3, 13, 1)], [Fact.Proc(13, 1, "claude.exe")])));
        Assert.Empty(new TerminalPage(null!).Read(Fact.Reading([Fact.Term(1, 11)])));
    }

    [Fact]
    public void Holds_A_Table_With_An_Empty_Package_Prefix_Does_Not_Take_Every_Window()
    {
        var tables = new TerminalTables([], [], [new AiProgramRow("Alpha AI", ["alphaai.exe"], [""])], [], new Dictionary<string, HelperColor>());
        var tiles = new TerminalPage(tables).Read(Fact.Reading([new TermWindowFact(1, 11, "other.exe", "Q.Invented_x", "Alpha_Class", "Alpha", 0)]));
        Assert.Empty(tiles);
    }

    private static void AssertNoLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            Assert.False(char.IsSurrogate(text[i]), $"lone surrogate at {i}");
        }
    }

    private static string Ch(int code) => ((char)code).ToString();
}
