using Island.Core.Agents.Connect;

namespace Island.Tests.Agents.Connect;

public class SignalTableTests
{
    private static readonly string[] SixSignals = ["started", "working", "needs you", "tool done", "finished", "ended"];

    [Fact]
    public void Codex_Events_Map_As_Its_Page_Says()
    {
        // The names and meanings are those of https://learn.chatgpt.com/docs/hooks, read 2026-10-07.
        var map = CodexSignals.Rows.ToDictionary(r => r.Event, r => r.Signal);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["SessionStart"] = "started",
                ["UserPromptSubmit"] = "working",
                ["PermissionRequest"] = "needs you",
                ["PostToolUse"] = "tool done",
                ["Stop"] = "finished",
                ["Interrupt"] = "finished",
                ["SessionEnd"] = "ended",
            },
            map);
        Assert.All(CodexSignals.Rows, r => Assert.Equal("codex", r.Helper));
        Assert.All(CodexSignals.Rows, r => Assert.Contains(r.Signal, SixSignals));
    }

    [Fact]
    public void Codex_Only_Finished_And_Needs_You_Raise_The_Notice()
    {
        var noisy = CodexSignals.Rows.Where(r => r.RaisesNotice).Select(r => r.Event).ToList();

        Assert.Equal(["PermissionRequest", "Stop"], noisy.Order(StringComparer.Ordinal));
        Assert.False(CodexSignals.Find("Interrupt")!.RaisesNotice); // an interruption by the person raises no notice
        Assert.Equal("finished", CodexSignals.Find("Interrupt")!.Signal);
        Assert.All(CodexSignals.Rows.Where(r => r.RaisesNotice), r => Assert.Contains(r.Signal, new[] { "finished", "needs you" }));
    }

    [Fact]
    public void Codex_Session_End_Is_Mapped_But_Never_Written()
    {
        // The page says SessionEnd always runs in the foreground, so an entry for it would make Codex wait.
        var end = CodexSignals.Find("SessionEnd")!;

        Assert.False(end.Written);
        Assert.Equal(CodexSignals.SessionEndNote, end.Note);
        Assert.DoesNotContain("SessionEnd", CodexHooks.WrittenEvents);
        Assert.Equal(["SessionStart", "UserPromptSubmit", "PermissionRequest", "PostToolUse", "Stop", "Interrupt"], CodexHooks.WrittenEvents);
    }

    [Fact]
    public void Codex_An_Event_The_Table_Does_Not_Know_Maps_To_Nothing()
    {
        Assert.Null(CodexSignals.Find("PreToolUse"));
        Assert.Null(CodexSignals.Find("stop")); // exact spelling, as the page has it
        Assert.Null(CodexSignals.Find(null));
        Assert.Null(CodexSignals.Find(""));
    }

    [Fact]
    public void The_Signal_Names_Are_The_Six_The_Island_Knows()
    {
        Assert.Equal(SixSignals, new[] { Signals.Started, Signals.Working, Signals.NeedsYou, Signals.ToolDone, Signals.Finished, Signals.Ended });
    }
}
