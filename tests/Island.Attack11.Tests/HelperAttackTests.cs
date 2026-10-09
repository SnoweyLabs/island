using Island.Core;
using Island.Core.Terminals;
using TermProcess = Island.Core.Terminals.ProcessFact;

namespace Island.Attack11.Tests;

/// <summary>WORK-ORDER-11 section 2 attacked from outside: chains that loop, are empty or name processes that are gone; helpers inside helpers; the AI program named like a helper.</summary>
public class HelperAttackTests
{
    private static TerminalTables T => Fact.Tables;

    private static IReadOnlyList<TerminalTile> Tiles(TerminalReading reading) => new TerminalPage(T).Read(reading);

    // A terminal window 1 owned by process 100; a hidden console window owned by shell 200, which belongs to window 1.
    private static TerminalReading OneShell(params TermProcess[] extra) => Fact.Reading(
        [Fact.Term(1, 100, "Alpha")],
        [Fact.Console(31, 200, 1)],
        [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "powershell.exe"), .. extra]);

    // ---- Chains --------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Helper_Found_By_Name_Under_A_Shell_Takes_The_Tile_Of_Its_Window()
    {
        var tiles = Tiles(OneShell(Fact.Proc(300, 200, "claude.exe")));
        Assert.Equal("Claude Code", tiles[0].FirstLine);
        Assert.Equal(HelperState.Idle, tiles[0].Ring);
    }

    [Fact]
    public void Holds_A_Process_List_That_Loops_Neither_Hangs_Nor_Throws()
    {
        // 300 (claude.exe) -> 301 -> 302 -> 300: a parent loop, as a reused process id can make
        var reading = Fact.Reading([Fact.Term(1, 100)], [Fact.Console(31, 200, 1)],
            [Fact.Proc(300, 302, "claude.exe"), Fact.Proc(301, 300, "node.exe"), Fact.Proc(302, 301, "node.exe"), Fact.Proc(200, 100, "powershell.exe")]);
        var tiles = Tiles(reading);
        Assert.Single(tiles); // the helper is found, but it leads to no window
        Assert.Equal(WindowRole.Terminal, tiles[0].Kind);
    }

    [Fact]
    public void Holds_A_Process_That_Is_Its_Own_Parent_Neither_Hangs_Nor_Throws()
    {
        var tiles = Tiles(Fact.Reading([Fact.Term(1, 100)], [], [Fact.Proc(300, 300, "claude.exe")]));
        Assert.Single(tiles);
    }

    [Fact]
    public void Holds_A_Chain_That_Loops_Is_Cut_At_The_Repeat()
    {
        var chain = new[] { 300, 301, 300, 301, 300, 200, 200 };
        var handle = HelperFinder.WindowOf(chain, "alpha", false, [Fact.Console(31, 200, 1)], PageWindows.From([Fact.Term(1, 100)], T));
        Assert.Equal(1, handle);
    }

    [Fact]
    public void Holds_An_Empty_Chain_And_Chains_Of_Nonsense_Lead_To_No_Window()
    {
        var page = PageWindows.From([Fact.Term(1, 100)], T);
        Assert.Null(HelperFinder.WindowOf([], "alpha", true, [], page));
        Assert.Null(HelperFinder.WindowOf(null, "alpha", true, [], page));
        Assert.Null(HelperFinder.WindowOf([0, -1, int.MinValue], "alpha", true, [Fact.Console(31, 0, 1)], page));
        Assert.Null(HelperFinder.WindowOf([9990, 9991], "alpha", true, [], page)); // processes that are gone: they own nothing
    }

    [Fact]
    public void Holds_A_Chain_Of_Ten_Thousand_Is_Cut_And_Quick()
    {
        var chain = Enumerable.Range(1000, 10_000).ToArray();
        var windows = PageWindows.From([Fact.Term(1, 10_900)], T); // the owner lies beyond the cut
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = HelperFinder.WindowOf(chain, "alpha", true, [], windows);
        clock.Stop();
        Assert.Null(found);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
        Assert.Equal(TerminalConstants.MaxChainUsed, HelperFinder.Normalize(chain).Count);
    }

    [Fact]
    public void Holds_A_Console_Window_Whose_Owner_Window_Is_Gone_Falls_Back_To_The_Old_Guess()
    {
        // the pseudo console window names owner window 77, which is not listed any more: rule 1 gives nothing; rule 2 finds the window owned by the chain's own process
        var reading = Fact.Reading([Fact.Term(1, 100)], [Fact.Console(31, 200, 77)], [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "powershell.exe"), Fact.Proc(300, 200, "claude.exe")]);
        var tiles = Tiles(reading);
        Assert.Equal("Claude Code", tiles[0].FirstLine);
    }

    [Fact]
    public void Holds_A_Console_Window_That_Names_A_Window_That_Is_Not_A_Terminal_Is_Not_Followed()
    {
        // the owner window 2 is an ordinary window: the helper must not turn it into a tile
        var reading = Fact.Reading([Fact.Term(1, 100), Fact.Other(2, 150)], [Fact.Console(31, 200, 2)],
            [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "powershell.exe"), Fact.Proc(300, 200, "claude.exe")]);
        var tiles = Tiles(reading);
        Assert.Equal([1L], tiles.Select(t => t.WindowHandle));
    }

    // ---- A helper inside a helper inside a helper -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Helper_Inside_A_Helper_Inside_A_Helper_Is_One_Helper_The_Outermost()
    {
        var reading = OneShell(Fact.Proc(300, 200, "claude.exe"), Fact.Proc(310, 300, "codex.exe"), Fact.Proc(320, 310, "claude.exe"));
        var found = HelperFinder.FindByName(reading.Processes, PageWindows.From(reading.Windows, T), T);
        Assert.Equal([300], found.Select(f => f.ProcessId));
        var tile = Tiles(reading)[0];
        Assert.Equal("Claude Code", tile.FirstLine);
        Assert.Equal(0, tile.Dots);
    }

    [Fact]
    public void Holds_Two_Separate_Helpers_Are_Two_Helpers_Even_When_One_Is_Deeper_Than_The_Other_Chain()
    {
        var reading = Fact.Reading([Fact.Term(1, 100)], [Fact.Console(31, 200, 1), Fact.Console(32, 210, 1)],
            [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "powershell.exe"), Fact.Proc(210, 100, "cmd.exe"), Fact.Proc(300, 200, "claude.exe"), Fact.Proc(301, 210, "codex.exe")]);
        var tile = Tiles(reading)[0];
        Assert.Equal(2, tile.Dots);
    }

    [Fact]
    public void Defect_Two_Helpers_That_Are_Each_The_Others_Parent_Both_Vanish()
    {
        // FINDING A11-01. A parent loop made of two helper-named processes (a stale parent id that a new process reused, as Windows does) leaves
        // each of them "inside the other", so BOTH are dropped by the "a helper inside a helper is not counted" rule and the terminal shows no helper at all.
        // Expected: the loop is broken somewhere and at least one of them still counts.
        var reading = Fact.Reading([Fact.Term(1, 100)], [Fact.Console(31, 200, 1)],
            [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "powershell.exe"), Fact.Proc(300, 301, "claude.exe"), Fact.Proc(301, 300, "claude.exe")]);
        var found = HelperFinder.FindByName(reading.Processes, PageWindows.From(reading.Windows, T), T);
        Assert.NotEmpty(found);
    }

    // ---- The AI program named like a helper -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Claude_Program_Under_Its_Own_Window_Is_Not_A_Helper_And_Changes_No_Tile()
    {
        var reading = Fact.Reading([Fact.Ai(5, 500, "Alpha chat", "Claude.exe")], [], [Fact.Proc(500, 4, "Claude.exe"), Fact.Proc(510, 500, "claude.exe")]);
        var tiles = Tiles(reading);
        Assert.Single(tiles);
        Assert.Equal(WindowRole.AiProgram, tiles[0].Kind);
        Assert.Equal("Claude", tiles[0].FirstLine);
        Assert.Equal(HelperState.Idle, tiles[0].Ring);
        Assert.Equal(0, tiles[0].Dots);
    }

    [Fact]
    public void Holds_The_Claude_Program_Without_A_Window_Makes_A_Helper_Found_By_Name_But_No_Tile()
    {
        var reading = Fact.Reading([Fact.Term(1, 100)], [], [Fact.Proc(500, 4, "Claude.exe"), Fact.Proc(510, 500, "claude.exe")]);
        var tiles = Tiles(reading);
        Assert.Single(tiles);
        Assert.Equal("Alpha", tiles[0].FirstLine); // the terminal tile is still a plain terminal (its title)
        Assert.Equal("terminal", tiles[0].SecondLine);
    }

    [Fact]
    public void Holds_A_Hook_Session_Inside_The_Claude_Program_Gains_Only_The_Ring_And_The_Dots()
    {
        var reading = Fact.Reading([Fact.Ai(5, 500, "Alpha chat", "Claude.exe")], [], [Fact.Proc(500, 4, "Claude.exe"), Fact.Proc(510, 500, "claude.exe")],
            [Fact.Session("Claude Code", "alpha", [510, 500], 0, HelperState.Working, 1)]);
        var tile = Tiles(reading)[0];
        Assert.Equal("Claude", tile.FirstLine);
        Assert.Equal("Alpha chat", tile.SecondLine);
        Assert.Equal(HelperState.Working, tile.Ring);
        Assert.Equal(FaceKind.ProgramIcon, tile.Face.Kind);
        Assert.Null(tile.HelperName);
    }

    [Fact]
    public void Holds_A_Hook_Session_Never_Draws_A_Tile_For_A_Window_That_Is_Not_On_The_Page()
    {
        var reading = Fact.Reading([Fact.Other(2, 150)], [], [Fact.Proc(150, 4, "alpha.exe")], [Fact.Session("Claude Code", "alpha", [150], 150, HelperState.NeedsYou, 1)]);
        Assert.Empty(Tiles(reading));
    }

    // ---- Sessions the page is handed ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Session_With_Odd_Fields_Never_Throws()
    {
        var odd = new HelperSessionFact(null!, null!, null!, -5, (HelperState)99, long.MinValue);
        var odd2 = new HelperSessionFact("", "", [], 0, HelperState.Working, 0);
        var tiles = Tiles(Fact.Reading([Fact.Term(1, 100)], [], [], [odd, odd2, null!]));
        Assert.Single(tiles);
    }

    [Fact]
    public void Holds_A_State_Out_Of_Range_Draws_No_Ring_Of_Its_Own()
    {
        var reading = OneShell();
        var tiles = Tiles(Fact.Reading(reading.Windows, reading.ConsoleWindows, reading.Processes, [Fact.Session("Claude Code", "alpha", [200], 200, (HelperState)99, 1)]));
        Assert.Equal(WindowRole.Terminal, tiles[0].Kind);
        Assert.Null(TerminalRing.ColourOf(tiles[0].Ring));
    }

    [Fact]
    public void Holds_The_Most_Urgent_Of_Several_Helpers_In_One_Window_Decides_The_Ring()
    {
        var reading = Fact.Reading([Fact.Term(1, 100)], [Fact.Console(31, 200, 1), Fact.Console(32, 210, 1), Fact.Console(33, 220, 1)],
            [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "pwsh.exe"), Fact.Proc(210, 100, "pwsh.exe"), Fact.Proc(220, 100, "pwsh.exe")],
            [
                Fact.Session("Claude Code", "one", [200], 200, HelperState.Finished, 1),
                Fact.Session("Codex", "two", [210], 210, HelperState.NeedsYou, 2),
                Fact.Session("Claude Code", "three", [220], 220, HelperState.Working, 3),
            ]);
        var tile = Tiles(reading)[0];
        Assert.Equal(HelperState.NeedsYou, tile.Ring);
        Assert.Equal(3, tile.Dots);
        Assert.Equal("two", tile.FirstLine);
    }

    [Fact]
    public void Holds_Five_Hundred_Sessions_On_One_Window_Cap_The_Dots()
    {
        var consoles = new List<ConsoleWindowFact>();
        var sessions = new List<HelperSessionFact>();
        var processes = new List<TermProcess> { Fact.Proc(100, 4, "WindowsTerminal.exe") };
        for (var i = 0; i < 500; i++)
        {
            consoles.Add(Fact.Console(3000 + i, 7000 + i, 1));
            processes.Add(Fact.Proc(7000 + i, 100, "pwsh.exe"));
            sessions.Add(Fact.Session("Claude Code", "p" + i, [7000 + i], 7000 + i, HelperState.Working, i));
        }

        var tile = Tiles(Fact.Reading([Fact.Term(1, 100)], consoles, processes, sessions))[0];
        Assert.Equal(ChoiceConstants.MaxDots, tile.Dots);
    }

    [Fact]
    public void Holds_Two_Unplaced_Sessions_Whose_Chains_Start_With_One_Process_Are_One_Helper()
    {
        var sessions = new[]
        {
            Fact.Session("Claude Code", "one", [100], 0, HelperState.Working, 1),
            Fact.Session("Claude Code", "two", [100], 0, HelperState.Finished, 2),
        };
        var helpers = HelperFinder.Collect(Fact.Reading([Fact.Term(1, 100)], [], [], sessions), PageWindows.From([Fact.Term(1, 100)], T), T);
        // keyed by the first id of the chain, so the second replaces the first: one helper (documented in T1's report as "a session with no process")
        Assert.Single(helpers);
        Assert.Equal("two", helpers[0].ProjectName);
    }

    [Fact]
    public void Defect_Two_Unplaced_Sessions_Collide_On_One_Key_Though_Their_Chains_Differ()
    {
        // FINDING A11-02 (LATENT: the app only hands over shown sessions, which always have a chain). A session with no process and no chain is keyed
        // -1 - i, a session whose chain starts with 5 is keyed -5: the fifth session (i = 4) with an empty chain erases the session of chain [5].
        // Expected: two sessions, two helpers.
        var sessions = new List<HelperSessionFact> { Fact.Session("Claude Code", "kept", [5], 0, HelperState.Working, 1) };
        for (var i = 1; i <= 3; i++) sessions.Add(Fact.Session("Claude Code", "other" + i, [900 + i], 0, HelperState.Working, 1 + i));
        sessions.Add(Fact.Session("Claude Code", "lost", [], 0, HelperState.Working, 5));
        var helpers = HelperFinder.Collect(Fact.Reading([], [], [], sessions), PageWindows.From([], T), T);
        Assert.Equal(5, helpers.Count);
    }
}
