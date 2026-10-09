using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Tests.Agents.Sessions;

public class HelperSignalTableTests
{
    private static readonly HelperSignalTable Table = HelperSignalTable.Default;

    [Theory]
    [InlineData("SessionStart", "startup", HelperSignal.Started)]
    [InlineData("SessionStart", "resume", HelperSignal.Started)]
    [InlineData("SessionStart", "", HelperSignal.Started)]
    [InlineData("UserPromptSubmit", "", HelperSignal.Working)]
    [InlineData("PermissionRequest", "Bash", HelperSignal.NeedsYou)]
    [InlineData("Notification", "permission_prompt", HelperSignal.NeedsYou)]
    [InlineData("PostToolUse", "Edit", HelperSignal.ToolDone)]
    [InlineData("PostToolUseFailure", "Edit", HelperSignal.ToolDone)]
    [InlineData("Stop", "", HelperSignal.Finished)]
    [InlineData("StopFailure", "", HelperSignal.Finished)]
    [InlineData("Notification", "idle_prompt", HelperSignal.Finished)]
    [InlineData("SessionEnd", "other", HelperSignal.Ended)]
    [InlineData("SessionEnd", "", HelperSignal.Ended)]
    public void Claude_Codes_Rows_Are_The_Work_Orders(string hookEvent, string kind, HelperSignal expected)
    {
        Assert.Equal(expected, Table.Match("claude", hookEvent, kind)?.Signal);
    }

    [Theory]
    [InlineData("PreToolUse", "Bash")]
    [InlineData("SubagentStop", "")]
    [InlineData("TaskCompleted", "")]
    [InlineData("PreCompact", "")]
    [InlineData("Notification", "auth_success")]
    [InlineData("Notification", "elicitation_dialog")]
    [InlineData("Notification", "agent_completed")]
    [InlineData("Notification", "Permission_Prompt")]
    [InlineData("Notification", "")]
    [InlineData("stop", "")]
    [InlineData("STOP", "")]
    [InlineData("", "")]
    public void Everything_Else_Maps_To_Nothing(string hookEvent, string kind)
    {
        Assert.Null(Table.Match("claude", hookEvent, kind));
    }

    [Fact]
    public void A_Tool_Row_Carries_The_Tool_Name_And_Others_Carry_None()
    {
        Assert.Equal("Bash", Table.Match("claude", "PermissionRequest", "Bash")!.ToolName);
        Assert.Equal("mcp__alpha__run", Table.Match("claude", "PostToolUse", " mcp__alpha__run ")!.ToolName);
        Assert.Equal("", Table.Match("claude", "Notification", "permission_prompt")!.ToolName);
        Assert.Equal("", Table.Match("claude", "SessionStart", "startup")!.ToolName);
        Assert.Equal("", Table.Match("claude", "PermissionRequest", null)!.ToolName);
    }

    [Fact]
    public void Only_Stop_And_The_Permission_Prompt_Raise_The_Notice_And_It_Agrees_With_Agent_Signals()
    {
        var events = new[]
        {
            "SessionStart", "UserPromptSubmit", "PermissionRequest", "Notification", "PostToolUse", "PostToolUseFailure",
            "Stop", "StopFailure", "SessionEnd", "PreToolUse", "SubagentStop", "Setup", "TeammateIdle",
        };
        var kinds = new[] { "", "permission_prompt", "idle_prompt", "auth_success", "Bash", "startup", "other", "elicitation_dialog" };

        var raising = new List<string>();
        foreach (var e in events)
            foreach (var k in kinds)
            {
                var notice = Table.RaisesNotice("claude", e, k);
                Assert.Equal(AgentSignals.Classify(e, k) is not null, notice);
                if (notice) raising.Add($"{e}/{k}");
            }

        Assert.Equal(["Notification/permission_prompt"], raising.Where(r => !r.StartsWith("Stop/")).ToList());
        Assert.Equal(kinds.Length, raising.Count(r => r.StartsWith("Stop/"))); // Stop, whatever its kind
    }

    [Fact]
    public void Another_Helpers_Rows_Join_Without_Changing_The_First_Table()
    {
        var rows = new[]
        {
            new SignalRow("AfterAgent", null, HelperSignal.Finished, RaisesNotice: true),
            new SignalRow("ToolFlag", "wait", HelperSignal.NeedsYou),
            new SignalRow("ToolFlag", null, HelperSignal.Working),
        };

        var joined = Table.With("Codex", rows);

        Assert.Equal(HelperSignal.Finished, joined.Match("codex", "AfterAgent", "")!.Signal);
        Assert.True(joined.RaisesNotice("CODEX", "AfterAgent", ""));
        Assert.Equal(HelperSignal.NeedsYou, joined.Match("codex", "ToolFlag", "wait")!.Signal); // an exact kind first
        Assert.Equal(HelperSignal.Working, joined.Match("codex", "ToolFlag", "other")!.Signal);
        Assert.Equal(HelperSignal.Finished, joined.Match("claude", "Stop", "")!.Signal); // Claude Code kept
        Assert.Null(joined.Match("claude", "AfterAgent", "")); // rows do not cross helpers
        Assert.Null(Table.Match("codex", "AfterAgent", "")); // the original is not changed
        Assert.Equal(["claude", "codex"], joined.Helpers.OrderBy(x => x).ToList());
    }

    [Fact]
    public void A_Helper_Registered_Again_Is_Replaced_And_Odd_Input_Is_Left_Out()
    {
        var once = Table.With("codex", [new SignalRow("A", null, HelperSignal.Working)]);
        var twice = once.With("codex", [new SignalRow("B", null, HelperSignal.Finished), new SignalRow("", null, HelperSignal.Ended)]);

        Assert.Null(twice.Match("codex", "A", ""));
        Assert.NotNull(twice.Match("codex", "B", ""));
        Assert.Single(twice.RowsOf("codex")); // the row with no event is left out
        Assert.Same(twice, twice.With("  ", [new SignalRow("C", null, HelperSignal.Working)])); // a nameless helper adds nothing
        Assert.Empty(twice.RowsOf(null));
        Assert.Null(twice.Match(null, "B", ""));
        Assert.Null(HelperSignalTable.Empty.Match("claude", "Stop", ""));
    }

    [Theory]
    [InlineData("started", HelperSignal.Started)]
    [InlineData("Working", HelperSignal.Working)]
    [InlineData("needs you", HelperSignal.NeedsYou)]
    [InlineData("NeedsYou", HelperSignal.NeedsYou)]
    [InlineData("needs-you", HelperSignal.NeedsYou)]
    [InlineData("tool_done", HelperSignal.ToolDone)]
    [InlineData("finished", HelperSignal.Finished)]
    [InlineData("ended", HelperSignal.Ended)]
    public void A_Signal_Named_As_Text_Is_Understood(string text, HelperSignal expected)
    {
        Assert.True(HelperSignalTable.TryParseSignal(text, out var signal));
        Assert.Equal(expected, signal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("waiting")]
    [InlineData(null)]
    public void An_Unknown_Signal_Name_Is_Refused(string? text)
    {
        Assert.False(HelperSignalTable.TryParseSignal(text, out _));
    }
}
