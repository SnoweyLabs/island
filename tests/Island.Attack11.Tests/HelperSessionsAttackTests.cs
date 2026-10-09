using Island.App;
using Island.Core;
using Island.Core.Agents.Sessions;
using Island.Core.Terminals;
using TermProcess = Island.Core.Terminals.ProcessFact;

namespace Island.Attack11.Tests;

/// <summary>
/// The app's own file src/Island.App/HelperSessions.cs (linked into this project: it uses Island.Core only) joined to the page logic the way TerminalsPage joins them:
/// messages in, a reading of the process list, the facts out, the tiles worked out. Invented windows, ids and processes only.
/// </summary>
public class HelperSessionsAttackTests
{
    private static readonly TerminalTables Tables = TerminalTables.Default;

    /// <summary>What TerminalsPage.ProbeOnce does with one reading, without the window.</summary>
    private static IReadOnlyList<TerminalTile> Probe(HelperSessions helpers, TerminalPage page, IReadOnlyList<TermWindowFact> windows, IReadOnlyList<ConsoleWindowFact> consoles, IReadOnlyList<TermProcess> processes)
    {
        var pageWindows = PageWindows.From(windows, Tables);
        helpers.ApplyReading(processes, HelperFinder.FindByName(processes, pageWindows, Tables), [.. pageWindows.AiWindows.Select(w => w.OwnerProcessId).Distinct()]);
        return page.Read(new TerminalReading(windows, consoles, processes, helpers.Facts()));
    }

    private static SessionMessage Msg(string ev, string kind, string sid, long t, int[] chain, string helper = "claude", string folder = @"Q:\Invented\Alpha") =>
        new(helper, ev, kind, sid, t, folder, chain);

    // ---- A terminal with a helper, end to end ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Helper_In_A_Terminal_Goes_Through_Every_Signal_To_Its_Ring()
    {
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        TermWindowFact[] windows = [Fact.Term(1, 100, "Alpha")];
        ConsoleWindowFact[] consoles = [Fact.Console(31, 200, 1)];
        TermProcess[] processes = [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "pwsh.exe"), Fact.Proc(300, 200, "claude.exe")];

        Assert.Equal(HelperState.Idle, Probe(helpers, page, windows, consoles, processes)[0].Ring);
        clock.Now += 10;
        helpers.Apply(Msg("UserPromptSubmit", "", "s1", clock.Now, [9001, 300, 200, 100]));
        var tile = Probe(helpers, page, windows, consoles, processes)[0];
        Assert.Equal(HelperState.Working, tile.Ring);
        Assert.Equal("Alpha", tile.FirstLine);
        Assert.Equal("Claude Code · working", tile.SecondLine);

        clock.Now += 10;
        helpers.Apply(Msg("Notification", "permission_prompt", "s1", clock.Now, [9002, 300, 200, 100]));
        Assert.Equal(HelperState.NeedsYou, Probe(helpers, page, windows, consoles, processes)[0].Ring);

        clock.Now += 10;
        helpers.Apply(Msg("Stop", "", "s1", clock.Now, [9003, 300, 200, 100]));
        Assert.Equal(HelperState.Finished, Probe(helpers, page, windows, consoles, processes)[0].Ring);

        clock.Now += 10;
        helpers.Apply(Msg("SessionEnd", "", "s1", clock.Now, [9004, 300, 200, 100]));
        Assert.Equal(HelperState.Idle, Probe(helpers, page, windows, consoles, processes)[0].Ring);
    }

    [Fact]
    public void Holds_The_Helper_Process_Going_Away_Turns_The_Tile_Back_Into_A_Plain_Terminal()
    {
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        TermWindowFact[] windows = [Fact.Term(1, 100, "Alpha")];
        ConsoleWindowFact[] consoles = [Fact.Console(31, 200, 1)];
        TermProcess[] with = [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "pwsh.exe"), Fact.Proc(300, 200, "claude.exe")];
        TermProcess[] without = [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "pwsh.exe")];

        clock.Now += 10;
        helpers.Apply(Msg("UserPromptSubmit", "", "s1", clock.Now, [9001, 300, 200, 100]));
        Assert.Equal(HelperState.Working, Probe(helpers, page, windows, consoles, with)[0].Ring);
        var tile = Probe(helpers, page, windows, consoles, without)[0];
        Assert.Equal(HelperState.Idle, tile.Ring);
        Assert.Equal("terminal", tile.SecondLine);
    }

    private static (List<TermWindowFact> Windows, List<ConsoleWindowFact> Consoles, List<TermProcess> Processes) Many(int count)
    {
        var windows = new List<TermWindowFact>();
        var consoles = new List<ConsoleWindowFact>();
        var processes = new List<TermProcess> { Fact.Proc(100, 4, "WindowsTerminal.exe") };
        for (var i = 0; i < count; i++)
        {
            windows.Add(Fact.Term(1000 + i, 100, $"Alpha {i}", rank: i));
            consoles.Add(Fact.Console(3000 + i, 7000 + i, 1000 + i));
            processes.Add(Fact.Proc(7000 + i, 100, "pwsh.exe"));
            processes.Add(Fact.Proc(8000 + i, 7000 + i, "claude.exe"));
        }

        return (windows, consoles, processes);
    }

    [Fact]
    public void Holds_Sixty_Terminals_Each_With_Its_Own_Helper_And_State()
    {
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        var (windows, consoles, processes) = Many(60);
        for (var i = 0; i < 60; i++)
        {
            clock.Now += 1;
            var ev = (i % 3) switch { 0 => ("UserPromptSubmit", ""), 1 => ("Notification", "permission_prompt"), _ => ("Stop", "") };
            helpers.Apply(Msg(ev.Item1, ev.Item2, "s" + i, clock.Now, [9000 + i, 8000 + i, 7000 + i, 100]));
        }

        var tiles = Probe(helpers, page, windows, consoles, processes);
        Assert.Equal(60, tiles.Count);
        for (var i = 0; i < 60; i++)
        {
            var expected = (i % 3) switch { 0 => HelperState.Working, 1 => HelperState.NeedsYou, _ => HelperState.Finished };
            Assert.True(tiles[i].Ring == expected, $"tile {i}: {tiles[i].Ring}");
        }
    }

    [Fact]
    public void Holds_A_Hundred_Terminals_Each_With_A_Helper_Keep_A_Hundred_Tiles_And_At_Most_Sixty_Four_States()
    {
        // Documented, by the work order's own cap (64 sessions, the least recently heard goes first): above 64 helpers at once the quietest lose their state. The sessions that
        // are only found by name take slots too and are made again at every reading (see the report, A11-10).
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        var (windows, consoles, processes) = Many(100);
        for (var i = 0; i < 100; i++)
        {
            clock.Now += 1;
            helpers.Apply(Msg("UserPromptSubmit", "", "s" + i, clock.Now, [9000 + i, 8000 + i, 7000 + i, 100]));
        }

        for (var n = 0; n < 5; n++)
        {
            var tiles = Probe(helpers, page, windows, consoles, processes);
            Assert.Equal(100, tiles.Count);
            Assert.True(tiles.Count(t => t.Ring == HelperState.Working) <= SessionLimits.MaxSessions);
            Assert.True(helpers.Count <= SessionLimits.MaxSessions);
        }
    }

    [Fact]
    public void Defect_Helpers_Found_Only_By_Name_Push_Out_The_Sessions_That_Have_A_State()
    {
        // FINDING A11-10 (LOW: it needs more than 64 helper processes at once). The book keeps at most 64 sessions and drops "the one heard from least recently". A helper found only
        // by its name is a session that was never heard from, but it is made new at its first reading, so it ranks as the most recent: with 65 or more helper processes the
        // sessions that really have a state (they were heard) are the ones dropped, and the next reading makes the nameless ones again. Measured: with 65 helpers, ten of them working, the page
        // shows 9 rings on the first reading and 0 from the second on; with 70, 4 then 0; with 100, 0. Expected: the ten working helpers keep their rings (a session that was heard
        // is never dropped for one that was not).
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        var (windows, consoles, processes) = Many(70);
        var pageWindows = PageWindows.From(windows, Tables);
        for (var n = 0; n < 2; n++) helpers.ApplyReading(processes, HelperFinder.FindByName(processes, pageWindows, Tables), []);
        for (var i = 0; i < 10; i++)
        {
            clock.Now += 1;
            helpers.Apply(Msg("UserPromptSubmit", "", "s" + i, clock.Now, [9000 + i, 8000 + i, 7000 + i, 100]));
        }

        var rings = 0;
        for (var n = 0; n < 4; n++)
        {
            helpers.ApplyReading(processes, HelperFinder.FindByName(processes, pageWindows, Tables), []);
            rings = page.Read(new TerminalReading(windows, consoles, processes, helpers.Facts())).Count(t => t.Ring == HelperState.Working);
        }

        Assert.Equal(10, rings);
    }

    [Fact]
    public void Defect_A_Connected_Helper_Inside_A_Helper_Is_Counted_Though_The_Work_Order_Says_It_Is_Not()
    {
        // FINDING A11-11 (LOW, a literal reading of work order section 2: "a helper process that has another helper process among its ancestors is not counted: it belongs to that one"; T1 reported
        // the same doubt, its report 4). The exception is applied to helpers found by name only. A Codex that Claude Code started (a tool call) and that is connected reports through a hook;
        // its session hangs on its own process, which the page does not drop, so the one terminal shows two helpers: two dots, and the tile reads "Codex" as long as it is the more urgent.
        // Expected: one helper in the window (no dots), the outer one.
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        TermWindowFact[] windows = [Fact.Term(1, 100, "Alpha")];
        ConsoleWindowFact[] consoles = [Fact.Console(31, 200, 1)];
        TermProcess[] processes = [Fact.Proc(100, 4, "WindowsTerminal.exe"), Fact.Proc(200, 100, "pwsh.exe"), Fact.Proc(300, 200, "claude.exe"), Fact.Proc(310, 300, "codex.exe")];
        clock.Now += 10;
        helpers.Apply(Msg("UserPromptSubmit", "", "inner", clock.Now, [9001, 310, 300, 200, 100], helper: "codex"));
        Probe(helpers, page, windows, consoles, processes);
        var tile = Probe(helpers, page, windows, consoles, processes)[0];
        Assert.Equal(0, tile.Dots);
        Assert.Equal("Claude Code", tile.HelperName);
    }

    // ---- The Claude program with its own window: many conversations ---------------------------------------------------------------------------------------

    private static TermWindowFact[] ClaudeWindow() => [Fact.Ai(5, 500, "Alpha chat", "Claude.exe")];

    private static TermProcess[] ClaudeProcesses() => [Fact.Proc(500, 4, "Claude.exe"), Fact.Proc(9001, 500, "node.exe"), Fact.Proc(9002, 500, "node.exe")];

    [Fact]
    public void Holds_One_Conversation_In_The_Claude_Program_Gives_Its_Tile_The_Ring_And_Keeps_Its_Texts()
    {
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        clock.Now += 10;
        helpers.Apply(Msg("UserPromptSubmit", "", "c1", clock.Now, [9001, 500]));
        var tile = Probe(helpers, page, ClaudeWindow(), [], ClaudeProcesses())[0];
        Assert.Equal(WindowRole.AiProgram, tile.Kind);
        Assert.Equal("Claude", tile.FirstLine);
        Assert.Equal(HelperState.Working, tile.Ring);
    }

    [Fact]
    public void Defect_Two_Conversations_In_The_Claude_Program_Collapse_To_The_One_Heard_Last()
    {
        // FINDING A11-03. The Claude program owns a window and holds many conversations at once (work order section 3: "such a program can hold many conversations at once").
        // Conversation c1 asks permission (needs you, orange); conversation c2, heard later, is working. Both hang on the one process (500), and HelperFinder.Collect keys a
        // session that hangs on a process by that process id, so c2 REPLACES c1: the ring shows "working" and the question waiting for the person is hidden, and there are no dots.
        // Expected (work order: "the most urgent — needs you, then working, then finished", dots count helpers): needs you, with 2 dots.
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var page = new TerminalPage(Tables);
        clock.Now += 10;
        helpers.Apply(Msg("Notification", "permission_prompt", "c1", clock.Now, [9001, 500]));
        clock.Now += 10;
        helpers.Apply(Msg("UserPromptSubmit", "", "c2", clock.Now, [9002, 500]));
        var tile = Probe(helpers, page, ClaudeWindow(), [], ClaudeProcesses())[0];
        Assert.Equal(2, helpers.Facts().Count); // the book holds both
        Assert.Equal(HelperState.NeedsYou, tile.Ring);
        Assert.Equal(2, tile.Dots);
    }

    // ---- Unguarded places -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_A_Process_List_That_Names_One_Id_Twice_Throws_Out_Of_The_Reading_Of_The_App()
    {
        // FINDING A11-04 (LATENT: one snapshot of the process list names each id once). HelperSessions.ApplyReading builds a dictionary with ToDictionary, which throws on a repeated
        // key; TerminalsPage.ProbeOnce calls it outside any try block, on a timer thread, where an exception ends the whole app. Every other reader of the same list
        // (HelperFinder.FindByName, SessionTracker.ApplyReading) uses TryAdd for exactly this reason. Expected: no exception.
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        TermProcess[] processes = [Fact.Proc(300, 200, "claude.exe"), Fact.Proc(300, 201, "other.exe")];
        var error = Record.Exception(() => helpers.ApplyReading(processes, [], []));
        Assert.Null(error);
    }

    [Fact]
    public void Holds_A_Reading_With_Nothing_In_It_Never_Throws_And_Drops_Every_Session_That_Hung_On_A_Process()
    {
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        helpers.Apply(Msg("UserPromptSubmit", "", "s1", clock.Now, [9001, 300]));
        helpers.ApplyReading([Fact.Proc(300, 4, "claude.exe")], [], []);
        Assert.Single(helpers.Facts());
        helpers.ApplyReading([], [], []);
        Assert.Empty(helpers.Facts());
    }

    [Fact]
    public void Holds_A_Message_For_A_Helper_The_Table_Does_Not_Know_Changes_Nothing_And_Raises_No_Change()
    {
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var raised = 0;
        helpers.Changed += () => raised++;
        helpers.Apply(Msg("UserPromptSubmit", "", "g1", clock.Now, [9001, 300], helper: "gemini"));
        helpers.Apply(Msg("Whatever", "", "g1", clock.Now, [9001, 300]));
        Assert.Equal(0, raised);
        Assert.Equal(0, helpers.Count);
    }

    [Fact]
    public void Holds_The_First_Message_Of_A_Session_Raises_No_Change_Until_A_Reading_Has_Placed_It()
    {
        // Documented (T2 doubt 6): a session is shown only after the reading that chooses its process, so the first message wakes nothing and the ring waits for the next reading.
        var clock = new Steady();
        var helpers = new HelperSessions(Tables, AgentSignalTables.All, clock.Read);
        var raised = 0;
        helpers.Changed += () => raised++;
        helpers.Apply(Msg("UserPromptSubmit", "", "s1", clock.Now, [9001, 300]));
        Assert.Equal(0, raised);
        helpers.ApplyReading([Fact.Proc(300, 4, "claude.exe")], [], []);
        Assert.Equal(1, raised);
        clock.Now += 5;
        helpers.Apply(Msg("Stop", "", "s1", clock.Now, [9002, 300]));
        Assert.Equal(2, raised);
    }
}
