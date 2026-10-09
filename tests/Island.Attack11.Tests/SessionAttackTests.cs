using Island.Core;
using Island.Core.Agents.Sessions;
using SessionProcess = Island.Core.Agents.Sessions.ProcessFact;

namespace Island.Attack11.Tests;

/// <summary>A clock the test moves by hand: the island's own reading of the steady counter.</summary>
internal sealed class Steady
{
    public long Now { get; set; } = 1_000_000;

    public long Read() => Now;
}

/// <summary>WORK-ORDER-11 section 3 attacked from outside: the session book. Invented sessions, ids and processes only.</summary>
public class SessionAttackTests
{
    private static readonly string[] HelperExes = ["claude.exe", "codex.exe", "agy.exe"];

    private static SessionTracker New(Steady clock, HelperSignalTable? table = null) => new(clock.Read, table ?? AgentSignalTables.All, HelperExes);

    private static SessionMessage Msg(string ev, string kind = "", string sid = "s1", long? t = null, int[]? chain = null, string helper = "claude", string folder = @"Q:\Invented\Alpha") =>
        new(helper, ev, kind, sid, t, folder, chain ?? [900]);

    private static SessionInfo? Of(SessionTracker tracker, ApplyResult r) => tracker.Get(r.SessionId);

    // ---- Every signal in every state, against a model written from the work order -----------------------------------------------------------------------------

    private enum Sig { Started, Working, NeedsA, NeedsNoName, ToolA, ToolB, ToolNoName, Finished }

    // Claude Code's events, or Codex's (a needs-you with no tool name is a PermissionRequest with no tool; finished is Interrupt half of the time)
    private static SessionMessage MessageOf(Sig sig, long t, string helper = "claude") => sig switch
    {
        Sig.Started => Msg("SessionStart", t: t, helper: helper),
        Sig.Working => Msg("UserPromptSubmit", t: t, helper: helper),
        Sig.NeedsA => Msg("PermissionRequest", "ToolA", t: t, helper: helper),
        Sig.NeedsNoName => helper == "codex" ? Msg("PermissionRequest", "", t: t, helper: helper) : Msg("Notification", "permission_prompt", t: t, helper: helper),
        Sig.ToolA => Msg("PostToolUse", "ToolA", t: t, helper: helper),
        Sig.ToolB => Msg("PostToolUse", "ToolB", t: t, helper: helper),
        Sig.ToolNoName => Msg("PostToolUse", "", t: t, helper: helper),
        _ => Msg(helper == "codex" && t % 20 == 0 ? "Interrupt" : "Stop", t: t, helper: helper),
    };

    // The model: idle, working, needs you (with the tool held), finished.
    private static (SessionState State, string Tool) Step((SessionState State, string Tool) m, Sig sig) => sig switch
    {
        Sig.Started => m,
        Sig.Working => (SessionState.Working, ""),
        Sig.NeedsA => (SessionState.NeedsYou, "ToolA"),
        Sig.NeedsNoName => (SessionState.NeedsYou, m.State == SessionState.NeedsYou ? m.Tool : ""),
        Sig.Finished => (SessionState.Finished, ""),
        _ => ToolDone(m, sig switch { Sig.ToolA => "ToolA", Sig.ToolB => "ToolB", _ => "" }),
    };

    private static (SessionState State, string Tool) ToolDone((SessionState State, string Tool) m, string tool) => m.State switch
    {
        SessionState.Idle => (SessionState.Working, ""),
        SessionState.NeedsYou when tool.Length == 0 || m.Tool.Length == 0 || tool == m.Tool => (SessionState.Working, ""),
        _ => m,
    };

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void Holds_Every_Sequence_Of_Four_Signals_Ends_In_The_State_The_Work_Order_Gives(string helper)
    {
        var all = Enum.GetValues<Sig>();
        var checkedCount = 0;
        foreach (var a in all)
        foreach (var b in all)
        foreach (var c in all)
        foreach (var d in all)
        {
            var seq = new[] { a, b, c, d };
            var clock = new Steady();
            var tracker = New(clock);
            var model = (State: SessionState.Idle, Tool: "");
            long id = 0;
            for (var i = 0; i < seq.Length; i++)
            {
                clock.Now += 10;
                var result = tracker.Apply(MessageOf(seq[i], clock.Now, helper));
                Assert.Equal(ApplyOutcome.Applied, result.Outcome);
                id = result.SessionId;
                model = Step(model, seq[i]);
                var info = tracker.Get(id)!;
                Assert.True(info.State == model.State, $"{string.Join(",", seq)} after step {i}: expected {model.State}, got {info.State}");
                if (model.State == SessionState.NeedsYou) Assert.Equal(model.Tool, info.ToolName);
            }

            checkedCount++;
        }

        Assert.Equal(4096, checkedCount);
    }

    [Fact]
    public void Holds_A_Late_Tool_Message_Never_Brings_A_Finished_Session_Back_Even_After_A_Needs_You()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var r = tracker.Apply(Msg("PermissionRequest", "ToolA", t: 10));
        tracker.Apply(Msg("Stop", t: 20));
        tracker.Apply(Msg("PostToolUse", "ToolA", t: 30));
        Assert.Equal(SessionState.Finished, tracker.Get(r.SessionId)!.State);
    }

    // ---- Order, repeats, the future -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Messages_Out_Of_Order_Change_Nothing_When_Older()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var first = tracker.Apply(Msg("UserPromptSubmit", t: 100));
        tracker.Apply(Msg("Stop", t: 200));
        var late = tracker.Apply(Msg("UserPromptSubmit", t: 150));
        Assert.Equal(ApplyOutcome.Dropped, late.Outcome);
        Assert.False(late.Changed);
        Assert.Equal(SessionState.Finished, tracker.Get(first.SessionId)!.State);
    }

    [Fact]
    public void Holds_A_Thousand_Repeats_Of_One_Message_Are_One_Session_Without_Growth()
    {
        var clock = new Steady();
        var tracker = New(clock);
        for (var i = 0; i < 1000; i++) tracker.Apply(Msg("UserPromptSubmit", t: 500));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Holds_Ten_Thousand_Different_Sessions_Keep_At_Most_Sixty_Four()
    {
        var clock = new Steady();
        var tracker = New(clock);
        for (var i = 0; i < 10_000; i++)
        {
            clock.Now += 1;
            tracker.Apply(Msg("UserPromptSubmit", sid: "id" + i, t: clock.Now, chain: [10_000 + i]));
        }

        Assert.Equal(SessionLimits.MaxSessions, tracker.Count);
    }

    [Fact]
    public void Holds_A_Time_From_The_Future_Counts_As_Now_And_Does_Not_Block_Later_Messages()
    {
        var clock = new Steady { Now = 5000 };
        var tracker = New(clock);
        var r = tracker.Apply(Msg("UserPromptSubmit", t: long.MaxValue));
        clock.Now = 6000;
        var next = tracker.Apply(Msg("Stop", t: 5500));
        Assert.Equal(ApplyOutcome.Applied, next.Outcome);
        Assert.Equal(SessionState.Finished, tracker.Get(r.SessionId)!.State);
    }

    [Fact]
    public void Holds_A_Time_Of_Zero_And_A_Negative_Time_Never_Throw()
    {
        var clock = new Steady();
        var tracker = New(clock);
        tracker.Apply(Msg("UserPromptSubmit", t: 0));
        tracker.Apply(Msg("Stop", t: -5));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Holds_An_End_From_The_Future_Does_Not_Silence_The_Session_Forever()
    {
        var clock = new Steady { Now = 5000 };
        var tracker = New(clock);
        tracker.Apply(Msg("SessionEnd", t: long.MaxValue)); // remembered as 5000
        clock.Now = 7000;
        var again = tracker.Apply(Msg("UserPromptSubmit", t: 6500));
        Assert.Equal(ApplyOutcome.Applied, again.Outcome);
    }

    [Fact]
    public void Holds_An_End_Heard_First_Is_Remembered_And_An_Earlier_Message_Does_Not_Revive_It()
    {
        var clock = new Steady();
        var tracker = New(clock);
        tracker.Apply(Msg("SessionEnd", t: 200));
        var late = tracker.Apply(Msg("UserPromptSubmit", t: 100));
        Assert.Equal(ApplyOutcome.Dropped, late.Outcome);
        Assert.Empty(tracker.Sessions());
    }

    // ---- Two sessions with one id, a session id of path characters -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"..\..\Windows\System32")]
    [InlineData("../../etc/passwd")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"\\server\share\x")]
    [InlineData("CON")]
    [InlineData("NUL:")]
    [InlineData("a:b:c")]
    [InlineData("%USERPROFILE%")]
    [InlineData("$(whoami)")]
    [InlineData("..")]
    [InlineData(".")]
    public void Holds_A_Session_Id_Of_Path_Characters_Is_Only_Text_Compared_With_Other_Ids(string id)
    {
        var clock = new Steady();
        var tracker = New(clock);
        var r = tracker.Apply(Msg("UserPromptSubmit", sid: id, t: 10));
        Assert.Equal(ApplyOutcome.Applied, r.Outcome);
        var again = tracker.Apply(Msg("Stop", sid: id, t: 20));
        Assert.Equal(r.SessionId, again.SessionId);
        var other = tracker.Apply(Msg("Stop", sid: id + "x", t: 20, chain: [901]));
        Assert.NotEqual(r.SessionId, other.SessionId);
    }

    [Fact]
    public void Holds_A_Session_Id_Over_The_Limit_Is_Cut_By_The_Book_And_Refused_On_The_Wire()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var r = tracker.Apply(Msg("UserPromptSubmit", sid: new string('x', 1_000_000), t: 10));
        Assert.Equal(SessionLimits.MaxSessionIdChars, tracker.Get(r.SessionId)!.SessionId.Length);
        var wire = "{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"" + new string('x', 65) + "\",\"t\":1,\"f\":\"\",\"c\":[1]}";
        Assert.Null(SessionWire.TryParse(wire));
    }

    [Fact]
    public void Holds_Two_Sessions_With_One_Id_On_Different_Helpers_Are_Two_Sessions()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var a = tracker.Apply(Msg("UserPromptSubmit", sid: "same", t: 10, helper: "claude"));
        var b = tracker.Apply(Msg("Stop", sid: "same", t: 20, helper: "codex", chain: [901]));
        Assert.NotEqual(a.SessionId, b.SessionId);
        Assert.Equal(SessionState.Working, tracker.Get(a.SessionId)!.State);
        Assert.Equal(SessionState.Finished, tracker.Get(b.SessionId)!.State);
    }

    [Fact]
    public void Holds_The_Same_Id_From_Two_Processes_Is_One_Session_As_The_Key_Says()
    {
        // Documented, not a defect by the work order's own key (helper + session id): a conversation taken up in a second terminal under the same id
        // is one session, and the state of both lands on it. Whether Claude Code reuses an id on resume is UNVERIFIED here.
        var clock = new Steady();
        var tracker = New(clock);
        var a = tracker.Apply(Msg("UserPromptSubmit", sid: "same", t: 10, chain: [900]));
        var b = tracker.Apply(Msg("Stop", sid: "same", t: 20, chain: [901]));
        Assert.Equal(a.SessionId, b.SessionId);
    }

    [Fact]
    public void Holds_Ids_That_Differ_Only_By_Characters_Cleaned_Away_Are_One_Session_Documented()
    {
        // The id is cleaned (control and format characters dropped) before it is compared, so "ab" and "a" + U+200B + "b" are one id. Harmless: nothing is keyed on a secret.
        var clock = new Steady();
        var tracker = New(clock);
        var a = tracker.Apply(Msg("UserPromptSubmit", sid: "ab", t: 10));
        var b = tracker.Apply(Msg("Stop", sid: "a" + (char)0x200B + "b", t: 20, chain: [901]));
        Assert.Equal(a.SessionId, b.SessionId);
    }

    // ---- A process id reused by another program -----------------------------------------------------------------------------------------------------------

    private static IReadOnlyList<SessionProcess> Procs(params SessionProcess[] p) => p;

    private static SessionProcess P(int id, int parent, string exe) => new(id, parent, exe);

    [Fact]
    public void Holds_A_Process_Id_Reused_By_Another_Program_Ends_The_Session()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var r = tracker.Apply(Msg("UserPromptSubmit", chain: [4201, 4200]));
        tracker.ApplyReading(Procs(P(4200, 4, "claude.exe"), P(4201, 4200, "pwsh.exe")), []);
        Assert.Single(tracker.Sessions());
        var read = tracker.ApplyReading(Procs(P(4200, 4, "notepad.exe")), []);
        Assert.Empty(tracker.Sessions());
        Assert.Single(read.Gone);
        Assert.Null(tracker.Get(r.SessionId));
    }

    [Fact]
    public void Holds_A_Process_Id_Reused_By_The_Same_Program_Under_Another_Parent_Ends_The_Session()
    {
        var clock = new Steady();
        var tracker = New(clock);
        tracker.Apply(Msg("UserPromptSubmit", chain: [4201, 4200]));
        tracker.ApplyReading(Procs(P(4200, 4, "claude.exe"), P(4201, 4200, "pwsh.exe")), []);
        var read = tracker.ApplyReading(Procs(P(4200, 77, "claude.exe")), []);
        Assert.Empty(tracker.Sessions());
        Assert.Single(read.Gone);
    }

    [Fact]
    public void Holds_A_Reused_Id_Whose_Old_Session_Ended_Starts_A_New_Session_Not_The_Old_One()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var first = tracker.Apply(Msg("UserPromptSubmit", sid: "one", chain: [4201, 4200]));
        tracker.ApplyReading(Procs(P(4200, 4, "claude.exe")), []);
        tracker.ApplyReading(Procs(), []); // gone
        clock.Now += 100;
        var second = tracker.Apply(Msg("UserPromptSubmit", sid: "two", t: clock.Now, chain: [4301, 4200]));
        Assert.NotEqual(first.SessionId, second.SessionId);
        tracker.ApplyReading(Procs(P(4200, 4, "claude.exe")), []);
        var shown = Assert.Single(tracker.Sessions());
        Assert.Equal("two", shown.SessionId);
    }

    [Fact]
    public void Holds_A_Process_List_That_Names_One_Id_Twice_Never_Throws_In_The_Book()
    {
        var clock = new Steady();
        var tracker = New(clock);
        tracker.Apply(Msg("UserPromptSubmit", chain: [4201, 4200]));
        tracker.ApplyReading(Procs(P(4200, 4, "claude.exe"), P(4200, 99, "other.exe"), P(4201, 4200, "pwsh.exe")), []);
        Assert.Single(tracker.Sessions());
    }

    // ---- A session found by name, a helper inside a helper ------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Found_By_Name_Twice_And_Its_First_Message_Are_One_Session()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var claude = new SessionProcess(4200, 4, "claude.exe");
        tracker.FoundByName("claude", claude, [4200, 4100]);
        tracker.FoundByName("claude", claude, [4200, 4100]);
        tracker.Apply(Msg("UserPromptSubmit", sid: "a", chain: [4201, 4200, 4100]));
        tracker.ApplyReading(Procs(claude, P(4201, 4200, "pwsh.exe")), []);
        var shown = Assert.Single(tracker.Sessions());
        Assert.Equal(SessionState.Working, shown.State);
    }

    [Fact]
    public void Holds_A_Hook_Chain_Through_Three_Helpers_Hangs_On_The_Nearest_One()
    {
        var clock = new Steady();
        var tracker = New(clock);
        tracker.Apply(Msg("UserPromptSubmit", sid: "inner", chain: [9001, 320, 310, 300]));
        tracker.ApplyReading(Procs(P(320, 310, "claude.exe"), P(310, 300, "codex.exe"), P(300, 4, "claude.exe"), P(9001, 320, "node.exe")), []);
        var shown = Assert.Single(tracker.Sessions());
        Assert.Equal(320, shown.Process!.Id);
    }

    // ---- Several signals from several helpers -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Table_Of_One_Helper_Does_Not_Answer_For_Another()
    {
        var clock = new Steady();
        var tracker = New(clock);
        Assert.Equal(ApplyOutcome.Ignored, tracker.Apply(Msg("Interrupt", helper: "claude")).Outcome); // Codex's event, not Claude Code's
        Assert.Equal(ApplyOutcome.Applied, tracker.Apply(Msg("Interrupt", helper: "codex", chain: [901])).Outcome);
        Assert.Equal(ApplyOutcome.Ignored, tracker.Apply(Msg("UserPromptSubmit", helper: "gemini", chain: [902])).Outcome); // blocked: no rows
        Assert.Equal(ApplyOutcome.Ignored, tracker.Apply(Msg("UserPromptSubmit", helper: "", chain: [903])).Outcome);
        Assert.Equal(ApplyOutcome.Ignored, tracker.Apply(Msg("", helper: "claude", chain: [904])).Outcome);
    }

    [Fact]
    public void Holds_A_Helper_Name_In_Another_Case_Is_The_Same_Helper()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var a = tracker.Apply(Msg("UserPromptSubmit", sid: "x", helper: "Claude", t: 10));
        var b = tracker.Apply(Msg("Stop", sid: "x", helper: "claude", t: 20));
        Assert.Equal(a.SessionId, b.SessionId);
    }

    [Fact]
    public void Holds_Eight_Threads_Hammering_The_Book_Neither_Throw_Nor_Deadlock()
    {
        var clock = new Steady();
        var tracker = New(clock);
        var errors = new List<Exception>();
        var threads = Enumerable.Range(0, 8).Select(n => new Thread(() =>
        {
            try
            {
                var rng = new Random(n);
                for (var i = 0; i < 3000; i++)
                {
                    clock.Now += 1;
                    switch (rng.Next(4))
                    {
                        case 0: tracker.Apply(Msg("UserPromptSubmit", sid: "s" + rng.Next(80), t: clock.Now, chain: [100 + rng.Next(100), 5])); break;
                        case 1: tracker.Apply(Msg(rng.Next(2) == 0 ? "Stop" : "SessionEnd", sid: "s" + rng.Next(80), t: clock.Now, chain: [100 + rng.Next(100)])); break;
                        case 2: tracker.ApplyReading(Enumerable.Range(100, rng.Next(60)).Select(p => new SessionProcess(p, 5, p % 3 == 0 ? "claude.exe" : "pwsh.exe")).ToList(), [100]); break;
                        default: _ = tracker.Sessions(); _ = tracker.Count; tracker.FoundByName("claude", new SessionProcess(100 + rng.Next(100), 5, "claude.exe")); break;
                    }
                }
            }
            catch (Exception e)
            {
                lock (errors) errors.Add(e);
            }
        })).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) Assert.True(t.Join(TimeSpan.FromSeconds(60)), "a thread did not finish (deadlock?)");
        Assert.Empty(errors);
        Assert.True(tracker.Count <= SessionLimits.MaxSessions);
    }

    // ---- The signal table ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Every_Codex_Row_Of_The_Joined_Table_Maps_To_The_Signal_Its_Row_Names()
    {
        foreach (var row in Island.Core.Agents.Connect.CodexSignals.Rows)
        {
            var match = AgentSignalTables.All.Match("codex", row.Event, "");
            Assert.NotNull(match);
            Assert.True(HelperSignalTable.TryParseSignal(row.Signal, out var expected), row.Signal);
            Assert.Equal(expected, match.Signal);
            Assert.Equal(row.RaisesNotice, match.RaisesNotice);
        }
    }

    [Theory]
    [InlineData("started", true)]
    [InlineData("Needs You", true)]
    [InlineData("tool_done", true)]
    [InlineData("ENDED", true)]
    [InlineData("", false)]
    [InlineData("12", false)]
    [InlineData("started,working", false)]
    [InlineData("finished!", true)]
    [InlineData("0", false)]
    public void Holds_Signal_Names_As_Text_Are_Read_Leniently_And_Never_As_A_Number(string text, bool ok)
    {
        Assert.Equal(ok, HelperSignalTable.TryParseSignal(text, out _));
    }

    [Fact]
    public void Holds_A_Row_With_No_Event_Is_Left_Out_And_A_Blank_Helper_Adds_Nothing()
    {
        var table = HelperSignalTable.Empty.With("alpha", [new SignalRow("", null, HelperSignal.Working), new SignalRow("Go", null, HelperSignal.Working)]);
        Assert.Single(table.RowsOf("alpha"));
        Assert.Same(table, table.With("  ", [new SignalRow("X", null, HelperSignal.Finished)]));
        Assert.Null(table.Match("alpha", null, null));
        Assert.Null(table.Match(null, "Go", null));
    }
}
