using Island.Core.Agents.Sessions;

namespace Island.Tests.Agents.Sessions;

public class SessionTests
{
    private readonly SessionTestKit _kit = new();

    [Fact]
    public void A_Prompt_Means_Working()
    {
        var r = _kit.Send("UserPromptSubmit");

        Assert.Equal(ApplyOutcome.Applied, r.Outcome);
        Assert.Equal(SessionState.Working, _kit.StateOf(r));
        Assert.Equal("Alpha", _kit.Session(r).Project);
    }

    [Fact]
    public void A_Permission_Request_Means_Needs_You()
    {
        var r = _kit.Send("PermissionRequest", "Bash");

        Assert.Equal(SessionState.NeedsYou, _kit.StateOf(r));
        Assert.Equal("Bash", _kit.Session(r).ToolName);
        var other = _kit.Send("Notification", "permission_prompt", sid: "s2", chain: [300, 50]);
        Assert.Equal(SessionState.NeedsYou, _kit.StateOf(other));
    }

    [Fact]
    public void The_Asked_Tool_Finishing_Means_Working_Again()
    {
        _kit.Send("PermissionRequest", "Bash");

        var r = _kit.Send("PostToolUse", "Bash");

        Assert.Equal(SessionState.Working, _kit.StateOf(r));
        Assert.Equal("", _kit.Session(r).ToolName);
        _kit.Send("PermissionRequest", "Edit");
        Assert.Equal(SessionState.Working, _kit.StateOf(_kit.Send("PostToolUseFailure", "Edit")));
    }

    [Fact]
    public void Another_Tool_Finishing_Does_Not_Clear_Needs_You()
    {
        _kit.Send("PermissionRequest", "Bash");

        var r = _kit.Send("PostToolUse", "Read");

        Assert.Equal(SessionState.NeedsYou, _kit.StateOf(r));
        Assert.Equal("Bash", _kit.Session(r).ToolName);
    }

    [Fact]
    public void A_Needs_You_Without_A_Name_Keeps_The_Name_Held()
    {
        _kit.Send("PermissionRequest", "Bash");

        var r = _kit.Send("Notification", "permission_prompt");

        Assert.Equal(SessionState.NeedsYou, _kit.StateOf(r));
        Assert.Equal("Bash", _kit.Session(r).ToolName);
        // With a name unknown on either side, any tool finishing clears it.
        _kit.Send("Notification", "permission_prompt", sid: "s2", chain: [300, 50]);
        Assert.Equal(SessionState.Working, _kit.StateOf(_kit.Send("PostToolUse", "Read", sid: "s2", chain: [300, 50])));
    }

    [Fact]
    public void A_Late_Tool_Message_Never_Revives_A_Finished_Session()
    {
        _kit.Send("UserPromptSubmit");
        _kit.Send("Stop");

        var r = _kit.Send("PostToolUse", "Bash");

        Assert.Equal(SessionState.Finished, _kit.StateOf(r));
        Assert.Equal(SessionState.Finished, _kit.StateOf(_kit.Send("PostToolUseFailure", "Bash")));
    }

    [Fact]
    public void A_Tool_Message_For_An_Idle_Session_Means_Working()
    {
        var start = _kit.Send("SessionStart", "startup");
        Assert.Equal(SessionState.Idle, _kit.StateOf(start));

        var r = _kit.Send("PostToolUse", "Bash");

        Assert.Equal(SessionState.Working, _kit.StateOf(r));
    }

    [Fact]
    public void Stop_Means_Finished_And_The_Idle_Notice_Too()
    {
        _kit.Send("UserPromptSubmit");
        Assert.Equal(SessionState.Finished, _kit.StateOf(_kit.Send("Stop")));

        _kit.Send("UserPromptSubmit");
        Assert.Equal(SessionState.Finished, _kit.StateOf(_kit.Send("Notification", "idle_prompt")));

        _kit.Send("UserPromptSubmit");
        Assert.Equal(SessionState.Finished, _kit.StateOf(_kit.Send("StopFailure", "rate_limit")));
    }

    [Fact]
    public void An_Older_Message_Changes_Nothing()
    {
        _kit.Send("UserPromptSubmit", t: 5_000);
        var applied = _kit.Send("Stop", t: 6_000);

        var late = _kit.Send("PermissionRequest", "Bash", t: 5_500);

        Assert.Equal(ApplyOutcome.Dropped, late.Outcome);
        Assert.Equal(SessionState.Finished, _kit.StateOf(applied));
        Assert.Equal("", _kit.Session(applied).ToolName);
        // Equal times apply in the order they arrive.
        Assert.Equal(SessionState.Working, _kit.StateOf(_kit.Send("UserPromptSubmit", t: 6_000)));
        // A dropped end does not end it either.
        Assert.Equal(ApplyOutcome.Dropped, _kit.Send("SessionEnd", t: 1_000).Outcome);
        Assert.NotNull(_kit.Tracker.Get(applied.SessionId));
    }

    [Fact]
    public void A_Time_From_The_Future_Counts_As_Now()
    {
        _kit.Now = 10_000;
        _kit.Send("UserPromptSubmit", t: 99_000_000); // counts as 10 000

        // A later honest message (time 10 500, island's reading 10 500) is not older than that.
        _kit.Now = 10_500;
        var r = _kit.Send("Stop", t: 10_500);

        Assert.Equal(ApplyOutcome.Applied, r.Outcome);
        Assert.Equal(SessionState.Finished, _kit.StateOf(r));
        // And a message at 9 999 is older than the clamped 10 000.
        Assert.Equal(ApplyOutcome.Dropped, _kit.Send("UserPromptSubmit", t: 9_999).Outcome);
    }

    [Fact]
    public void A_Version_One_Message_Takes_The_Islands_Own_Reading()
    {
        _kit.Now = 20_000;
        _kit.Send("UserPromptSubmit", t: null);
        _kit.Now = 20_100;

        Assert.Equal(ApplyOutcome.Applied, _kit.Send("Stop", t: null).Outcome);
        Assert.Equal(ApplyOutcome.Dropped, _kit.Send("UserPromptSubmit", t: 19_999).Outcome);
    }

    [Fact]
    public void A_Message_For_An_Unknown_Session_Makes_It()
    {
        Assert.Equal(0, _kit.Tracker.Count);

        var r = _kit.Send("PostToolUse", "Bash", sid: "never-started");

        Assert.Equal(1, _kit.Tracker.Count);
        Assert.Equal(SessionState.Working, _kit.StateOf(r)); // made idle, then applied
        var started = _kit.Send("SessionStart", "resume", sid: "other", chain: [300, 50]);
        Assert.Equal(SessionState.Idle, _kit.StateOf(started)); // started changes nothing more
        Assert.Equal(2, _kit.Tracker.Count);
    }

    [Fact]
    public void An_Unmapped_Event_Is_Ignored_And_Makes_Nothing()
    {
        var r = _kit.Send("PreToolUse", "Bash");

        Assert.Equal(ApplyOutcome.Ignored, r.Outcome);
        Assert.Equal(0, _kit.Tracker.Count);
        Assert.Equal(ApplyOutcome.Ignored, _kit.Send("Notification", "auth_success").Outcome);
    }

    [Fact]
    public void Session_End_Removes_It_And_An_Earlier_Message_Does_Not_Bring_It_Back()
    {
        var working = _kit.Send("UserPromptSubmit", t: 400);
        _kit.ReadUsual();
        Assert.Single(_kit.Tracker.Sessions());

        _kit.Send("SessionEnd", "other", t: 500);

        Assert.Null(_kit.Tracker.Get(working.SessionId));
        Assert.Empty(_kit.Tracker.Sessions());
        Assert.Equal(ApplyOutcome.Dropped, _kit.Send("PostToolUse", "Bash", t: 450).Outcome);
        Assert.Equal(ApplyOutcome.Dropped, _kit.Send("Stop", t: 500).Outcome); // at the very moment of the end: dropped too
        Assert.Empty(_kit.Tracker.Sessions());
        Assert.Equal(1, _kit.Tracker.Count); // the end is remembered, and counts
    }

    [Fact]
    public void An_End_Heard_Before_Anything_Else_Is_Remembered()
    {
        var end = _kit.Send("SessionEnd", t: 500);

        Assert.Equal(ApplyOutcome.Applied, end.Outcome);
        Assert.Null(_kit.Tracker.Get(end.SessionId));
        Assert.Equal(ApplyOutcome.Dropped, _kit.Send("UserPromptSubmit", t: 400).Outcome);
    }

    [Fact]
    public void A_Conversation_Taken_Up_Again_After_Its_End_Is_A_Session_Again()
    {
        var first = _kit.Send("UserPromptSubmit", t: 400);
        _kit.Send("SessionEnd", t: 500);

        var again = _kit.Send("SessionStart", "resume", t: 600);

        Assert.Equal(ApplyOutcome.Applied, again.Outcome);
        Assert.NotEqual(first.SessionId, again.SessionId);
        Assert.Equal(SessionState.Idle, _kit.StateOf(again));
        Assert.Equal(1, _kit.Tracker.Count); // the old end is replaced, not kept beside it
    }

    [Fact]
    public void A_Hooks_Own_Shell_Is_Never_What_A_Session_Hangs_On()
    {
        // The helper is "node.exe": not in the table of helpers, so the nearest process of the chain that
        // is in two readings is chosen, and the hook's own shell (900) is gone from the second.
        var kit = new SessionTestKit("claude.exe");
        var r = kit.Send("UserPromptSubmit", chain: [900, 100, 50]);

        var first = kit.Read(null, SessionTestKit.P(900, 100, "sh.exe"), SessionTestKit.P(100, 50, "node.exe"), SessionTestKit.P(50, 1, "term.exe"));
        Assert.Empty(kit.Tracker.Sessions()); // nothing chosen yet, not shown
        var second = kit.Read(null, SessionTestKit.P(100, 50, "node.exe"), SessionTestKit.P(50, 1, "term.exe"));

        var shown = Assert.Single(kit.Tracker.Sessions());
        Assert.Equal(100, shown.Process!.Id);
        Assert.Equal("node.exe", shown.Process.ExeName);
        Assert.Equal(50, shown.Process.ParentId);
        Assert.Equal(r.SessionId, shown.Id);
        Assert.False(first.Changed && second.Gone.Count > 0);
    }

    [Fact]
    public void A_Helper_By_Name_Wins_Over_A_Nearer_Process()
    {
        var kit = new SessionTestKit("claude.exe");
        kit.Send("UserPromptSubmit", chain: [900, 100, 50]);

        kit.Read(null, SessionTestKit.P(900, 100, "sh.exe"), SessionTestKit.P(100, 50, "claude.exe"), SessionTestKit.P(50, 1, "term.exe"));

        Assert.Equal(100, Assert.Single(kit.Tracker.Sessions()).Process!.Id);
    }

    [Fact]
    public void A_Session_With_Neither_Process_Nor_Window_Is_Not_Shown()
    {
        _kit.Send("UserPromptSubmit", chain: [900, 800]);

        _kit.Read(null, SessionTestKit.P(77, 1, "other.exe"));
        _kit.Read(null, SessionTestKit.P(77, 1, "other.exe"));

        Assert.Empty(_kit.Tracker.Sessions());
        Assert.Equal(1, _kit.Tracker.Count);
    }

    [Fact]
    public void A_Session_Whose_Chain_Holds_An_AI_Window_Owner_Is_Shown_Without_A_Process()
    {
        var r = _kit.Send("UserPromptSubmit", chain: [900, 800]);

        _kit.Read([800], SessionTestKit.P(77, 1, "other.exe"));

        var shown = Assert.Single(_kit.Tracker.Sessions());
        Assert.Equal(r.SessionId, shown.Id);
        Assert.Null(shown.Process);
        _kit.Read([], SessionTestKit.P(77, 1, "other.exe"));
        Assert.Empty(_kit.Tracker.Sessions());
    }

    [Fact]
    public void A_New_Id_On_The_Same_Process_Replaces_The_Old_Session()
    {
        var old = _kit.Send("UserPromptSubmit", sid: "old");
        _kit.ReadUsual();

        var next = _kit.Send("SessionStart", "clear", sid: "new");

        Assert.NotEqual(old.SessionId, next.SessionId);
        Assert.Null(_kit.Tracker.Get(old.SessionId));
        var shown = Assert.Single(_kit.Tracker.Sessions());
        Assert.Equal("new", shown.SessionId);
        Assert.Equal(SessionState.Idle, shown.State);
    }

    [Fact]
    public void A_New_Id_On_The_Same_Process_Replaces_The_Old_One_When_The_Process_Is_Chosen_Later()
    {
        // Two sessions made before any reading, from different first processes of the chain (a hook shell each).
        var a = _kit.Send("UserPromptSubmit", sid: "a", chain: [901, 100, 50]);
        var b = _kit.Send("Stop", sid: "b", chain: [902, 100, 50], t: _kit.Now);
        Assert.Equal(2, _kit.Tracker.Count);

        _kit.ReadUsual();

        var shown = Assert.Single(_kit.Tracker.Sessions());
        Assert.Equal(b.SessionId, shown.Id); // the one heard from last stays
        Assert.Null(_kit.Tracker.Get(a.SessionId));
    }

    [Fact]
    public void A_Version_One_Session_Takes_The_Id_Of_Its_First_Version_Two_Message()
    {
        var one = _kit.Send("UserPromptSubmit", sid: "");
        Assert.Equal("", _kit.Session(one).SessionId);

        var two = _kit.Send("PermissionRequest", "Bash", sid: "abc");

        Assert.Equal(one.SessionId, two.SessionId);
        Assert.Equal("abc", _kit.Session(two).SessionId);
        Assert.Equal(1, _kit.Tracker.Count);
        // From now on the id is the session's: a message with the same id from anywhere is it.
        Assert.Equal(one.SessionId, _kit.Send("Stop", sid: "abc", chain: [777, 50]).SessionId);
    }

    [Fact]
    public void An_Id_Less_And_An_Id_Bearing_Session_On_One_Chosen_Process_Become_One()
    {
        var a = _kit.Send("UserPromptSubmit", sid: "", chain: [901, 100, 50], t: 100);
        var b = _kit.Send("Stop", sid: "abc", chain: [902, 100, 50], t: 200);

        _kit.ReadUsual();

        var shown = Assert.Single(_kit.Tracker.Sessions());
        Assert.Equal(a.SessionId, shown.Id); // the older stays
        Assert.Equal("abc", shown.SessionId); // takes the id
        Assert.Equal(SessionState.Finished, shown.State); // and the newer state
        Assert.NotEqual(b.SessionId, shown.Id);
    }

    [Fact]
    public void A_Process_Owning_An_AI_Window_Holds_Many_Sessions()
    {
        _kit.Send("UserPromptSubmit", sid: "a", chain: [100, 50]);
        _kit.Send("UserPromptSubmit", sid: "b", chain: [100, 50]);
        Assert.Equal(2, _kit.Tracker.Count); // before a reading the first process of a chain proves nothing

        _kit.Read([100], SessionTestKit.P(100, 50, "claude.exe"), SessionTestKit.P(50, 1, "term.exe"));

        Assert.Equal(2, _kit.Tracker.Sessions().Count);
        Assert.Equal(2, _kit.Tracker.Sessions().Select(s => s.Key).Distinct().Count());
    }

    [Fact]
    public void A_Reused_Process_Id_Is_A_Gone_Process()
    {
        var r = _kit.Send("UserPromptSubmit");
        _kit.ReadUsual();
        Assert.Single(_kit.Tracker.Sessions());

        // Same id, another file name.
        var again = _kit.Read(null, SessionTestKit.P(100, 50, "other.exe"), SessionTestKit.P(50, 1, "term.exe"));

        Assert.Equal(r.SessionId, Assert.Single(again.Gone).Id);
        Assert.True(again.Changed);
        Assert.Null(_kit.Tracker.Get(r.SessionId));
        Assert.Equal(0, _kit.Tracker.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("parent")]
    public void A_Missing_Process_Or_Another_Parent_Is_A_Gone_Process(string how)
    {
        _kit.Send("UserPromptSubmit");
        _kit.ReadUsual();

        var next = how == "missing"
            ? _kit.Read(null, SessionTestKit.P(50, 1, "term.exe"))
            : _kit.Read(null, SessionTestKit.P(100, 51, "claude.exe"), SessionTestKit.P(50, 1, "term.exe"));

        Assert.Single(next.Gone);
        Assert.Empty(_kit.Tracker.Sessions());
    }

    [Fact]
    public void A_Session_Found_By_Name_And_Its_First_Message_Are_One()
    {
        var chain = new[] { 100, 50 };
        Assert.True(_kit.Tracker.FoundByName(SessionTestKit.Claude, SessionTestKit.P(100, 50, "claude.exe"), chain));
        var named = Assert.Single(_kit.Tracker.Sessions());
        Assert.Equal(SessionState.Idle, named.State);
        Assert.False(_kit.Tracker.FoundByName(SessionTestKit.Claude, SessionTestKit.P(100, 50, "claude.exe"), chain)); // nothing twice

        var r = _kit.Send("UserPromptSubmit", sid: "abc", chain: [901, 100, 50]);

        Assert.Equal(named.Id, r.SessionId);
        Assert.Equal(1, _kit.Tracker.Count);
        Assert.Equal(SessionState.Working, _kit.Tracker.Sessions()[0].State);
        Assert.Equal("abc", _kit.Tracker.Sessions()[0].SessionId);
    }

    [Fact]
    public void A_First_Message_And_Then_The_Name_Are_One_Session_Too()
    {
        var r = _kit.Send("UserPromptSubmit", chain: [100, 50]);

        _kit.Tracker.FoundByName(SessionTestKit.Claude, SessionTestKit.P(100, 50, "claude.exe"), [100, 50]);

        Assert.Equal(1, _kit.Tracker.Count);
        Assert.Equal(r.SessionId, Assert.Single(_kit.Tracker.Sessions()).Id);
        Assert.Equal(SessionState.Working, _kit.Tracker.Sessions()[0].State);
    }

    [Fact]
    public void The_Most_Urgent_State_Of_A_Window_Wins()
    {
        var working = _kit.Send("UserPromptSubmit", sid: "a", chain: [101, 50]);
        var finished = _kit.Send("Stop", sid: "b", chain: [102, 50]);
        var asking = _kit.Send("PermissionRequest", "Bash", sid: "c", chain: [103, 50]);
        var idle = _kit.Send("SessionStart", sid: "d", chain: [104, 50]);

        Assert.Equal(SessionState.NeedsYou, _kit.Tracker.MostUrgentOf([working.SessionId, finished.SessionId, asking.SessionId, idle.SessionId]));
        Assert.Equal(SessionState.Working, _kit.Tracker.MostUrgentOf([working.SessionId, finished.SessionId, idle.SessionId]));
        Assert.Equal(SessionState.Finished, _kit.Tracker.MostUrgentOf([finished.SessionId, idle.SessionId]));
        Assert.Equal(SessionState.Idle, _kit.Tracker.MostUrgentOf([idle.SessionId]));
        Assert.Null(_kit.Tracker.MostUrgentOf([]));
        Assert.Null(_kit.Tracker.MostUrgentOf([9999]));
        Assert.Equal(SessionState.NeedsYou, SessionTracker.MostUrgent([SessionState.Finished, SessionState.NeedsYou, SessionState.Working]));
    }

    [Fact]
    public void The_Sixty_Fifth_Session_Pushes_Out_The_Quietest()
    {
        var ids = new List<long>();
        for (var i = 0; i < SessionLimits.MaxSessions; i++)
            ids.Add(_kit.Send("UserPromptSubmit", sid: $"s{i}", chain: [1000 + i, 50]).SessionId);
        Assert.Equal(SessionLimits.MaxSessions, _kit.Tracker.Count);
        _kit.Send("PostToolUse", "Bash", sid: "s0", chain: [1000, 50]); // the first is heard from again: the second is now the quietest

        var newest = _kit.Send("UserPromptSubmit", sid: "s64", chain: [2000, 50]);

        Assert.Equal(SessionLimits.MaxSessions, _kit.Tracker.Count);
        Assert.Null(_kit.Tracker.Get(ids[1]));
        Assert.NotNull(_kit.Tracker.Get(ids[0]));
        Assert.NotNull(_kit.Tracker.Get(newest.SessionId));
    }

    [Fact]
    public void Remembered_Ends_Count_Toward_The_Limit()
    {
        for (var i = 0; i < SessionLimits.MaxSessions; i++)
            _kit.Send("SessionEnd", sid: $"e{i}", t: 100 + i, chain: [1000 + i, 50]);
        Assert.Equal(SessionLimits.MaxSessions, _kit.Tracker.Count);

        _kit.Send("UserPromptSubmit", sid: "live", chain: [3000, 50]);

        Assert.Equal(SessionLimits.MaxSessions, _kit.Tracker.Count); // the quietest end made room
    }

    [Fact]
    public void Another_Helper_Is_A_Different_Session_Even_With_The_Same_Id()
    {
        var table = HelperSignalTable.Default.With("codex", [new SignalRow("Stop", null, HelperSignal.Finished)]);
        var tracker = new SessionTracker(() => 1000, table, ["claude.exe"]);

        var a = tracker.Apply(new SessionMessage("claude", "Stop", "", "same", 10, "", [100]));
        var b = tracker.Apply(new SessionMessage("codex", "Stop", "", "same", 10, "", [100]));

        Assert.NotEqual(a.SessionId, b.SessionId);
        Assert.Equal(2, tracker.Count);
        Assert.Equal(ApplyOutcome.Ignored, tracker.Apply(new SessionMessage("gemini", "Stop", "", "same", 10, "", [100])).Outcome);
    }

    [Fact]
    public void The_Key_Follows_The_Work_Order()
    {
        var withId = _kit.Send("UserPromptSubmit", sid: "abc");
        Assert.Equal("claude|sabc", _kit.Session(withId).Key);

        var withoutId = _kit.Send("UserPromptSubmit", sid: "", chain: [300, 50]);
        Assert.Equal("claude|p300", _kit.Session(withoutId).Key);

        _kit.ReadUsual();
        Assert.Equal("claude|h100", _kit.Session(withId).Key); // once the process is chosen, that process
    }
}
