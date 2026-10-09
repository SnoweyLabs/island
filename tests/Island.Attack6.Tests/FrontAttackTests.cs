using Island.Core;

namespace Island.Attack6.Tests;

/// <summary>WO6 section 2 (and the full table of WO7 section 1): ShowDecision, NeverOverList, FrontClassifier, NoticeFallback.</summary>
public class FrontAttackTests
{
    private static readonly FrontState[] States = Enum.GetValues<FrontState>();
    private static readonly Appearer[] Things = Enum.GetValues<Appearer>();
    private static readonly ShowOrigin[] Origins = Enum.GetValues<ShowOrigin>();
    private static readonly Mode[] Modes = Enum.GetValues<Mode>();

    /// <summary>The WORK-ORDER-7 table, written out again from the document, not from the code.</summary>
    private static ShowAnswer Oracle(FrontState front, Appearer thing, ShowOrigin origin, Mode mode)
    {
        var asked = origin == ShowOrigin.Asked;
        bool show = front switch
        {
            FrontState.ExclusiveFullscreen => false,
            FrontState.Clear => mode switch { Mode.DND => asked, _ => true },
            FrontState.FullscreenProgram => mode switch
            {
                Mode.Focus => asked || thing == Appearer.Notice,
                Mode.Vibe => asked,
                _ => false,
            },
            FrontState.Presentation => mode switch { Mode.DND => false, _ => asked },
            _ => false,
        };
        return show ? ShowAnswer.Show : ShowAnswer.StayAway;
    }

    [Fact]
    public void Holds_Every_Cell_Of_The_Table_Matches_The_Document_For_Every_State_Thing_Origin_And_Mode()
    {
        var cells = 0;
        foreach (var f in States)
            foreach (var t in Things)
                foreach (var o in Origins)
                    foreach (var m in Modes)
                    {
                        Assert.Equal(Oracle(f, t, o, m), ShowDecision.Decide(f, t, o, m));
                        cells++;
                    }

        Assert.Equal(4 * 3 * 2 * 3, cells);
    }

    [Fact]
    public void Holds_The_Island_Never_Covers_Exclusive_Fullscreen_In_Any_Mode_Origin_Or_Thing()
    {
        foreach (var t in Things)
            foreach (var o in Origins)
                foreach (var m in Modes.Concat([(Mode)(-1), (Mode)99, (Mode)int.MaxValue, (Mode)int.MinValue]))
                    Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(FrontState.ExclusiveFullscreen, t, o, m));
    }

    [Fact]
    public void Holds_Any_Value_Outside_The_Enums_Answers_StayAway_Never_Throws()
    {
        int[] odd = [-1, 4, 7, 99, int.MaxValue, int.MinValue];
        foreach (var bad in odd)
            foreach (var f in States)
                foreach (var t in Things)
                    foreach (var o in Origins)
                        foreach (var m in Modes)
                        {
                            Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide((FrontState)bad, t, o, m));
                            Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(f, (Appearer)bad, o, m));
                            Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(f, t, (ShowOrigin)bad, m));
                            Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(f, t, o, (Mode)bad));
                        }

        Assert.Equal(ShowAnswer.StayAway, default);
    }

    [Fact]
    public void Holds_Never_Over_List_Counts_A_Listed_Fullscreen_Program_As_Exclusive_And_Nothing_Else()
    {
        var list = NeverOverList.Empty.With(new NeverOverEntry("Alpha", "alpha.exe"));
        Assert.Equal(FrontState.ExclusiveFullscreen, list.Apply(FrontState.FullscreenProgram, "ALPHA.EXE"));
        Assert.Equal(FrontState.Clear, list.Apply(FrontState.Clear, "alpha.exe"));
        Assert.Equal(FrontState.Presentation, list.Apply(FrontState.Presentation, "alpha.exe"));
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, "beta.exe"));
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, null));
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, ""));
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, "   "));
        Assert.Equal((FrontState)77, list.Apply((FrontState)77, "alpha.exe"));
        Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(list.Apply(FrontState.FullscreenProgram, "alpha.exe"), Appearer.Island, ShowOrigin.Asked, Mode.Vibe));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a\\b.exe")]
    [InlineData("a/b.exe")]
    [InlineData("C:\\a.exe")]
    [InlineData("a:b.exe")]
    [InlineData("a*.exe")]
    [InlineData("a?.exe")]
    [InlineData("a\"b.exe")]
    [InlineData("a<b.exe")]
    [InlineData("a|b.exe")]
    [InlineData("a\0b.exe")]
    [InlineData("a\nb.exe")]
    public void Holds_An_Entry_That_Is_Not_A_File_Name_Is_Refused(string exe)
    {
        var list = NeverOverList.Empty.With(new NeverOverEntry("Alpha", exe));
        Assert.Empty(list.Entries);
        Assert.False(list.Contains(exe));
    }

    [Fact]
    public void Holds_Null_And_Blank_Names_And_Null_Entries_Are_Refused_Without_Throwing()
    {
        Assert.Empty(NeverOverList.From([null, new NeverOverEntry("", "a.exe"), new NeverOverEntry("  ", "a.exe"), new NeverOverEntry(null!, "a.exe"), new NeverOverEntry("A", null!)]).Entries);
        Assert.False(NeverOverList.IsValid(null));
        Assert.Empty(NeverOverList.Empty.Without(null!).Entries);
        Assert.False(NeverOverList.Empty.Contains(null));
    }

    [Fact]
    public void Holds_Repeats_In_Another_Case_Are_One_Entry_And_Without_Ignores_Case()
    {
        var list = NeverOverList.From([new("A", "alpha.exe"), new("B", "ALPHA.EXE"), new("C", "beta.exe")]);
        Assert.Equal(2, list.Entries.Count);
        Assert.Equal("A", list.Entries[0].Name);
        Assert.Single(list.Without("Alpha.Exe").Entries);
        Assert.Equal(2, list.Entries.Count); // immutable
    }

    [Fact]
    public void Holds_The_Entries_Of_A_List_Cannot_Be_Changed_From_Outside()
    {
        var list = NeverOverList.Empty.With(new NeverOverEntry("A", "alpha.exe"));
        if (list.Entries is System.Collections.IList asList) Assert.Throws<NotSupportedException>(() => asList.Add(new NeverOverEntry("X", "x.exe")));
        Assert.Single(list.Entries);
        if (NeverOverList.Empty.Entries is System.Collections.IList empty) Assert.Throws<NotSupportedException>(() => empty.Add(new NeverOverEntry("X", "x.exe")));
        Assert.Empty(NeverOverList.Empty.Entries);
    }

    [Fact]
    public void Holds_A_Huge_Or_Odd_Name_Is_Kept_Whole_And_Matching_Works_On_Unicode_Case()
    {
        var name = new string('x', 1_000_000);
        var list = NeverOverList.Empty.With(new NeverOverEntry(name, "ünï.exe"));
        Assert.Single(list.Entries);
        Assert.True(list.Contains("ÜNÏ.EXE"));
        Assert.False(list.Contains("uni.exe"));
    }

    // ---- an entry that can never match ------------------------------------------------------------------------

    [Fact]
    public void Defect_An_Entry_With_Padding_Spaces_Is_Accepted_And_Can_Never_Match_The_Real_File_Name()
    {
        // Windows file names cannot be told apart by a leading or trailing space in a process image name: the front reader
        // hands "alpha.exe". An entry " alpha.exe" is accepted by IsValid and then never matches.
        var list = NeverOverList.Empty.With(new NeverOverEntry("Alpha", " alpha.exe "));
        var accepted = list.Entries.Count == 1;
        var matches = list.Contains("alpha.exe");
        Assert.False(accepted && !matches, "the entry was accepted but can never be matched by the real file name");
    }

    [Fact]
    public void Defect_An_Entry_Without_Exe_Extension_Is_Accepted_And_Can_Never_Match()
    {
        // A pick must end in .exe (Pick.IsStorable); the never-over list says "stored like a pick" but accepts "alpha".
        var list = NeverOverList.Empty.With(new NeverOverEntry("Alpha", "alpha"));
        Assert.False(list.Entries.Count == 1 && !list.Contains("alpha.exe"), "the entry was accepted but no running program is called that");
    }

    // ---- the classifier -----------------------------------------------------------------------------------------

    private static readonly FrontRect Screen = new(0, 0, 1920, 1080);

    private static FrontWindowFacts Facts(FrontRect? window = null, FrontRect? screen = null, bool title = false, bool border = false,
        string? cls = "AlphaWindow", bool island = false, bool shell = false, bool tool = false) =>
        new(window ?? Screen, screen ?? Screen, title, border, cls, island, shell, tool);

    [Fact]
    public void Holds_A_Borderless_Window_Equal_To_Its_Screen_Is_A_Fullscreen_Program()
    {
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts()));
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(cls: null)));
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(cls: "")));
    }

    [Fact]
    public void Holds_Every_Single_Reason_To_Say_Clear_Says_Clear()
    {
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(null));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(title: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(border: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(island: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(shell: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(tool: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new FrontRect(0, 0, 1919, 1080))));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new FrontRect(-1, 0, 1920, 1080))));
        foreach (var c in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SysListView32", "Flip3D", "progman", "SHELL_TRAYWND" })
            Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(cls: c)));
    }

    [Fact]
    public void Holds_Empty_And_Inverted_Rectangles_Are_Never_Fullscreen_Even_When_Equal()
    {
        foreach (var r in new[] { new FrontRect(0, 0, 0, 0), new FrontRect(5, 5, 5, 5), new FrontRect(10, 10, 0, 0), new FrontRect(0, 0, 10, 0), new FrontRect(0, 0, 0, 10) })
            Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: r, screen: r)));
    }

    [Fact]
    public void Holds_Negative_And_Extreme_Rectangles_Equal_To_Their_Screen_Are_Fullscreen_Without_Overflow()
    {
        var left = new FrontRect(-3840, -200, 0, 1960);
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(window: left, screen: left)));
        var huge = new FrontRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(window: huge, screen: huge)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: Screen, screen: left)));
    }

    [Fact]
    public void Holds_Odd_Class_Names_Never_Throw_And_Only_The_Listed_Ones_Are_Skipped()
    {
        foreach (var cls in new[] { new string('W', 1_000_000), "\0", "Progman\0", " Progman", "Progman ", "ProgmanX", "\uD800", "Ünï" })
            Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(cls: cls)));
    }

    [Fact]
    public void Holds_Windows_Hard_States_Win_And_Everything_Else_Falls_To_The_Rectangle_Test()
    {
        Assert.Equal(FrontState.ExclusiveFullscreen, FrontClassifier.Combine(3, null));
        Assert.Equal(FrontState.ExclusiveFullscreen, FrontClassifier.Combine(3, Facts(title: true)));
        Assert.Equal(FrontState.ExclusiveFullscreen, FrontClassifier.Combine(3, Facts(island: true)));
        Assert.Equal(FrontState.Presentation, FrontClassifier.Combine(4, null));
        foreach (var v in new[] { 0, 1, 2, 5, 6, 7, -1, 99, int.MaxValue, int.MinValue })
        {
            Assert.Null(FrontClassifier.FromNotificationState(v));
            Assert.Equal(FrontState.Clear, FrontClassifier.Combine(v, null));
            Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Combine(v, Facts()));
        }
    }

    [Fact]
    public void Holds_A_Failed_Reading_Is_Clear_As_The_Work_Order_Says()
    {
        // WORK-ORDER-6 section 2: "anything else, and any failure to read: Clear". Both inputs missing gives Clear, and Clear shows.
        var state = FrontClassifier.Combine(null, null);
        Assert.Equal(FrontState.Clear, state);
        Assert.Equal(ShowAnswer.Show, ShowDecision.Decide(state, Appearer.Island, ShowOrigin.Asked, Mode.Vibe));
    }

    [Fact]
    public void Holds_A_Combined_Reading_Is_Never_Outside_The_Enum()
    {
        foreach (int? n in new int?[] { null, 0, 3, 4, -7 })
            foreach (var f in new[] { null, Facts(), Facts(title: true) })
                Assert.True(Enum.IsDefined(FrontClassifier.Combine(n, f)));
    }

    // ---- the notice fallback --------------------------------------------------------------------------------------

    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static NoticeStep Next(NoticeWait w, Mode mode, FrontState front, bool second, DateTimeOffset now) =>
        NoticeFallback.Next(w, new NoticeFacts(mode, front, second, now));

    [Fact]
    public void Holds_Nothing_Waiting_Means_No_Action_No_Timer_Whatever_The_Facts()
    {
        foreach (var m in Modes.Concat([(Mode)42]))
            foreach (var f in States.Concat([(FrontState)42]))
            {
                var step = Next(NoticeWait.Idle, m, f, true, T0);
                Assert.Equal(NoticeAction.None, step.Action);
                Assert.False(step.NeedsTimer);
            }

        Assert.Equal(NoticeAction.None, NoticeFallback.Next(new NoticeWait((NoticePhase)9, T0, false), new NoticeFacts(Mode.Focus, FrontState.Clear, false, T0)).Action);
    }

    [Fact]
    public void Holds_An_Unknown_Mode_Drops_The_Notice_With_No_Sound()
    {
        foreach (var m in new[] { (Mode)(-1), (Mode)3, (Mode)int.MaxValue })
        {
            var step = Next(NoticeWait.Arrive(T0), m, FrontState.FullscreenProgram, false, T0);
            Assert.Equal(NoticeAction.Drop, step.Action);
            Assert.False(step.NeedsTimer);
        }
    }

    [Fact]
    public void Holds_The_Sound_Plays_At_Most_Once_And_Only_In_Focus_Over_A_Hard_State_Without_A_Second_Screen()
    {
        foreach (var m in Modes)
            foreach (var f in new[] { FrontState.ExclusiveFullscreen, FrontState.Presentation })
            {
                var w = NoticeWait.Arrive(T0);
                var sounds = 0;
                for (var i = 0; i < 100; i++)
                {
                    var step = Next(w, m, f, false, T0.AddSeconds(i));
                    if (step.Action == NoticeAction.PlaySound) sounds++;
                    if (!step.NeedsTimer) break;
                    w = step.State;
                }

                Assert.Equal(m == Mode.Focus ? 1 : 0, sounds);
            }
    }

    [Fact]
    public void Holds_Stale_Is_Exactly_Ten_Minutes_And_A_Clock_That_Went_Back_Is_Never_Stale()
    {
        var w = NoticeWait.Arrive(T0);
        Assert.Equal(NoticeAction.Wait, Next(w, Mode.Vibe, FrontState.Presentation, false, T0 + NoticeFallback.StaleAfter - TimeSpan.FromTicks(1)).Action);
        Assert.Equal(NoticeAction.Drop, Next(w, Mode.Vibe, FrontState.Presentation, false, T0 + NoticeFallback.StaleAfter).Action);
        Assert.Equal(NoticeAction.Wait, Next(w, Mode.Vibe, FrontState.Presentation, false, T0.AddDays(-400)).Action);
        Assert.Equal(NoticeAction.Drop, Next(NoticeWait.Arrive(DateTimeOffset.MinValue), Mode.Vibe, FrontState.Presentation, false, DateTimeOffset.MaxValue).Action);
        Assert.Equal(NoticeAction.Wait, Next(NoticeWait.Arrive(DateTimeOffset.MaxValue), Mode.Vibe, FrontState.Presentation, false, DateTimeOffset.MinValue).Action);
    }

    [Fact]
    public void Holds_In_Dnd_A_Notice_That_May_Not_Show_Is_Dropped_Even_With_A_Second_Screen()
    {
        foreach (var f in new[] { FrontState.FullscreenProgram, FrontState.Presentation, FrontState.ExclusiveFullscreen, FrontState.Clear })
            Assert.Equal(NoticeAction.Drop, Next(NoticeWait.Arrive(T0), Mode.DND, f, true, T0).Action);
    }

    [Fact]
    public void Holds_A_Second_Screen_Comes_Before_The_Sound_And_The_Sound_Before_Waiting()
    {
        var w = NoticeWait.Arrive(T0);
        Assert.Equal(NoticeAction.ShowOnOtherScreen, Next(w, Mode.Focus, FrontState.Presentation, true, T0).Action);
        var first = Next(w, Mode.Focus, FrontState.Presentation, false, T0);
        Assert.Equal(NoticeAction.PlaySound, first.Action);
        Assert.Equal(NoticeAction.Wait, Next(first.State, Mode.Focus, FrontState.Presentation, false, T0).Action);
        Assert.Equal(NoticeAction.ShowHere, Next(first.State, Mode.Focus, FrontState.Clear, false, T0).Action);
        Assert.Equal(NoticeAction.ShowHere, Next(w, Mode.Focus, FrontState.FullscreenProgram, false, T0).Action);
    }

    // ---- a hole in the documented rule, found by reading -------------------------------------------------------------

    [Fact]
    public void Defect_An_Unknown_Front_State_Plays_The_Sound_In_Focus()
    {
        // ShowDecision answers StayAway for a value outside FrontState (its documented rule), but the fallback then treats it as "the
        // table says no" and, in Focus, plays the one system sound. Unknown mode is handled ("above all no sound"); unknown front is not.
        var step = Next(NoticeWait.Arrive(T0), Mode.Focus, (FrontState)42, false, T0);
        Assert.NotEqual(NoticeAction.PlaySound, step.Action);
    }
}
