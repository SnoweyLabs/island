using Island.Core;

namespace Island.Attack7A.Tests;

/// <summary>The agent notice: a thousand notices in a second, project names of a megabyte with control, direction-changing and invisible characters.</summary>
public class AgentAttackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static AgentNotice Note(AgentSignal signal, string session, string name = "island") => new(signal, name, session, [1]);

    [Fact]
    public void Holds_A_Thousand_Notices_In_One_Second_The_Newest_Replaces_And_Nothing_Piles_Up()
    {
        var q = new NoticeQueue();
        for (var i = 0; i < 1000; i++)
        {
            var now = T0.AddMilliseconds(i);
            Assert.True(q.Post(Note(i % 2 == 0 ? AgentSignal.Finished : AgentSignal.NeedsYourAnswer, "p" + i, "proj" + i), now));
            if (i % 7 == 0) _ = q.Update(now, capsuleOpen: i % 3 == 0, pillUp: i % 5 == 0, pointerOver: i % 11 == 0);
        }

        var state = q.Update(T0.AddMilliseconds(1000), false, false, false);
        Assert.Equal("proj999", state.Showing!.ProjectName);
        Assert.False(state.TakesKeyboard);
        // One slot only: after it ends nothing else is left behind.
        Assert.Null(q.Update(T0.AddSeconds(60), false, false, false).Showing);
        Assert.Null(q.Update(T0.AddSeconds(61), false, false, false).Showing);
    }

    [Fact]
    public void Holds_A_Repeated_Stop_For_The_Same_Session_Changes_Nothing_While_It_Is_Still_There()
    {
        var q = new NoticeQueue();
        Assert.True(q.Post(Note(AgentSignal.Finished, "p1"), T0));
        _ = q.Update(T0, false, false, false);
        for (var i = 1; i < 1000; i++) Assert.False(q.Post(Note(AgentSignal.Finished, "p1"), T0.AddMilliseconds(i)));
        Assert.True(q.Post(Note(AgentSignal.Finished, "p2"), T0.AddMilliseconds(1001))); // another session replaces
        Assert.True(q.Post(Note(AgentSignal.NeedsYourAnswer, "p2"), T0.AddMilliseconds(1002))); // a different kind of signal does too
    }

    [Fact]
    public void Defect_A_Stop_Right_After_Needs_Your_Answer_For_The_Same_Session_Is_Swallowed_And_The_Old_Text_Stays()
    {
        var q = new NoticeQueue();
        q.Post(Note(AgentSignal.NeedsYourAnswer, "p1"), T0);
        _ = q.Update(T0, false, false, false);
        // The person answers; the agent finishes two seconds later: the Stop is the news, not a repeat of the same signal.
        var posted = q.Post(Note(AgentSignal.Finished, "p1"), T0.AddSeconds(2));
        Assert.True(posted);
    }

    [Fact]
    public void Defect_A_Clock_Reading_At_The_Last_Representable_Moment_Throws_Out_Of_The_Queue()
    {
        var q = new NoticeQueue();
        q.Post(Note(AgentSignal.Finished, "p1"), DateTimeOffset.MaxValue);
        Assert.Null(Record.Exception(() => q.Update(DateTimeOffset.MaxValue, false, false, false)));
    }

    [Fact]
    public void Holds_A_Notice_That_Waits_Behind_The_Capsule_Is_Never_Shown_While_It_Is_Open_And_Goes_Stale()
    {
        var q = new NoticeQueue();
        q.Post(Note(AgentSignal.Finished, "p1"), T0);
        for (var s = 0; s < 200; s += 5) Assert.Null(q.Update(T0.AddSeconds(s), true, false, false).Showing);
        Assert.Null(q.Update(T0.AddMinutes(6), false, false, false).Showing); // waited past MaxWait
        q.Post(Note(AgentSignal.Finished, "p2"), T0.AddMinutes(7));
        Assert.NotNull(q.Update(T0.AddMinutes(7), false, false, false).Showing);
        for (var s = 0; s < 400; s++) Assert.True(q.Update(T0.AddMinutes(7).AddSeconds(s * 0.1), false, false, pointerOver: true).Showing is not null || s > 0); // hovering keeps it, at most the cap
        Assert.Null(q.Update(T0.AddMinutes(7).AddSeconds(NoticeQueue.HoverCap.TotalSeconds + 40), false, false, true).Showing);
    }

    [Fact]
    public void Holds_Seconds_Is_Always_Between_Three_And_Thirty()
    {
        foreach (var v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0, 2.99, 3, 6, 30, 30.01, 1e300 })
            Assert.InRange(new NoticeQueue(v).Seconds, 3.0, 30.0);
    }

    private static readonly string[] NastyNames =
    [
        new string('a', 1_000_000), "C:\\work\\" + new string('b', 1_000_000), "a\0b", "a\r\nb", "\u202Eevil", "evil\u202E", "a\u2066b\u2069", "\u200Fx\u200E", "\u2028\u2029", "\uD83D", "\uDE00", "x\uD83Dy",
        "C:\\a/b\\c//", "C:\\a\\b\\\\", "C:/a/b/", "////", "\\\\server\\share\\", "proj...", "proj. . .", "   ", "\t", "a\tb", "..", ".", "C:\\", "", null!,
        string.Concat(Enumerable.Repeat("😀", 100)), "ééééééééééééééééééééééééééééééééééééééééé", "e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301",
    ];

    [Fact]
    public void Holds_Project_Name_Is_Bounded_Printable_And_Never_Throws()
    {
        foreach (var raw in NastyNames)
        {
            var name = ProjectName.From(raw);
            Assert.True(name.Length <= ProjectName.MaxChars, $"length {name.Length}");
            Assert.True(name.All(c => !char.IsControl(c)), "control character");
            Assert.True(name.All(c => c is not (>= '\u202A' and <= '\u202E') and not (>= '\u2066' and <= '\u2069') and not '\u200E' and not '\u200F' and not '\u061C'), "direction character");
            Assert.DoesNotContain(name, c => char.IsSurrogate(c) && !(char.IsHighSurrogate(c) || char.IsLowSurrogate(c)));
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsHighSurrogate(name[i])) Assert.True(i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]), "lone high surrogate");
                if (char.IsLowSurrogate(name[i])) Assert.True(i > 0 && char.IsHighSurrogate(name[i - 1]), "lone low surrogate");
            }

            Assert.DoesNotContain('\\', name);
            Assert.DoesNotContain('/', name);
            Assert.Equal(name.TrimEnd(), name);
            var notice = AgentNotice.From(new AgentMessage("Stop", "", raw ?? "", [5]));
            Assert.NotNull(notice);
            Assert.True(notice!.ProjectName.Length is > 0 and <= ProjectName.MaxChars);
        }
    }

    [Theory]
    [InlineData("C:\\work\\\u200B\u200B\u200B")]
    [InlineData("C:\\work\\\u2060\u2060")]
    [InlineData("C:\\work\\\uFEFF")]
    [InlineData("C:\\work\\\u00AD\u00AD")]
    [InlineData("C:\\work\\\u2800")]
    public void Defect_A_Project_Name_Of_Only_Invisible_Characters_Is_Drawn_As_A_Blank_Instead_Of_Falling_Back_To_Agent(string folder)
    {
        var notice = AgentNotice.From(new AgentMessage("Stop", "", folder, [5]))!;
        Assert.Equal(ProjectName.Unknown, notice.ProjectName);
    }

    [Fact]
    public void Holds_Notice_Width_Is_Finite_And_Bounded_For_Any_Text_Width()
    {
        foreach (var w in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -5, 0, 1e300, double.MaxValue, 100 })
            Assert.InRange(NoticeLayout.Width(w), NoticeLayout.Width(NoticeLayout.TextMinWidth), NoticeLayout.WidestWidth);
        foreach (var w in new[] { double.NaN, double.PositiveInfinity, -5, 1e300 })
            Assert.InRange(PillLayout.Width(w), PillLayout.Width(PillLayout.TitleMinWidth), PillLayout.WidestWidth);
    }

    [Fact]
    public void Holds_The_Wire_Round_Trips_And_Always_Fits_The_Limit_Even_For_Wide_Characters()
    {
        foreach (var raw in NastyNames.Concat([new string('é', 400), string.Concat(Enumerable.Repeat("😀", 400)), new string('\u4E2D', 500)]))
        {
            var bytes = AgentWire.Encode("Notification", "permission_prompt", raw, Enumerable.Range(1, 40).ToList());
            Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes, $"{bytes.Length} bytes");
            var message = AgentWire.TryParse(bytes);
            Assert.NotNull(message);
            Assert.True(message!.Folder.Length <= AgentPipe.MaxFolderChars);
            Assert.Equal(AgentPipe.MaxChain, message.Chain.Count);
            if (raw is null || raw.Length <= AgentPipe.MaxFolderChars) Assert.Equal(ProjectName.From(raw), ProjectName.From(message.Folder));
        }
    }

    [Fact]
    public void Holds_The_Wire_Parser_Never_Throws_On_Garbage()
    {
        var r = new Random(5);
        for (var i = 0; i < 20_000; i++)
        {
            var buf = new byte[r.Next(0, 300)];
            r.NextBytes(buf);
            _ = AgentWire.TryParse(buf);
        }

        foreach (var s in new[] { "{}", "[]", "null", "{\"v\":1}", "{\"v\":1,\"e\":1,\"k\":\"\",\"f\":\"\",\"c\":[]}", "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"\",\"c\":[1],\"c\":[2]}", "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"\",\"c\":[-1]}", "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"\\ud800\",\"c\":[1]}", "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1.5]}" })
            _ = AgentWire.TryParse(s);
        Assert.NotNull(AgentWire.TryParse("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}"));
    }

    [Fact]
    public void Holds_Terminal_Choice_Picks_The_Nearest_Program_With_A_Window_And_Handles_Huge_Lists()
    {
        var windows = Enumerable.Range(0, 20_000).Select(i => new WindowFact(i, i % 50, i % 3 == 0 ? "island - term" : "x", i)).ToList();
        var choice = TerminalChoice.Choose([999, 7, 3], windows, "island");
        Assert.Equal(7, choice!.OwnerProcessId);
        Assert.Null(TerminalChoice.Choose([], windows, "island"));
        Assert.Null(TerminalChoice.Choose([999], windows, "island"));
        Assert.NotNull(TerminalChoice.Choose([3], windows, "")); // an empty project name never matches everything
        Assert.NotNull(TerminalChoice.Choose([3], windows, new string('x', 100_000)));
    }

    [Fact]
    public void Holds_Process_Chain_Stops_At_Loops_And_The_Limit()
    {
        var loop = new Dictionary<int, int> { [1] = 2, [2] = 3, [3] = 1 };
        Assert.Equal([2, 3], ProcessChain.Build(1, loop));
        var long_ = Enumerable.Range(1, 5000).ToDictionary(i => i, i => i + 1);
        Assert.Equal(AgentPipe.MaxChain, ProcessChain.Build(1, long_).Count);
    }
}
