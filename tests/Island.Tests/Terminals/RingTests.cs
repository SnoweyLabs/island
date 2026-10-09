using Island.Core;
using Island.Core.Agents.Sessions;
using Island.Core.Terminals;

namespace Island.Tests.Terminals;

/// <summary>WORK-ORDER-11 section 3: the ring around a tile that has a helper in it, and the notice that still comes only for the two old events.</summary>
public class RingTests
{
    [Fact]
    public void Two_Neighbouring_Rings_Never_Touch()
    {
        // The tiles stand ItemGap apart; each ring reaches OuterReach beyond its tile: what is left between two rings is positive.
        var left = 2 * TerminalRing.OuterReach;
        Assert.True(left < LookConstants.ItemGap, $"two rings take {left} of {LookConstants.ItemGap}");
        Assert.Equal(1.5, LookConstants.ItemGap - left, 6);
        Assert.Equal(0.75, TerminalRing.InnerGap);
        Assert.Equal(2.5, TerminalRing.Thickness);
        Assert.Equal(3.25, TerminalRing.OuterReach);
        // The ring lies outside the white ring of a selected tile too (that one is 2 wide just outside the disc): they would overlap, which is why only one is drawn.
        Assert.True(TerminalRing.InnerGap < LookConstants.SelectedRingWidth);
    }

    [Fact]
    public void A_Selected_Tile_With_A_State_Keeps_Its_Glow_And_Its_State_Ring()
    {
        foreach (var state in new[] { HelperState.Working, HelperState.NeedsYou, HelperState.Finished })
        {
            Assert.False(TerminalRing.WhiteRingShown(state, selected: true), $"{state}: the white ring is not drawn on a tile that has a state ring");
            Assert.NotNull(TerminalRing.ColourOf(state)); // the state ring itself stays, selected or not
        }

        // A tile with no state keeps the white ring of a selected tile as on every other page, and an unselected tile has none.
        Assert.True(TerminalRing.WhiteRingShown(HelperState.Idle, selected: true));
        Assert.False(TerminalRing.WhiteRingShown(HelperState.Idle, selected: false));
        Assert.False(TerminalRing.WhiteRingShown(HelperState.Working, selected: false));
    }

    [Fact]
    public void An_Idle_Session_And_A_Plain_Terminal_Have_No_Ring()
    {
        Assert.Null(TerminalRing.ColourOf(HelperState.Idle));
        var tables = TerminalTables.Default;
        var plain = TerminalTiles.Build(new TerminalReading(
            [new TermWindowFact(1, 10, "WindowsTerminal.exe", null, "CASCADIA_HOSTING_WINDOW_CLASS", "a shell", 0)], [], [], []), tables);
        Assert.Equal(HelperState.Idle, Assert.Single(plain).Ring);
    }

    [Fact]
    public void The_Three_Colours_And_The_Arc_Are_The_Ones_The_Order_Names()
    {
        Assert.Equal(new HelperColor(76, 141, 255), TerminalRing.ColourOf(HelperState.Working));
        Assert.Equal(new HelperColor(255, 176, 32), TerminalRing.ColourOf(HelperState.NeedsYou));
        Assert.Equal(new HelperColor(53, 196, 106), TerminalRing.ColourOf(HelperState.Finished));
        Assert.Equal(0.28, TerminalRing.ArcShare);
        Assert.Equal(1.4, TerminalRing.TurnSeconds);
        Assert.Equal(0.12, TerminalRing.TrackAlpha);
        // The arc is a function of time alone: the same moment gives the same place, a turn later too.
        Assert.Equal(TerminalRing.ArcStart(0.35), TerminalRing.ArcStart(0.35 + 1.4), 9);
        Assert.Equal(0.25, TerminalRing.ArcStart(0.35), 9);
        Assert.InRange(TerminalRing.ArcStart(123.456), 0, 1);
    }
}

public class NoticeTests
{
    [Fact]
    public void For_Claude_Code_Only_The_Two_Old_Events_Raise_The_Notice()
    {
        // Every event of Claude Code the table knows, with every kind: the notice comes for Stop and for a Notification of kind permission_prompt, and for nothing else.
        var events = new[]
        {
            "SessionStart", "UserPromptSubmit", "PermissionRequest", "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "SessionEnd",
            "PreToolUse", "SubagentStop", "Interrupt", "",
        };
        var kinds = new[] { "", "permission_prompt", "idle_prompt", "auth_success", "elicitation_dialog", "Bash", "ToolPermission" };
        var raised = new List<string>();
        foreach (var e in events)
        foreach (var k in kinds)
            if (AgentSignalTables.All.RaisesNotice(HelperNames.ClaudeCode, e, k)) raised.Add($"{e}/{k}");

        Assert.Equal(new[] { "Notification/permission_prompt", "Stop/", "Stop/permission_prompt", "Stop/idle_prompt", "Stop/auth_success", "Stop/elicitation_dialog", "Stop/Bash", "Stop/ToolPermission" }.Order(), raised.Order());

        // And exactly as the old classifier says, for every pair.
        foreach (var e in events)
        foreach (var k in kinds)
            Assert.Equal(AgentSignals.Classify(e, k) is not null, AgentSignalTables.All.RaisesNotice(HelperNames.ClaudeCode, e, k));

        // The message of version 2 for these two makes the same notice the version-1 message made, by the one path.
        var stop = new SessionMessage("claude", "Stop", "", "s1", 100, "Q:\\Invented\\island", [10]);
        var ask = new SessionMessage("claude", "Notification", "permission_prompt", "s1", 101, "Q:\\Invented\\island", [10]);
        Assert.Equal(AgentSignal.Finished, AgentNotice.From(stop, AgentSignalTables.All)!.Signal);
        Assert.Equal(AgentSignal.NeedsYourAnswer, AgentNotice.From(ask, AgentSignalTables.All)!.Signal);
        Assert.Equal("island", AgentNotice.From(stop, AgentSignalTables.All)!.ProjectName);
        Assert.Null(AgentNotice.From(new SessionMessage("claude", "UserPromptSubmit", "", "s1", 102, "Q:\\x", [10]), AgentSignalTables.All));
    }

    [Fact]
    public void Codex_Raises_The_Notice_For_Finished_And_For_A_Permission_Request_Only()
    {
        Assert.True(AgentSignalTables.All.RaisesNotice("codex", "Stop", ""));
        Assert.True(AgentSignalTables.All.RaisesNotice("codex", "PermissionRequest", "Bash"));
        Assert.False(AgentSignalTables.All.RaisesNotice("codex", "Interrupt", "")); // the person interrupted: no notice
        Assert.False(AgentSignalTables.All.RaisesNotice("codex", "UserPromptSubmit", ""));
        Assert.False(AgentSignalTables.All.RaisesNotice("codex", "PostToolUse", "Bash"));
        Assert.Equal(HelperSignal.NeedsYou, AgentSignalTables.All.Match("codex", "PermissionRequest", "Bash")!.Signal);
        Assert.Equal("Bash", AgentSignalTables.All.Match("codex", "PermissionRequest", "Bash")!.ToolName);
    }
}
