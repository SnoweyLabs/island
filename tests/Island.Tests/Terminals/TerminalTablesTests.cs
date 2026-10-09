using Island.Core;
using Island.Core.Terminals;

namespace Island.Tests.Terminals;

public class TerminalTablesTests
{
    private static readonly TerminalTables Real = TerminalTables.Default;

    [Fact]
    public void The_Terminal_Classes_Are_The_Two_Of_The_Work_Order()
    {
        Assert.Equal(["CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass"], Real.TerminalClasses);
        Assert.True(Real.IsTerminalClass("cascadia_hosting_window_class"));
        Assert.False(Real.IsTerminalClass("Notepad"));
        Assert.False(Real.IsTerminalClass(null));
    }

    [Fact]
    public void The_Terminal_Programs_Hold_The_Names_Of_The_Suggestion_List_And_Three_More()
    {
        string[] fromSuggestion = ["WindowsTerminal.exe", "wt.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wsl.exe", "bash.exe", "conhost.exe"];
        string[] added = ["wezterm-gui.exe", "alacritty.exe", "mintty.exe"];

        Assert.Equal([.. fromSuggestion, .. added], Real.TerminalPrograms.Select(r => r.FileName));
        Assert.All(fromSuggestion, f => Assert.False(Real.TerminalProgramOf(f)!.Unconfirmed));
        Assert.All(added, f => Assert.True(Real.TerminalProgramOf(f)!.Unconfirmed));
        Assert.NotNull(Real.TerminalProgramOf("CMD.EXE"));
        Assert.Null(Real.TerminalProgramOf("notepad.exe"));
        Assert.Null(Real.TerminalProgramOf(null));
    }

    [Fact]
    public void The_Real_Suggestion_List_Agrees_With_The_Table()
    {
        // PickSuggest sends these programs to the vibe page; the Terminals page must know them as terminals as well.
        foreach (var exe in new[] { "WindowsTerminal.exe", "wt.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wsl.exe", "bash.exe", "conhost.exe" })
        {
            Assert.Equal(PageIds.Vibe, PickSuggest.PageFor(PickKind.Program, exe, null));
            Assert.NotNull(Real.TerminalProgramOf(exe));
        }
    }

    [Fact]
    public void The_AI_Programs_Start_From_The_Starter_List_And_Are_Marked_Unconfirmed()
    {
        Assert.Equal(["Claude", "ChatGPT", "Antigravity", "Cursor", "Windsurf"], Real.AiPrograms.Select(r => r.Name));
        Assert.All(Real.AiPrograms, r => Assert.True(r.Unconfirmed)); // UNVERIFIED: no official page confirmed any of the names
        foreach (var name in new[] { "Claude", "Antigravity", "Cursor" })
        {
            var starter = StarterPicks.Programs.First(p => p.Name == name);
            var row = Real.AiPrograms.First(r => r.Name == name);
            Assert.Equal(starter.ExeCandidates, row.ExeNames);
            Assert.Equal(starter.PackageHints, row.PackagePrefixes);
        }

        Assert.Equal("Claude", Real.AiProgramOf("CLAUDE.EXE", null)!.Name);
        Assert.Equal("Claude", Real.AiProgramOf("x.exe", "AnthropicPBC.Claude_8wekyb3d8bbwe")!.Name);
        Assert.Equal("ChatGPT", Real.AiProgramOf(null, "OpenAI.Codex_2p2nqsd0c76g0")!.Name);
        Assert.Null(Real.AiProgramOf("code.exe", "Microsoft.Code_1"));
        Assert.Null(Real.AiProgramOf(null, null));
        Assert.Null(Real.AiProgramOf("", ""));
    }

    [Fact]
    public void The_Helper_Programs_Are_Claude_Code_Codex_And_Antigravity()
    {
        Assert.Equal(["claude.exe", "codex.exe", "agy.exe"], Real.HelperPrograms.Select(r => r.FileName));
        Assert.Equal("Claude Code", Real.HelperOf("Claude.exe")!.HelperName);
        Assert.Equal("Codex", Real.HelperOf("codex.exe")!.HelperName);
        Assert.Equal("Antigravity", Real.HelperOf("agy.exe")!.HelperName);
        Assert.True(Real.HelperOf("agy.exe")!.Unconfirmed);
        Assert.Null(Real.HelperOf("node.exe")); // Gemini runs as a general program: no name finds it
        Assert.Null(Real.HelperOf(null));
    }

    [Fact]
    public void The_Helper_Colours_Are_The_Ones_Of_The_Work_Order()
    {
        Assert.Equal(new HelperColor(217, 119, 60), Real.ColorOf("Claude Code"));
        Assert.Equal(new HelperColor(16, 163, 127), Real.ColorOf("Codex"));
        Assert.Equal(new HelperColor(76, 141, 255), Real.ColorOf("Antigravity"));
        Assert.Equal(new HelperColor(142, 117, 255), Real.ColorOf("gemini"));
        Assert.Equal(TerminalConstants.UnknownHelperColor, Real.ColorOf("Somebody"));
        Assert.Equal(TerminalConstants.UnknownHelperColor, Real.ColorOf(null));
    }

    [Fact]
    public void The_Real_Tables_Make_Tiles_For_The_Invented_Windows_Of_The_Real_Programs()
    {
        var reading = new TerminalReading(
            [
                new TermWindowFact(1, 10, "WindowsTerminal.exe", null, "CASCADIA_HOSTING_WINDOW_CLASS", "Alpha", 0),
                new TermWindowFact(2, 11, "Claude.exe", null, "Chrome_WidgetWin_1", "Chat", 1),
                new TermWindowFact(3, 12, "notepad.exe", null, "Notepad", "Notes", 2),
            ],
            [],
            [],
            []);

        var tiles = TerminalTiles.Build(reading, Real);

        Assert.Equal([1L, 2L], tiles.Select(t => t.WindowHandle));
        Assert.Equal("WT", tiles[0].Face.Letters);
        Assert.Equal("Claude", tiles[1].FirstLine);
    }
}

public class FirstSeenTests
{
    [Fact]
    public void A_New_Key_Goes_On_The_Right_And_Nothing_Moves()
    {
        var order = new FirstSeen<int>();

        Assert.Equal([3, 1, 2], order.Order([3, 1, 2]));
        Assert.Equal([3, 1, 2, 0], order.Order([0, 1, 2, 3]));
    }

    [Fact]
    public void A_Key_That_Is_Gone_Is_Forgotten_And_Comes_Back_On_The_Right()
    {
        var order = new FirstSeen<int>();
        order.Order([1, 2, 3]);

        Assert.Equal([1, 3], order.Order([3, 1]));
        Assert.Equal(2, order.Count);
        Assert.Equal([1, 3, 2], order.Order([2, 3, 1]));
    }

    [Fact]
    public void Repeats_And_Nulls_And_Empty_Readings_Are_Survived()
    {
        var order = new FirstSeen<string>();

        Assert.Equal(["b", "a"], order.Order(["b", "a", "b"]));
        Assert.Empty(order.Order(null));
        Assert.Equal(0, order.Count);
        Assert.Equal(["a"], order.Order(["a", null!]));
    }
}
