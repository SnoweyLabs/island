using Island.Core;

namespace Island.Attack7A.Tests;

/// <summary>The island machine: play, pause, main key, search, notice, page key, in every order and on the same step.</summary>
public class MachineAttackTests
{
    private const double Huge = 1e6;

    private static string Why(IslandMachine m) =>
        $"phase={m.Phase} pill={m.ShowsPill} notice={m.ShowsNotice} search={m.SearchOpen} kb={m.HasKeyboard} contents={m.ContentsVisible} row2={m.SecondRowOpen} " +
        $"wTarget={m.Width.Target:0.##} hTarget={m.Height.Target:0.##} deadline={m.IdleDeadlineMs} wanted={m.PillWanted} allowed={m.PillAllowed}";

    private static void CheckInvariants(IslandMachine m, double now, string where)
    {
        var w = $"{where}: {Why(m)}";
        Assert.True(Enum.IsDefined(m.Phase), w);
        foreach (var v in new[] { m.DrawnWidth, m.DrawnHeight, m.DrawnRadius, m.DrawnY, m.Progress, m.StretchX, m.StretchY, m.Width.Target, m.Height.Target, m.CapsuleTargetWidth })
            Assert.True(double.IsFinite(v), "not finite: " + w);
        Assert.True(!m.ShowsNotice || m.ShowsPill, "notice is not a pill: " + w);
        Assert.True(!m.SearchOpen || !m.ShowsPill, "search and pill together: " + w);
        var shown = m.Phase is IslandPhase.FlyingIn or IslandPhase.Open;
        Assert.True(!m.HasKeyboard || (shown && !m.ShowsPill), "keyboard when it should not be: " + w);
        if (m.Phase == IslandPhase.Hidden)
        {
            Assert.True(!m.ShowsPill && !m.ShowsNotice && !m.SearchOpen && !m.HasKeyboard, "hidden but holds a shape: " + w);
            Assert.True(double.IsPositiveInfinity(m.IdleDeadlineMs), "hidden with a deadline: " + w);
        }

        if (shown && !m.ShowsPill) Assert.True(double.IsFinite(m.IdleDeadlineMs) && m.IdleDeadlineMs >= now, "capsule without an idle end: " + w);
        if (shown && m.ShowsNotice) Assert.True(double.IsPositiveInfinity(m.IdleDeadlineMs), "notice on the idle clock: " + w);
        if (shown && m.ShowsPill && !m.ShowsNotice)
            Assert.True((m.PillWanted && m.PillAllowed) == double.IsPositiveInfinity(m.IdleDeadlineMs), "pill's idle clock disagrees with wanted/allowed: " + w);
    }

    /// <summary>Run with nothing happening (idle time huge): the shape must reach a rest that matches what the flags say.</summary>
    private static void AssertSettles(Clock c, string where)
    {
        var m = c.M;
        c.Run(4000);
        CheckInvariants(m, c.Now, where + " (settled)");
        var w = $"{where}: {Why(m)}";
        Assert.True(m.Phase is IslandPhase.Hidden or IslandPhase.Open, "stuck phase: " + w);
        if (m.Phase != IslandPhase.Open) return;
        Assert.True(m.ContentsVisible, "contents never came in: " + w);
        Assert.True(m.IsAtRest, "springs not at rest: " + w);
        var size = CapsuleLayout.SizeFor(m.Contents);
        var width = m.ShowsPill ? (m.ShowsNotice ? m.NoticeWidth : m.PillWidth) : m.SearchOpen ? m.SearchWidth : size.Width;
        var height = m.ShowsNotice ? NoticeLayout.Height : m.ShowsPill ? PillLayout.Height : m.SecondRowOpen ? ChoiceConstants.TwoRowHeight : size.Height;
        Assert.True(Math.Abs(m.Width.Target - width) < 1e-6, $"width target {m.Width.Target} but the flags say {width}: " + w);
        Assert.True(Math.Abs(m.Height.Target - height) < 1e-6, $"height target {m.Height.Target} but the flags say {height}: " + w);
    }

    private static readonly string[] OtherPages = [PageIds.Folders, PageIds.Apps, PageIds.Vibe, PageIds.Browser];

    private static void Apply(Random r, Clock c, int op)
    {
        var m = c.M;
        var t = c.Now;
        switch (op)
        {
            case 0: m.ShowHideKey(t); break;
            case 1: m.MainKey(t); break;
            case 2: m.SetPill(r.Next(2) == 0, r.Next(3) != 0, t); break;
            case 3: m.SetNotice(r.Next(2) == 0, t); break;
            case 4: m.SetNoticeWidth(r.Next(0, 400), t); break;
            case 5:
                if (m.ShowsNotice) break; // SetPillWidth while the notice shows is its own Defect_ test
                m.SetPillWidth(r.Next(0, 300), t);
                break;
            case 6:
                if (m.SearchOpen) break; // OpenSearch again while search is open is its own Defect_ test
                m.OpenSearch(SearchLayout.Width(r.Next(0, 30), r.Next(0, 12)), t, r.Next(2) == 0);
                break;
            case 7: m.SetSearchWidth(SearchLayout.Width(r.Next(0, 30), r.Next(0, 12)), t); break;
            case 8: m.CloseSearch(t); break;
            case 9: m.TakeKeyboard(t); break;
            case 10:
                var id = r.Next(6) == 0 ? "ghost" : r.Next(2) == 0 ? OtherPages[r.Next(OtherPages.Length)] : m.PageId;
                if (m.SearchOpen && id == m.PageId) id = OtherPages[r.Next(OtherPages.Length)]; // the same page while search is open is its own Defect_ test
                m.PageKey(id, t);
                break;
            case 11:
                var digit = r.Next(0, 11);
                if (m.SearchOpen && m.HasKeyboard && digit >= 1 && digit <= m.DigitKeyCount && m.SearchOpen && Pages.BuiltIn[digit - 1].Id == m.PageId) break; // the same Defect_
                m.DigitKey(digit, t);
                break;
            case 12: m.EscapeKey(t); break;
            case 13: m.ToggleSecondRow(t); break;
            case 14: m.CloseSecondRow(t); break;
            case 15: m.OtherKey(t); break;
            case 16: m.FocusLost(t); break;
            case 17: m.Activity(t); break;
            case 18: m.ItemClick(r.Next(-2, 12), t); break;
            case 19: m.ContentsChanged(t); break;
            case 20: m.Tick(t); break;
            case 21: m.SetPages(r.Next(2) == 0 ? Pages.BuiltIn : [.. Pages.BuiltIn.Take(r.Next(1, 5))], t); break;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Holds_Random_Storm_Of_Every_Input_Keeps_The_Invariants_And_Settles(int seed)
    {
        var r = new Random(seed);
        var c = new Clock(new IslandMachine(Huge));
        for (var step = 0; step < 5000; step++)
        {
            var dtChoice = r.Next(10);
            c.Run(dtChoice < 4 ? 0 : dtChoice < 7 ? Clock.Frame : dtChoice < 9 ? 140 : 900); // 40% of the inputs arrive on the very same step
            Apply(r, c, r.Next(22));
            CheckInvariants(c.M, c.Now, $"seed {seed} step {step}");
            if (step % 150 == 149) AssertSettles(c, $"seed {seed} step {step}");
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void Holds_Storm_With_A_Short_Idle_Time_Always_Ends_Hidden_When_Left_Alone(int seed)
    {
        var r = new Random(seed);
        var c = new Clock(new IslandMachine(3));
        for (var step = 0; step < 4000; step++)
        {
            c.Run(r.Next(3) == 0 ? 0 : r.Next(1, 700));
            Apply(r, c, r.Next(22));
            CheckInvariants(c.M, c.Now, $"seed {seed} step {step}");
        }

        // Nothing wanted, nothing allowed, no notice: whatever the shape, it must leave by itself.
        c.M.SetNotice(false, c.Now);
        c.M.SetPill(false, false, c.Now);
        c.Run(20_000);
        Assert.True(c.M.Phase == IslandPhase.Hidden, "did not leave by itself: " + Why(c.M));
    }

    public enum Start { Hidden, PillUp, CapsuleUp, NoticeUp, SearchUp }

    private static Clock From(Start start)
    {
        var c = new Clock(new IslandMachine(Huge));
        switch (start)
        {
            case Start.PillUp: c.M.SetPill(true, true, c.Now); break;
            case Start.CapsuleUp: c.M.MainKey(c.Now); break;
            case Start.NoticeUp: c.M.SetNotice(true, c.Now); break;
            case Start.SearchUp: c.M.MainKey(c.Now); c.Run(3000); c.M.OpenSearch(500, c.Now); break;
        }

        c.Run(3000);
        return c;
    }

    private static readonly (string Name, Action<IslandMachine, double> Do)[] Same =
    [
        ("main", (m, t) => m.MainKey(t)),
        ("play", (m, t) => m.SetPill(true, true, t)),
        ("pause", (m, t) => m.SetPill(false, true, t)),
        ("notice", (m, t) => m.SetNotice(true, t)),
        ("noticeGone", (m, t) => m.SetNotice(false, t)),
        ("pageKey", (m, t) => m.PageKey(PageIds.Apps, t)),
        ("search", (m, t) => m.OpenSearch(500, t, true)),
        ("tray", (m, t) => m.ShowHideKey(t)),
    ];

    private static List<int[]> Permutations(int n, int pick)
    {
        var result = new List<int[]>();
        void Go(List<int> cur)
        {
            if (cur.Count == pick) { result.Add([.. cur]); return; }
            for (var i = 0; i < n; i++)
                if (!cur.Contains(i)) { cur.Add(i); Go(cur); cur.RemoveAt(cur.Count - 1); }
        }

        Go([]);
        return result;
    }

    [Theory]
    [InlineData(Start.Hidden)]
    [InlineData(Start.PillUp)]
    [InlineData(Start.CapsuleUp)]
    [InlineData(Start.NoticeUp)]
    [InlineData(Start.SearchUp)]
    public void Holds_Every_Order_Of_Five_Inputs_On_The_Same_Step_Gives_A_Legal_Settled_Shape(Start start)
    {
        foreach (var perm in Permutations(Same.Length, 5))
        {
            var c = From(start);
            var label = $"{start}: " + string.Join(",", perm.Select(i => Same[i].Name));
            foreach (var i in perm)
            {
                Same[i].Do(c.M, c.Now);
                CheckInvariants(c.M, c.Now, label);
            }

            AssertSettles(c, label);
        }
    }

    [Fact]
    public void Defect_A_Page_Key_For_The_Page_Already_Shown_Ends_Search_But_Leaves_The_Capsule_At_Search_Width()
    {
        var c = new Clock(new IslandMachine(Huge));
        c.M.MainKey(c.Now);
        c.Run(3000);
        c.M.OpenSearch(700, c.Now);
        c.Run(3000);
        Assert.True(c.M.SearchOpen);
        Assert.Equal(700, c.M.Width.Target);

        c.M.DigitKey(1, c.Now); // page 1 is the page that is showing: an empty search field, the digit 1 (SearchKeys says SwitchPage)
        c.Run(3000);

        Assert.False(c.M.SearchOpen);
        // The page row is laid out again at its own width.
        Assert.Equal(CapsuleLayout.SizeFor(c.M.Contents).Width, c.M.Width.Target);
    }

    [Fact]
    public void Holds_A_Page_Key_For_Another_Page_While_Search_Is_Open_Returns_To_The_Page_Width()
    {
        var c = new Clock(new IslandMachine(Huge));
        c.M.MainKey(c.Now);
        c.Run(3000);
        c.M.OpenSearch(700, c.Now);
        c.Run(3000);
        c.M.PageKey(PageIds.Apps, c.Now);
        c.Run(3000);
        Assert.False(c.M.SearchOpen);
        Assert.Equal(CapsuleLayout.SizeFor(c.M.Contents).Width, c.M.Width.Target);
    }

    [Fact]
    public void Defect_A_New_Pill_Title_Width_While_The_Notice_Shows_Resizes_The_Notice_To_The_Pill()
    {
        var c = From(Start.NoticeUp);
        Assert.True(c.M.ShowsNotice);
        Assert.Equal(c.M.NoticeWidth, c.M.Width.Target);
        c.M.SetPillWidth(150, c.Now); // the track changed while the notice is up: the pill is not what shows
        c.Run(3000);
        Assert.True(c.M.ShowsNotice);
        Assert.Equal(c.M.NoticeWidth, c.M.Width.Target);
    }

    [Fact]
    public void Defect_Asking_To_Open_Search_While_It_Is_Open_Records_The_Width_But_The_Capsule_Does_Not_Follow()
    {
        var c = From(Start.SearchUp);
        Assert.Equal(500, c.M.Width.Target);
        c.M.OpenSearch(800, c.Now);
        c.Run(3000);
        Assert.True(c.M.SearchOpen);
        Assert.Equal(c.M.SearchWidth, c.M.Width.Target); // SearchWidth says 800, the capsule stays at 500 until some later swap jumps it
    }

    [Fact]
    public void Defect_The_Search_Width_Has_No_Bound_In_The_Machine()
    {
        var c = new Clock(new IslandMachine(Huge));
        c.M.MainKey(c.Now);
        c.Run(3000);
        c.M.OpenSearch(500, c.Now);
        c.Run(3000);
        c.M.SetSearchWidth(1e9, c.Now); // SearchLayout.Width never gives more than ~1100; the pill and the notice are clamped by their layouts, search is not
        c.Run(3000);
        Assert.True(c.M.DrawnWidth < 5000, $"capsule is {c.M.DrawnWidth:0} wide");
    }

    [Fact]
    public void Holds_Notice_Over_Pill_Returns_The_Pill_And_Never_The_Keyboard()
    {
        var c = From(Start.PillUp);
        c.M.SetNotice(true, c.Now);
        Assert.False(c.M.HasKeyboard);
        c.Run(2000);
        Assert.True(c.M.ShowsNotice);
        c.M.SetNotice(false, c.Now);
        c.Run(2000);
        Assert.True(c.M.ShowsPill && !c.M.ShowsNotice && c.M.Phase == IslandPhase.Open);
        Assert.False(c.M.HasKeyboard);
    }

    [Fact]
    public void Holds_Non_Finite_Times_And_Widths_Change_Nothing()
    {
        var c = From(Start.CapsuleUp);
        var before = Why(c.M);
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            c.M.SetPill(true, true, bad); c.M.SetNotice(true, bad); c.M.OpenSearch(500, bad); c.M.SetSearchWidth(500, bad);
            c.M.SetSearchWidth(bad, c.Now); c.M.SetNoticeWidth(bad, c.Now); c.M.SetPillWidth(bad, c.Now); c.M.CloseSearch(bad); c.M.TakeKeyboard(bad);
            c.M.MainKey(bad); c.M.ShowHideKey(bad); c.M.PageKey(PageIds.Apps, bad); c.M.SetIdleSeconds(5, bad); c.M.SetIdleSeconds(bad, c.Now); c.M.Tick(bad);
        }

        Assert.Equal(before, Why(c.M));
    }

    [Fact]
    public void Holds_A_Machine_Time_Far_Beyond_Any_Uptime_Still_Behaves()
    {
        var m = new IslandMachine(5);
        var now = 1e12;
        m.Tick(now);
        m.MainKey(now);
        for (var i = 0; i < 200; i++) m.Tick(now += Clock.Frame);
        CheckInvariants(m, now, "1e12");
        Assert.Equal(IslandPhase.Open, m.Phase);
    }
}
