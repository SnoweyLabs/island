using Island.Core.Terminals;
using static Island.Tests.Terminals.TerminalFx;

namespace Island.Tests.Terminals;

public class HelperWindowTests
{
    private static PageWindows PageOf(params TermWindowFact[] windows) => PageWindows.From(windows, Tables);

    [Fact]
    public void A_Hidden_Console_Window_Leads_To_Its_Owner()
    {
        var reading = HelperInWindowOne();

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal(1, tile.WindowHandle);
        Assert.Equal("Claude Code", tile.FirstLine); // no hook has named a project
        Assert.Equal("Alpha", tile.SecondLine); // the window's title
        Assert.Equal(FaceKind.HelperDisc, tile.Face.Kind);
        Assert.Equal(new HelperColor(1, 2, 3), tile.Face.Disc);
        Assert.Equal("CC", tile.Face.Letters);
        Assert.Equal("Claude Code", tile.HelperName);
        Assert.Equal(WindowRole.Terminal, tile.Kind);
        Assert.Equal(HelperState.Idle, tile.Ring);
        Assert.Equal(1L, HelperFinder.WindowOf([100, 200, 300], "", false, [Hidden(50, 200, 1)], PageOf(TermWindow(1, 300))));
    }

    [Fact]
    public void A_Classic_Console_Window_Is_The_Window_Itself()
    {
        // The console window is owned (as a window) by a process outside the chain, so only the console fact can lead to it.
        var page = PageOf(TermWindow(7, 999, "Shell", cls: ClassicConsole, exe: null));
        var console = new ConsoleWindowFact(7, ClassicConsole, 200, 0);

        var found = HelperFinder.WindowOf([100, 200], "", false, [console], page);

        Assert.Equal(7L, found);
        Assert.Null(HelperFinder.WindowOf([100, 200], "", false, [], page)); // without the console fact nothing leads there
    }

    [Fact]
    public void The_Nearest_Process_Of_The_Chain_Wins()
    {
        var page = PageOf(TermWindow(1, 301), TermWindow(2, 401));
        ConsoleWindowFact[] consoles = [Hidden(50, 200, 1), Hidden(51, 300, 2)];

        Assert.Equal(1L, HelperFinder.WindowOf([100, 200, 300], "", false, consoles, page));
        Assert.Equal(2L, HelperFinder.WindowOf([100, 300, 200], "", false, consoles, page));

        // The nearest process that owns a console window decides, even when its window is no terminal of the page: the chain is not walked on.
        ConsoleWindowFact[] firstLeadsNowhere = [Hidden(50, 200, 99), Hidden(51, 300, 1)];
        Assert.Null(HelperFinder.WindowOf([100, 200, 300], "", false, firstLeadsNowhere, page));
    }

    [Fact]
    public void Without_A_Console_Window_The_Old_Guess_Is_Used()
    {
        var page = PageOf(TermWindow(1, 300, "Alpha - beta", rank: 2), TermWindow(2, 300, "Other", rank: 0));

        Assert.Equal(1L, HelperFinder.WindowOf([100, 200, 300], "beta", false, [], page)); // the title with the project's name
        Assert.Equal(2L, HelperFinder.WindowOf([100, 200, 300], "", false, [], page)); // else the one used last
        Assert.Equal(2L, HelperFinder.WindowOf([100, 200, 300], "unknown", false, [], page));

        // Through the whole of it: a session that names its project finds its window by the old guess.
        var reading = Reading(
            [TermWindow(1, 300, "Alpha - beta", rank: 2), TermWindow(2, 300, "Other", rank: 0)],
            processes: [Proc(300, 1, "explorer.exe"), Proc(200, 300, "shell.exe"), Proc(100, 200, HelperAExe)],
            sessions: [Session("Claude Code", "beta", 100, HelperState.Working, 1, 200, 300)]);
        var tiles = Tiles(reading);
        Assert.Equal("beta", tiles[0].FirstLine);
        Assert.Equal("terminal", tiles[1].SecondLine);
    }

    [Fact]
    public void A_Helper_With_No_Window_Makes_No_Tile()
    {
        var reading = Reading(
            [TermWindow(1, 301, "Alpha")],
            [Hidden(50, 200, 77)], // leads to a window that is not on the page
            [Proc(200, 5, "shell.exe"), Proc(100, 200, HelperAExe)]);

        var tiles = Tiles(reading);

        var tile = Assert.Single(tiles); // only the page's own window
        Assert.Equal("Alpha", tile.FirstLine);
        Assert.Equal("terminal", tile.SecondLine);
        Assert.Null(HelperFinder.WindowOf([100, 200], "", false, [], PageOf()));
        Assert.Null(HelperFinder.WindowOf([], "", false, [Hidden(50, 200, 1)], PageOf(TermWindow(1, 300))));
        Assert.Null(HelperFinder.WindowOf(null, "", true, null, PageOf(TermWindow(1, 300))));
    }

    [Fact]
    public void The_AI_Program_Of_The_Same_Name_Is_Not_A_Helper()
    {
        // The AI program's own program has the helper's file name; found by name, it must change no tile.
        var tables = Tables with { AiPrograms = [new AiProgramRow("Bravo", [HelperAExe], [])] };
        var windows = PageWindows.From([new TermWindowFact(5, 100, HelperAExe, null, "Frame", "Chat", 0), TermWindow(1, 300)], tables);
        ProcessFact[] processes = [Proc(100, 5, HelperAExe), Proc(110, 100, HelperAExe), Proc(115, 110, "shell.exe"), Proc(120, 115, HelperAExe), Proc(300, 5, "explorer.exe")];

        var found = HelperFinder.FindByName(processes, windows, tables);

        Assert.Empty(found); // 100 owns the AI window; 110 and 120 hang below it

        var tiles = TerminalTiles.Build(Reading([new TermWindowFact(5, 100, HelperAExe, null, "Frame", "Chat", 0), TermWindow(1, 300)], processes: processes), tables);
        Assert.Equal(["Bravo", "Alpha"], tiles.Select(t => t.FirstLine));
        Assert.All(tiles, t => Assert.Equal(HelperState.Idle, t.Ring));

        // The same program started inside a terminal is a helper: the terminal's window is nearer than the AI program's.
        var inTerminal = HelperFinder.FindByName([Proc(300, 100, "shell.exe"), Proc(130, 300, HelperAExe)], windows, tables);
        Assert.Equal(130, Assert.Single(inTerminal).ProcessId);
    }

    [Fact]
    public void The_Real_Claude_Program_Is_Not_A_Helper_When_It_Owns_An_AI_Window()
    {
        var tables = TerminalTables.Default;
        var windows = PageWindows.From([new TermWindowFact(5, 100, "Claude.exe", null, "Chrome_WidgetWin_1", "Chat", 0)], tables);

        Assert.Empty(HelperFinder.FindByName([Proc(100, 5, "claude.exe"), Proc(101, 100, "claude.exe")], windows, tables));
        Assert.Single(HelperFinder.FindByName([Proc(200, 5, "claude.exe")], windows, tables));
    }

    [Fact]
    public void A_Helper_Started_By_A_Helper_Is_Not_Counted()
    {
        var page = PageOf(TermWindow(1, 300));
        ProcessFact[] processes = [Proc(300, 5, "explorer.exe"), Proc(100, 300, HelperAExe), Proc(110, 100, HelperBExe), Proc(115, 110, "shell.exe"), Proc(120, 115, HelperAExe), Proc(150, 300, HelperBExe)];

        var found = HelperFinder.FindByName(processes, page, Tables);

        Assert.Equal([100, 150], found.Select(h => h.ProcessId)); // 110 and 120 belong to 100
        Assert.Equal(["Claude Code", "Codex"], found.Select(h => h.HelperName));
    }

    [Fact]
    public void Two_Helpers_In_One_Window_Are_One_Tile_With_Dots()
    {
        var windows = new[] { TermWindow(1, 300, "Alpha") };
        ConsoleWindowFact[] consoles = [Hidden(50, 200, 1), Hidden(51, 201, 1)];
        ProcessFact[] shells = [Proc(300, 5, "explorer.exe"), Proc(200, 300, "shell.exe"), Proc(201, 300, "shell.exe")];
        var page = new TerminalPage(Tables);

        // First one helper, then a second one joins it: the tile stays one, gains a dot, and the texts stay with the first seen.
        var one = page.Read(Reading(windows, consoles, [.. shells, Proc(100, 200, HelperAExe)]));
        var two = page.Read(Reading(windows, consoles, [.. shells, Proc(100, 200, HelperAExe), Proc(101, 201, HelperBExe)]));

        Assert.Equal(0, Assert.Single(one).Dots);
        var tile = Assert.Single(two);
        Assert.Equal(2, tile.Dots);
        Assert.Equal("Claude Code", tile.FirstLine);

        // The most urgent one gives the texts, the letters and the ring.
        var urgent = page.Read(Reading(windows, consoles, [.. shells, Proc(100, 200, HelperAExe), Proc(101, 201, HelperBExe)],
            [Session("Codex", "beta", 101, HelperState.NeedsYou, 5), Session("Claude Code", "", 100, HelperState.Working, 6)]));
        var tileU = Assert.Single(urgent);
        Assert.Equal(2, tileU.Dots);
        Assert.Equal("beta", tileU.FirstLine);
        Assert.Equal("Codex · needs you", tileU.SecondLine);
        Assert.Equal("Be", tileU.Face.Letters);
        Assert.Equal(new HelperColor(4, 5, 6), tileU.Face.Disc);
        Assert.Equal(HelperState.NeedsYou, tileU.Ring);

        // Working beats finished, finished beats idle.
        var working = page.Read(Reading(windows, consoles, [.. shells, Proc(100, 200, HelperAExe), Proc(101, 201, HelperBExe)],
            [Session("Codex", "beta", 101, HelperState.Finished, 7), Session("Claude Code", "gamma", 100, HelperState.Working, 8)]));
        Assert.Equal("gamma", Assert.Single(working).FirstLine);
        Assert.Equal(HelperState.Working, working[0].Ring);
    }

    [Fact]
    public void The_Tile_Turns_Back_Into_A_Terminal_In_The_Same_Place()
    {
        var windows = new[] { TermWindow(1, 300, "One"), TermWindow(2, 310, "Two"), TermWindow(3, 320, "Three") };
        ConsoleWindowFact[] consoles = [Hidden(50, 210, 2)];
        ProcessFact[] shells = [Proc(310, 5, "explorer.exe"), Proc(210, 310, "shell.exe")];
        var page = new TerminalPage(Tables);

        var with = page.Read(Reading(windows, consoles, [.. shells, Proc(100, 210, HelperAExe)]));
        var without = page.Read(Reading(windows, consoles, shells));

        Assert.Equal([1L, 2L, 3L], with.Select(t => t.WindowHandle));
        Assert.Equal("Claude Code", with[1].FirstLine);
        Assert.Equal([1L, 2L, 3L], without.Select(t => t.WindowHandle));
        Assert.Equal("Two", without[1].FirstLine);
        Assert.Equal("terminal", without[1].SecondLine);
        Assert.Equal(FaceKind.ProgramIcon, without[1].Face.Kind);
        Assert.Equal(0, without[1].Dots);
        Assert.Null(without[1].HelperName);
    }

    [Fact]
    public void A_Helper_Only_Seen_By_Name_Has_No_Ring_Nor_State_Words()
    {
        var tile = Assert.Single(Tiles(HelperInWindowOne()));

        Assert.Equal(HelperState.Idle, tile.Ring);
        Assert.DoesNotContain("·", tile.SecondLine);
    }

    [Theory]
    [InlineData(HelperState.Working, "Codex · working")]
    [InlineData(HelperState.NeedsYou, "Codex · needs you")]
    [InlineData(HelperState.Finished, "Codex · finished")]
    [InlineData(HelperState.Idle, "Codex")]
    public void The_State_Words_Go_On_The_Second_Line_When_The_Project_Is_Known(HelperState state, string second)
    {
        var reading = HelperInWindowOne(HelperBExe) with { Sessions = [Session("Codex", "Q:\\Invented\\Alpha", 100, state)] };

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal("Q:\\Invented\\Alpha", tile.FirstLine); // a name as given: the project's name is whatever the session carries
        Assert.Equal(second, tile.SecondLine);
        Assert.Equal(state, tile.Ring);
    }

    [Fact]
    public void Without_A_Project_The_Second_Line_Is_The_Title_With_The_State_Words()
    {
        var reading = HelperInWindowOne(title: "Alpha - work") with { Sessions = [Session("Claude Code", "", 100, HelperState.Working)] };

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal("Claude Code", tile.FirstLine);
        Assert.Equal("working · Alpha - work", tile.SecondLine); // the state words first, so that a long title is what gives way (Dan's P22, WORK-ORDER-13)
        Assert.Equal("CC", tile.Face.Letters);
    }

    [Fact]
    public void The_Letters_Come_From_The_Project_When_It_Is_Known()
    {
        var reading = HelperInWindowOne() with { Sessions = [Session("Claude Code", "North-Star", 100, HelperState.Idle)] };

        Assert.Equal("NS", Assert.Single(Tiles(reading)).Face.Letters);
    }

    [Fact]
    public void A_Session_Hanging_On_A_Helper_Found_By_Name_Is_The_Same_Helper()
    {
        var reading = HelperInWindowOne() with { Sessions = [Session("Claude Code", "beta", 100, HelperState.Working, 3)] };

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal(0, tile.Dots); // one helper, not two
        Assert.Equal("beta", tile.FirstLine);
    }

    [Fact]
    public void The_Session_Heard_From_Later_Replaces_An_Earlier_One_On_The_Same_Process()
    {
        var reading = HelperInWindowOne() with
        {
            Sessions = [Session("Claude Code", "late", 100, HelperState.Finished, 9), Session("Claude Code", "early", 100, HelperState.Working, 2)],
        };

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal("late", tile.FirstLine);
        Assert.Equal(HelperState.Finished, tile.Ring);
    }

    [Fact]
    public void A_Session_Whose_Process_Is_Not_Found_By_Name_Still_Finds_Its_Window()
    {
        // A helper that runs as a general program (so its name says nothing) and reported through a hook.
        var reading = Reading(
            [TermWindow(1, 300, "Alpha")],
            [Hidden(50, 200, 1)],
            [Proc(300, 5, "explorer.exe"), Proc(200, 300, "shell.exe"), Proc(100, 200, "node.exe")],
            [Session("Gemini", "beta", 100, HelperState.Working, 1, 200, 300)]);

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal("beta", tile.FirstLine);
        Assert.Equal("Gemini · working", tile.SecondLine);
        Assert.Equal("Be", tile.Face.Letters);
        Assert.Equal(HelperState.Working, tile.Ring);
        Assert.Equal(HelperState.Working, Tiles(reading)[0].Ring);
    }

    [Fact]
    public void A_Session_With_No_Process_Of_Its_Own_Uses_Its_Chain()
    {
        var reading = Reading(
            [TermWindow(1, 300, "Alpha")],
            [Hidden(50, 200, 1)],
            sessions: [Session("Codex", "beta", 0, HelperState.NeedsYou, 1, 200, 300)]);

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal("beta", tile.FirstLine);
        Assert.Equal(HelperState.NeedsYou, tile.Ring);
    }

    [Fact]
    public void A_Session_In_An_AI_Programs_Window_Only_Gains_The_Ring_And_The_Dots()
    {
        var reading = Reading(
            [AiWindow(5, 500, "A chat")],
            processes: [Proc(500, 5, AiExe), Proc(100, 500, HelperAExe), Proc(101, 500, HelperBExe)],
            sessions: [Session("Claude Code", "beta", 100, HelperState.NeedsYou, 2, 500), Session("Codex", "gamma", 101, HelperState.Working, 3, 500)]);

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal(WindowRole.AiProgram, tile.Kind);
        Assert.Equal("Bravo", tile.FirstLine); // its own texts
        Assert.Equal("A chat", tile.SecondLine);
        Assert.Equal(FaceKind.ProgramIcon, tile.Face.Kind); // its own face
        Assert.Equal(HelperState.NeedsYou, tile.Ring);
        Assert.Equal(2, tile.Dots);
        Assert.Null(tile.HelperName);
    }

    [Fact]
    public void A_Helper_Found_Only_By_Name_Never_Reaches_An_AI_Programs_Window()
    {
        // Rule 3 is for a session that a hook reported, and only for that.
        var reading = Reading(
            [AiWindow(5, 500, "A chat")],
            processes: [Proc(500, 5, AiExe), Proc(100, 500, HelperAExe)]);

        var tile = Assert.Single(Tiles(reading));

        Assert.Equal(HelperState.Idle, tile.Ring);
        Assert.Equal(0, tile.Dots);
        Assert.Null(HelperFinder.WindowOf([100, 500], "", false, [], PageOf(AiWindow(5, 500))));
        Assert.Equal(5L, HelperFinder.WindowOf([100, 500], "", true, [], PageOf(AiWindow(5, 500))));
    }

    [Fact]
    public void The_Old_Guess_Is_Limited_To_Terminal_Windows()
    {
        // The nearest process of the chain owns only an AI program's window: that is no terminal, so rule 2 passes it by.
        var page = PageOf(AiWindow(5, 200), TermWindow(1, 300, "Alpha"));

        Assert.Equal(1L, HelperFinder.WindowOf([100, 200, 300], "", false, [], page));
        Assert.Null(HelperFinder.WindowOf([100, 200], "", false, [], page));
    }

    [Fact]
    public void A_Terminal_With_A_Helper_Inside_An_AI_Window_Chain_Prefers_The_Console_Window()
    {
        var page = PageOf(AiWindow(5, 500), TermWindow(1, 300));

        // The nearest console window (rule 1) wins over the AI program higher up.
        Assert.Equal(1L, HelperFinder.WindowOf([100, 200, 500], "", true, [Hidden(50, 200, 1)], page));
    }

    [Fact]
    public void Loops_Repeats_And_Reused_Ids_Are_Survived()
    {
        var page = PageOf(TermWindow(1, 300));
        ProcessFact[] looped = [Proc(100, 200, HelperAExe), Proc(200, 100, "shell.exe"), Proc(300, 300, "explorer.exe"), Proc(100, 300, HelperBExe), Proc(150, 150, HelperAExe)];

        var found = HelperFinder.FindByName(looped, page, Tables);

        Assert.Equal([100, 150], found.Select(h => h.ProcessId)); // the first row of a reused id counts
        Assert.All(found, h => Assert.Equal(h.Chain.Distinct().Count(), h.Chain.Count));
        Assert.Equal([5, 6], HelperFinder.Normalize([0, -3, 5, 5, 6, 0]));
        Assert.Empty(HelperFinder.Normalize(null));
        Assert.Equal(TerminalConstants.MaxChainUsed, HelperFinder.Normalize([.. Enumerable.Range(1, 100)]).Count);
    }
}
