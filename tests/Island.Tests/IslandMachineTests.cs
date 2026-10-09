using Island.Core;

namespace Island.Tests;

/// <summary>Drives a machine with a fake clock at 60 frames per second.</summary>
internal sealed class Clock(IslandMachine machine)
{
    public const double Frame = 1000.0 / 60;
    public double Now { get; private set; }

    public IslandMachine M { get; } = machine;

    public void RunTo(double ms)
    {
        while (Now + Frame <= ms)
        {
            Now += Frame;
            M.Tick(Now);
        }

        Now = ms;
        M.Tick(Now);
    }

    public void Run(double forMs) => RunTo(Now + forMs);
}

public class IslandMachineTests
{
    private static Clock NewClock(double idleSeconds = 60) => new(new IslandMachine(idleSeconds));

    private static Clock OpenClock(string? category = null)
    {
        var c = NewClock();
        if (category is { } id) c.M.PageKey(id, c.Now);
        else c.M.ShowHideKey(c.Now);
        c.Run(2000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        return c;
    }

    [Fact]
    public void ShowKey_From_Hidden_Flies_In_Then_Opens()
    {
        var c = NewClock();
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        Assert.False(c.M.NeedsFrames);

        c.M.ShowHideKey(c.Now);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.Equal(PageIds.Media, c.M.PageId);
        Assert.Equal(-90, c.M.Y.Value);
        Assert.Equal(LookConstants.TopGap, c.M.Y.Target);
        Assert.Equal(LookConstants.BallSize, c.M.Width.Target);

        c.RunTo(319);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.Equal(LookConstants.BallSize, c.M.Width.Target);

        c.RunTo(321);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(466, c.M.Width.Target);
        Assert.Equal(76, c.M.Height.Target);
        Assert.False(c.M.ContentsVisible);

        c.RunTo(409);
        Assert.False(c.M.ContentsVisible);
        c.RunTo(321 + 91);
        Assert.True(c.M.ContentsVisible);
        Assert.Equal(410, c.M.ContentsChangedAtMs, 1e-9);

        c.Run(3000);
        Assert.InRange(c.M.DrawnWidth, 465.9, 466.1);
        Assert.InRange(c.M.Y.Value, 13.99, 14.01);
        Assert.True(c.M.NeedsFrames);
    }

    [Fact]
    public void ShowKey_While_Open_Closes_And_Ends_Hidden()
    {
        var c = OpenClock();
        var t0 = c.Now;
        c.M.ShowHideKey(t0);

        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.False(c.M.ContentsVisible);
        Assert.Equal(466, c.M.Width.Target);

        c.RunTo(t0 + 109);
        Assert.Equal(466, c.M.Width.Target);
        c.RunTo(t0 + 111);
        Assert.Equal(LookConstants.BallSize, c.M.Width.Target);
        Assert.Equal(LookConstants.TopGap, c.M.Y.Target);

        c.RunTo(t0 + 559);
        Assert.Equal(LookConstants.TopGap, c.M.Y.Target);
        c.RunTo(t0 + 561);
        Assert.Equal(-90, c.M.Y.Target);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);

        c.Run(4000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        Assert.False(c.M.NeedsFrames);
    }

    [Fact]
    public void CategoryKey_While_Hidden_Summons_That_Category()
    {
        var c = NewClock();
        c.M.PageKey(PageIds.Browser, c.Now);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.Equal(PageIds.Browser, c.M.PageId);

        c.Run(2000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(502, c.M.Width.Target);
        Assert.Equal(PageIds.Browser, c.M.ContentsPageId);
        Assert.Equal(0, c.M.SelectedItem);
    }

    [Fact]
    public void ShowKey_Summons_In_The_Category_Last_Shown()
    {
        var c = OpenClock(PageIds.Apps);
        c.M.ShowHideKey(c.Now);
        c.Run(5000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);

        c.M.ShowHideKey(c.Now);
        c.Run(2000);
        Assert.Equal(PageIds.Apps, c.M.PageId);
        Assert.Equal(PageIds.Apps, c.M.ContentsPageId);
    }

    [Fact]
    public void CategoryKey_While_Open_Switches_Without_Closing()
    {
        var c = OpenClock(PageIds.Media);
        var t0 = c.Now;
        c.M.ItemClick(2, t0);
        Assert.Equal(2, c.M.SelectedItem);

        c.M.PageKey(PageIds.Folders, t0);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(PageIds.Folders, c.M.PageId);
        Assert.Equal(PageIds.Media, c.M.ContentsPageId);
        Assert.False(c.M.ContentsVisible);
        Assert.Equal(LookConstants.TopGap, c.M.Y.Target);

        c.RunTo(t0 + 119);
        Assert.Equal(PageIds.Media, c.M.ContentsPageId);
        Assert.Equal(466, c.M.Width.Target);

        c.RunTo(t0 + 121);
        Assert.Equal(PageIds.Folders, c.M.ContentsPageId);
        Assert.Equal(0, c.M.SelectedItem);
        Assert.Equal(CapsuleLayout.Width(4, isMedia: false), c.M.Width.Target);
        Assert.False(c.M.ContentsVisible);

        c.RunTo(t0 + 120 + 89);
        Assert.False(c.M.ContentsVisible);
        c.RunTo(t0 + 120 + 91);
        Assert.True(c.M.ContentsVisible);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
    }

    [Fact]
    public void CategoryKey_During_FlyIn_Restarts_The_Summon_With_The_New_Category()
    {
        var c = NewClock();
        c.M.PageKey(PageIds.Media, c.Now);
        c.RunTo(200);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        var y = c.M.Y.Value;

        c.M.PageKey(PageIds.Vibe, c.Now);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.Equal(y, c.M.Y.Value);

        c.RunTo(200 + 319);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        c.RunTo(200 + 321);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(PageIds.Vibe, c.M.ContentsPageId);
    }

    [Fact]
    public void Same_CategoryKey_While_Open_Only_Resets_Idle()
    {
        var c = OpenClock(PageIds.Apps);
        c.M.ItemClick(3, c.Now);
        c.RunTo(c.Now + 30_000);
        var deadlineBefore = c.M.IdleDeadlineMs;

        c.M.PageKey(PageIds.Apps, c.Now);

        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.ContentsVisible, "Contents must not be replayed.");
        Assert.Equal(3, c.M.SelectedItem);
        Assert.Equal(c.Now + 60_000, c.M.IdleDeadlineMs, 1e-9);
        Assert.True(c.M.IdleDeadlineMs > deadlineBefore);
    }

    [Fact]
    public void Five_Idle_Seconds_Close_It()
    {
        var c = new Clock(new IslandMachine());
        c.M.ShowHideKey(0);
        c.RunTo(4_900);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.RunTo(5_001);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        c.Run(5000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
    }

    // ---- WO2-KEYS: the island takes and releases the keyboard ----------

    [Fact]
    public void MainKey_Summon_Takes_The_Keyboard()
    {
        var c = NewClock();
        Assert.False(c.M.HasKeyboard);
        c.M.MainKey(c.Now);
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.True(c.M.HasKeyboard);
        c.Run(2000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.HasKeyboard);

        // The main key again dismisses, and the keyboard goes with it.
        c.M.MainKey(c.Now);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.False(c.M.HasKeyboard);
    }

    [Fact]
    public void Startup_Summon_Does_Not_Take_The_Keyboard()
    {
        var c = NewClock();
        c.M.ShowHideKey(c.Now); // what start-up and the tray menu call
        c.Run(2000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.False(c.M.HasKeyboard);

        // With no keyboard, digits and Esc do nothing at all.
        var deadline = c.M.IdleDeadlineMs;
        c.M.DigitKey(3, c.Now);
        c.M.EscapeKey(c.Now);
        c.M.OtherKey(c.Now);
        Assert.Equal(PageIds.Media, c.M.PageId);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(deadline, c.M.IdleDeadlineMs);

        // A page key summons without the keyboard as well.
        var d = NewClock();
        d.M.PageKey(PageIds.Apps, d.Now);
        Assert.False(d.M.HasKeyboard);
    }

    [Fact]
    public void Digit_While_Open_Switches_Page()
    {
        var c = NewClock();
        c.M.MainKey(c.Now);
        c.Run(2000);

        c.M.DigitKey(3, c.Now);
        Assert.Equal(PageIds.Apps, c.M.PageId);
        c.Run(2000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(PageIds.Apps, c.M.ContentsPageId);
        Assert.True(c.M.HasKeyboard, "Switching page keeps the keyboard.");

        c.M.DigitKey(1, c.Now);
        c.Run(2000);
        Assert.Equal(PageIds.Media, c.M.ContentsPageId);

        // A digit while the ball is still flying in switches too.
        var d = NewClock();
        d.M.MainKey(d.Now);
        d.Run(100);
        d.M.DigitKey(5, d.Now);
        d.Run(2000);
        Assert.Equal(PageIds.Browser, d.M.ContentsPageId);
        Assert.Equal(IslandPhase.Open, d.M.Phase);
    }

    [Fact]
    public void Digit_Beyond_The_Last_Page_Does_Nothing()
    {
        var c = NewClock();
        c.M.MainKey(c.Now);
        c.Run(2000);

        foreach (var digit in new[] { 0, 7, 9, 10, -1 })
        {
            c.M.DigitKey(digit, c.Now);
            c.Run(500);
            Assert.Equal(PageIds.Media, c.M.PageId);
            Assert.Equal(IslandPhase.Open, c.M.Phase);
            Assert.True(c.M.HasKeyboard);
        }

        // Nine at most, and never more than there are pages.
        Assert.Equal(6, c.M.DigitKeyCount);
        var many = Enumerable.Range(0, 12).Select(i => new Page($"p{i}", $"P{i}", "#112233", "grid", null, false)).ToArray();
        Assert.Equal(9, new IslandMachine(5, many).DigitKeyCount);
        var nine = new Clock(new IslandMachine(5, many));
        nine.M.MainKey(0);
        nine.Run(2000);
        nine.M.DigitKey(9, nine.Now);
        Assert.Equal("p8", nine.M.PageId);
        nine.M.DigitKey(10, nine.Now);
        Assert.Equal("p8", nine.M.PageId);
    }

    [Fact]
    public void Digit_Without_The_Keyboard_Does_Nothing()
    {
        // Hidden: a digit does not even summon.
        var c = NewClock();
        c.M.DigitKey(2, c.Now);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);

        // Open but without the keyboard (focus lost): the digit goes to the other window, not to the island.
        var d = NewClock();
        d.M.MainKey(d.Now);
        d.Run(2000);
        d.M.FocusLost(d.Now);
        var deadline = d.M.IdleDeadlineMs;
        d.M.DigitKey(2, d.Now);
        Assert.Equal(PageIds.Media, d.M.PageId);
        Assert.Equal(deadline, d.M.IdleDeadlineMs);

        // And after a dismissal.
        d.M.MainKey(d.Now);
        d.Run(3000);
        Assert.Equal(IslandPhase.Hidden, d.M.Phase);
        d.M.DigitKey(2, d.Now);
        Assert.Equal(IslandPhase.Hidden, d.M.Phase);
    }

    [Fact]
    public void Escape_Dismisses_And_Releases_The_Keyboard()
    {
        var c = NewClock();
        c.M.MainKey(c.Now);
        c.Run(2000);
        Assert.True(c.M.HasKeyboard);

        c.M.EscapeKey(c.Now);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.False(c.M.HasKeyboard);
        c.Run(3000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        Assert.False(c.M.HasKeyboard);

        // Esc with the keyboard lost leaves the island alone.
        var d = NewClock();
        d.M.MainKey(d.Now);
        d.Run(2000);
        d.M.FocusLost(d.Now);
        d.M.EscapeKey(d.Now);
        Assert.Equal(IslandPhase.Open, d.M.Phase);
    }

    [Fact]
    public void Any_Key_Resets_The_Idle_Clock()
    {
        var c = new Clock(new IslandMachine());
        c.M.MainKey(c.Now);
        c.Run(500);

        // A digit, a digit with no page behind it, and any other key each push the deadline out.
        foreach (var press in new Action<double>[] { t => c.M.DigitKey(2, t), t => c.M.DigitKey(9, t), c.M.OtherKey })
        {
            c.Run(3000);
            var before = c.M.IdleDeadlineMs;
            press(c.Now);
            Assert.Equal(c.Now + 5000, c.M.IdleDeadlineMs, 1e-9);
            Assert.True(c.M.IdleDeadlineMs > before);
            Assert.Equal(IslandPhase.Open, c.M.Phase);
        }

        // Keys kept it open for far longer than five seconds in total; then silence closes it.
        c.Run(5100);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.False(c.M.HasKeyboard);
    }

    [Fact]
    public void Losing_Focus_Releases_The_Keyboard_But_Keeps_It_Open()
    {
        var c = new Clock(new IslandMachine());
        c.M.MainKey(c.Now);
        c.Run(2000);
        var deadline = c.M.IdleDeadlineMs;

        c.M.FocusLost(c.Now);
        Assert.False(c.M.HasKeyboard);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.ContentsVisible);
        Assert.Equal(deadline, c.M.IdleDeadlineMs); // losing focus is not use

        // It still leaves by itself when its time is up, and never had the keyboard again.
        c.RunTo(deadline + 100);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.False(c.M.HasKeyboard);
    }

    [Fact]
    public void Idle_Dismissal_Releases_The_Keyboard()
    {
        var c = new Clock(new IslandMachine());
        c.M.MainKey(c.Now);
        c.RunTo(4_900);
        Assert.True(c.M.HasKeyboard);
        c.RunTo(5_100);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.False(c.M.HasKeyboard);
    }

    [Fact]
    public void ContentsChanged_Lays_The_Open_Capsule_Out_Again_Without_A_Jump()
    {
        var items = new List<Item> { new("A", "x", "Aa", 10), new("B", "x", "Bb", 20) };
        var m = new IslandMachine(30, null, _ => items);
        var c = new Clock(m);
        m.PageKey(PageIds.Apps, c.Now);
        c.Run(2000);
        var before = m.CapsuleTargetWidth;
        Assert.Equal(CapsuleLayout.Width(2, false), before);

        items.Add(new Item("C", "x", "Cc", 30));
        m.ContentsChanged(c.Now);
        Assert.False(m.ContentsVisible, "the contents go out first");
        var widthNow = m.Width.Value;
        c.Run(2000);

        Assert.Equal(CapsuleLayout.Width(3, false), m.CapsuleTargetWidth);
        Assert.True(m.IsAtRest);
        Assert.True(Math.Abs(widthNow - before) < 1, "the width did not jump when the change was announced");
        Assert.Equal(IslandPhase.Open, m.Phase);

        // Hidden: nothing to lay out and nothing starts.
        var hidden = new IslandMachine(30, null, _ => items);
        hidden.ContentsChanged(0);
        Assert.Equal(IslandPhase.Hidden, hidden.Phase);
    }

    [Fact]
    public void SetPages_Adds_A_Page_Reachable_By_Its_Number_And_Survives_A_Removed_Current_Page()
    {
        var mine = new Page("games", "Games", "#12AB34", "grid", null, false);
        var m = new IslandMachine(30);
        var c = new Clock(m);
        m.MainKey(c.Now);
        c.Run(2000);
        Assert.Equal(6, m.DigitKeyCount);

        m.SetPages([.. Pages.BuiltIn, mine], c.Now);
        Assert.Equal(7, m.DigitKeyCount);
        m.DigitKey(7, c.Now);
        c.Run(2000);
        Assert.Equal("games", m.PageId);
        Assert.Equal(IslandPhase.Open, m.Phase);
        Assert.Equal("#12AB34", m.Page.Color);

        // The page being shown is removed: the machine goes to the first page and stays legal.
        m.SetPages(Pages.BuiltIn, c.Now);
        c.Run(2000);
        Assert.Equal(PageIds.Media, m.PageId);
        Assert.Equal(PageIds.Media, m.ContentsPageId);
        Assert.Equal(IslandPhase.Open, m.Phase);
        Assert.True(m.IsAtRest);
    }

    [Fact]
    public void Idle_Time_Comes_From_The_Setting()
    {
        var c = NewClock(idleSeconds: 2);
        c.M.ShowHideKey(0);
        c.RunTo(1_900);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.RunTo(2_100);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
    }

    [Fact]
    public void Activity_Resets_The_Idle_Clock()
    {
        var c = NewClock();
        c.M.ShowHideKey(0);
        c.RunTo(50_000);
        c.M.Activity(50_000);
        c.RunTo(100_000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.RunTo(110_100);
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
    }

    [Fact]
    public void Activity_Does_Nothing_While_Hidden()
    {
        var c = NewClock();
        c.M.Activity(1000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        Assert.True(double.IsPositiveInfinity(c.M.IdleDeadlineMs));
    }

    [Fact]
    public void Summon_During_Closing_Reverses_Without_A_Jump()
    {
        var c = OpenClock();
        c.M.ShowHideKey(c.Now);
        c.Run(700); // past the fly-out command, still on its way out
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        Assert.Equal(-90, c.M.Y.Target);
        var before = c.M.Y;
        Assert.True(before.Value > -90 + 1, "Test needs the ball to still be in sight.");

        c.M.ShowHideKey(c.Now);

        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.Equal(before.Value, c.M.Y.Value);
        Assert.Equal(before.Velocity, c.M.Y.Velocity);
        Assert.Equal(LookConstants.TopGap, c.M.Y.Target);

        c.Run(3000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.InRange(c.M.Y.Value, 13.99, 14.01);
    }

    [Fact]
    public void Category_Key_During_Closing_Also_Reverses()
    {
        var c = OpenClock(PageIds.Media);
        c.M.ShowHideKey(c.Now);
        c.Run(300);
        var y = c.M.Y.Value;

        c.M.PageKey(PageIds.Vibe, c.Now);

        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.Equal(y, c.M.Y.Value);
        c.Run(3000);
        Assert.Equal(PageIds.Vibe, c.M.ContentsPageId);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
    }

    [Fact]
    public void Item_Click_Moves_The_Selection()
    {
        var c = OpenClock(PageIds.Apps);
        Assert.Equal(0, c.M.SelectedItem);
        c.M.ItemClick(4, c.Now);
        Assert.Equal(4, c.M.SelectedItem);
        c.M.ItemClick(1, c.Now);
        Assert.Equal(1, c.M.SelectedItem);
        c.M.ItemClick(5, c.Now); // out of range for 5 items
        c.M.ItemClick(-1, c.Now);
        Assert.Equal(1, c.M.SelectedItem);
    }

    [Fact]
    public void Item_Click_Resets_The_Idle_Clock()
    {
        var c = OpenClock();
        c.RunTo(c.Now + 40_000);
        c.M.ItemClick(0, c.Now);
        Assert.Equal(c.Now + 60_000, c.M.IdleDeadlineMs, 1e-9);
    }

    [Fact]
    public void Stretch_Applies_Only_While_Still_A_Ball_And_Preserves_Area()
    {
        var c = NewClock();
        c.M.ShowHideKey(0);
        c.RunTo(120);
        Assert.True(c.M.StretchY > 1.0);
        Assert.True(c.M.StretchY <= 1.34 + 1e-9);
        Assert.Equal(1 / Math.Sqrt(c.M.StretchY), c.M.StretchX, 1e-12);

        c.Run(3000);
        Assert.Equal(1.0, c.M.StretchY);
        Assert.Equal(1.0, c.M.StretchX);
    }

    [Fact]
    public void Drawn_Size_Never_Drops_Below_94_Percent_Of_The_Ball()
    {
        var c = NewClock();
        for (var round = 0; round < 3; round++)
        {
            c.M.ShowHideKey(c.Now);
            for (var i = 0; i < 360; i++)
            {
                c.Run(Clock.Frame);
                Assert.True(c.M.DrawnWidth >= 30 * 0.94 - 1e-9);
                Assert.True(c.M.DrawnHeight >= 30 * 0.94 - 1e-9);
                Assert.True(c.M.DrawnRadius <= Math.Min(c.M.DrawnWidth, c.M.DrawnHeight) / 2 + 1e-9);
            }
        }
    }

    [Fact]
    public void Repeated_Tick_With_The_Same_Time_Changes_Nothing()
    {
        var c = OpenClock();
        c.M.ShowHideKey(c.Now);
        c.Run(100);
        var y = c.M.Y;
        c.M.Tick(c.Now);
        c.M.Tick(c.Now);
        Assert.Equal(y, c.M.Y);
    }

    [Fact]
    public void Hammering_Keys_In_Every_Phase_Never_Breaks_The_Machine()
    {
        var c = NewClock();
        var rng = new Random(12345);
        for (var i = 0; i < 4000; i++)
        {
            c.Run(rng.Next(0, 90));
            switch (rng.Next(5))
            {
                case 0: c.M.ShowHideKey(c.Now); break;
                case 1: c.M.PageKey(Pages.BuiltIn[rng.Next(5)].Id, c.Now); break;
                case 2: c.M.Activity(c.Now); break;
                case 3: c.M.ItemClick(rng.Next(-1, 7), c.Now); break;
                default: break;
            }

            Assert.True(double.IsFinite(c.M.Y.Value) && double.IsFinite(c.M.Width.Value));
            Assert.InRange(c.M.SelectedItem, 0, c.M.ContentsItems.Count - 1);
            if (c.M.Phase == IslandPhase.Hidden) Assert.False(c.M.ContentsVisible);
        }

        // Whatever happened, a final dismiss and some quiet always ends in Hidden.
        if (c.M.Phase is IslandPhase.FlyingIn or IslandPhase.Open) c.M.ShowHideKey(c.Now);
        c.Run(10_000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
    }

    [Fact]
    public void Random_Input_Storm_Never_Jumps()
    {
        // EVALS.md M3. While any part of the island is on screen, each of the four springs changes
        // only by a spring step: at every step the value equals the previous value plus the new
        // velocity times the step. A direct assignment anywhere would break that equation.
        foreach (var seed in new[] { 7, 1234, 20261006, 99991, 424242 })
        {
            try
            {
                RunStorm(seed);
            }
            catch (Exception e) when (e is Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"seed {seed}: {e.Message}");
            }
        }
    }

    private static void RunStorm(int seed)
    {
        const double step = 1000.0 / 120;
        var rng = new Random(seed);
        var m = new IslandMachine(idleSeconds: 3);
        var previous = Snapshot(m);
        double now = 0;

        void Check(string what)
        {
            var current = Snapshot(m);
            if (previous.Phase != IslandPhase.Hidden && current.Phase != IslandPhase.Hidden)
            {
                for (var i = 0; i < 4; i++)
                {
                    var (before, after) = (previous.Springs[i], current.Springs[i]);
                    if (after.Steps < before.Steps)
                    {
                        // Snapped: only allowed when the island had already settled out of sight and became
                        // Hidden inside this one call, then was summoned again.
                        Assert.True(Math.Abs(previous.Springs[0].Value - (-LookConstants.SpawnHeightAboveEdge)) < LookConstants.HiddenSettlePosition,
                            $"{what}: spring {i} was snapped while the island was still in sight (y={previous.Springs[0].Value})");
                        continue;
                    }

                    Assert.True(after.Steps - before.Steps is 0 or 1, $"{what}: spring {i} took {after.Steps - before.Steps} steps at once");
                    if (after.Steps == before.Steps + 1)
                        Assert.True(Math.Abs(after.Value - (before.Value + after.Velocity * Spring.StepSeconds)) < 1e-9,
                            $"{what}: spring {i} jumped from {before.Value} to {after.Value} at t={now:F2}");
                    else
                        Assert.Equal(before.Value, after.Value);
                }
            }

            Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
            if (m.Phase == IslandPhase.Hidden) Assert.False(m.ContentsVisible, $"{what}: contents shown while hidden");
            if (m.Phase is IslandPhase.Hidden or IslandPhase.Closing) Assert.False(m.HasKeyboard, $"{what}: keyboard held while leaving");
            previous = current;
        }

        for (var n = 0; n < 24_000; n++)
        {
            var start = now;
            now = n * step;
            if (rng.NextDouble() < 0.05)
            {
                var at = start + rng.NextDouble() * (now - start);
                switch (rng.Next(13))
                {
                    case 11: m.SetNotice(rng.NextDouble() < 0.5, at); break; // a notice comes or is taken back
                    case 12: m.SetNoticeWidth(rng.Next(-5, 300), at); break;
                    case 9: m.SetPill(rng.NextDouble() < 0.7, rng.NextDouble() < 0.85, at); break; // something plays or stops, the table allows or not
                    case 10: m.SetPillWidth(rng.Next(-5, 220), at); break; // the title changes
                    case 0: m.ShowHideKey(at); break;
                    case 1: m.PageKey(Pages.BuiltIn[rng.Next(5)].Id, at); break;
                    case 2: m.Activity(at); break;
                    case 3: m.MainKey(at); break;
                    case 4: m.DigitKey(rng.Next(-1, 12), at); break;
                    case 5: m.EscapeKey(at); break;
                    case 6: m.FocusLost(at); break;
                    case 7: m.OtherKey(at); break;
                    default: m.ItemClick(rng.Next(-1, 7), at); break;
                }

                Check("input");
            }

            m.Tick(now);
            Check("tick");
        }

        m.SetNotice(false, now);
        m.SetPill(false, false, now); // nothing plays any more: whatever is up leaves
        if (m.Phase is IslandPhase.FlyingIn or IslandPhase.Open) m.ShowHideKey(now);
        for (var n = 0; n < 1200 && m.Phase != IslandPhase.Hidden; n++)
        {
            now += step;
            m.Tick(now);
            Check("settle");
        }

        Assert.Equal(IslandPhase.Hidden, m.Phase);
    }

    private static (IslandPhase Phase, Spring[] Springs) Snapshot(IslandMachine m) =>
        (m.Phase, [m.Y, m.Width, m.Height, m.Radius]);

    [Fact]
    public void Every_Summon_Takes_The_Same_Time()
    {
        // EVALS.md M4, in simulated time: repeated summons of one page take the identical time.
        foreach (var page in Pages.BuiltIn.Select(p => p.Id))
        {
            var durations = new List<double>();
            var m = new IslandMachine();
            double now = 0;
            for (var round = 0; round < 6; round++)
            {
                m.PageKey(page, now);
                var started = now;
                while (!m.IsAtRest && now - started < 10_000)
                {
                    now += Clock.Frame;
                    m.Tick(now);
                }

                durations.Add(now - started);
                m.ShowHideKey(now);
                while (m.Phase != IslandPhase.Hidden && now - started < 60_000)
                {
                    now += Clock.Frame;
                    m.Tick(now);
                }

                now += Clock.Frame * 7; // a pause of whole frames between summons
            }

            Assert.All(durations, d => Assert.Equal(durations[0], d, 1e-6));
            Assert.InRange(durations[0], 500, 3000);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(144)]
    [InlineData(240)]
    public void Summon_Takes_The_Same_Time_At_Another_Refresh_Rate(int hz)
    {
        double Measure(double frameMs)
        {
            var m = new IslandMachine();
            m.PageKey(PageIds.Browser, 0);
            double now = 0;
            while (!m.IsAtRest && now < 10_000)
            {
                now += frameMs;
                m.Tick(now);
            }

            return now;
        }

        var at60 = Measure(1000.0 / 60);
        var other = Measure(1000.0 / hz);
        Assert.InRange(Math.Abs(other - at60), 0, 1000.0 / Math.Min(hz, 60) + 1e-6);
    }

    [Fact]
    public void Summon_During_Closing_Keeps_The_Drawn_Position_Too()
    {
        var c = OpenClock();
        c.M.ShowHideKey(c.Now);
        c.Run(700);
        var drawn = c.M.DrawnY;
        c.M.ShowHideKey(c.Now);
        Assert.Equal(drawn, c.M.DrawnY, 1e-12);
    }

    [Fact]
    public void Event_Timing_Does_Not_Depend_On_When_Frames_Arrive()
    {
        // A summon, then asking at exactly 1 s: the shape is the same whether frames came at 60 Hz or in coarse lumps.
        double Width(Func<int, double> frame)
        {
            var m = new IslandMachine();
            m.PageKey(PageIds.Apps, 0);
            double now = 0;
            var i = 0;
            while (now < 1000 - 1e-9)
            {
                now = Math.Min(1000, now + frame(i++));
                m.Tick(now);
            }

            return m.DrawnWidth;
        }

        var steady = Width(_ => 1000.0 / 60);
        var coarse = Width(i => i % 2 == 0 ? 70 : 9);
        var fine = Width(_ => 1000.0 / 240);
        Assert.InRange(Math.Abs(steady - coarse), 0, 0.01);
        Assert.InRange(Math.Abs(steady - fine), 0, 0.01);
    }

    [Fact]
    public void Screen_Never_Changes_While_Visible()
    {
        // WORK-ORDER-6 section 1 (W2): two screens side by side; the island is summoned with the pointer on the first, and the
        // pointer then goes to the second while the ball flies in, the capsule is open and while it leaves. It stays where it was.
        var left = new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.0, IsPrimary: true);
        var right = new ScreenInfo(new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1040), 1.25);
        var screens = new PretendScreens { Screens = [left, right], Pointer = new ScreenPoint(500, 400) };
        var picker = new ScreenPicker();
        var c = NewClock();

        ScreenPlacement Ask() => picker.Update(screens.Pointer, screens.Screens, visible: c.M.Phase != IslandPhase.Hidden, 900, 300);

        var first = Ask();
        Assert.Equal(0, first.Index);
        c.M.MainKey(c.Now);
        screens.Pointer = new ScreenPoint(2500, 400); // the pointer goes to the second screen
        var choicesBefore = picker.Choices;

        foreach (var phase in new[] { 60.0, 400, 600 }) // flying in, expanding, open
        {
            c.Run(phase);
            Assert.NotEqual(IslandPhase.Hidden, c.M.Phase);
            Assert.Equal(first, Ask());
        }

        c.M.MainKey(c.Now); // leave
        while (c.M.Phase != IslandPhase.Hidden)
        {
            c.Run(30);
            if (c.M.Phase != IslandPhase.Hidden) Assert.Equal(first, Ask()); // also while it leaves
        }

        Assert.Equal(choicesBefore, picker.Choices); // a visible island never chose again

        // Hidden again: the next summon takes the screen under the pointer now.
        var second = Ask();
        Assert.Equal(1, second.Index);
        Assert.Equal(1.25, second.Screen.Scale);
        Assert.Equal(0, ScreenChooser.ChooseIndex(new ScreenPoint(500, 400), screens.Screens));
    }

    [Fact]
    public void Autostart_Launch_Shows_Nothing()
    {
        // WORK-ORDER-6 section 3: a start by Windows's start-up list (--autostart) brings the island up silently: it does not show itself
        // (the app summons it at start only when this says yes); a start by the person does. The machine itself never shows by itself.
        Assert.False(StartupSwitch.SummonsAtStart(["--autostart"]));
        Assert.False(StartupSwitch.SummonsAtStart(["--data-dir", "x", "--autostart"]));
        Assert.True(StartupSwitch.SummonsAtStart([]));
        Assert.True(StartupSwitch.SummonsAtStart(["--data-dir", "x"]));

        var machine = new IslandMachine();
        machine.Tick(0);
        machine.Tick(5000);
        Assert.Equal(IslandPhase.Hidden, machine.Phase); // nothing shows until a key, a click or the app's own start-up call says so
        Assert.False(machine.NeedsFrames);
        machine.PageKey(PageIds.Media, 5100); // the key still works after a silent start
        Assert.Equal(IslandPhase.FlyingIn, machine.Phase);
    }

    // ---- WORK-ORDER-6 section 4 ------------------------------------------------

    [Fact]
    public void Page_And_Pick_Keys_Summon_Without_The_Keyboard()
    {
        var pick = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);

        // A page's key, and a pick's key (which asks for that pick's page), bring the island in on the page and leave the keyboard alone.
        var pageKey = NewClock();
        pageKey.M.PageKey(PageIds.Vibe, pageKey.Now);
        Assert.Equal(PageIds.Vibe, pageKey.M.PageId);
        Assert.False(pageKey.M.HasKeyboard);

        var pickKey = NewClock();
        new PickKeyHandler(id => id == pick.Id ? pick : null, _ => { }, page => pickKey.M.PageKey(page, pickKey.Now)).Press(pick.Id);
        pickKey.Run(2000);
        Assert.Equal(IslandPhase.Open, pickKey.M.Phase);
        Assert.Equal(PageIds.Apps, pickKey.M.PageId);
        Assert.False(pickKey.M.HasKeyboard);

        // Open on another page already: the pick's key changes page, still with no keyboard.
        pickKey.M.PageKey(PageIds.Browser, pickKey.Now);
        new PickKeyHandler(id => id == pick.Id ? pick : null, _ => { }, page => pickKey.M.PageKey(page, pickKey.Now)).Press(pick.Id);
        Assert.Equal(PageIds.Apps, pickKey.M.PageId);
        Assert.False(pickKey.M.HasKeyboard);
    }

    [Fact]
    public void A_Pick_Key_Still_Jumps_When_The_Island_Stays_Away()
    {
        var c = NewClock();
        var pick = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var log = new List<string>();
        // The island stays away (what is in front says so): the "come in" step is asked for but does not move the machine.
        var handler = new PickKeyHandler(id => id == pick.Id ? pick : null, p => log.Add("jump " + p.Id), page => log.Add("come in on " + page));

        Assert.True(handler.Press(pick.Id));

        Assert.Equal(["jump " + pick.Id, "come in on " + PageIds.Apps], log); // the jump is asked for first, inside the key's own handler
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);

        // A key whose pick is gone does nothing at all.
        Assert.False(handler.Press("program:gone"));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void The_Idle_Time_Changed_In_The_Settings_Counts_From_Then()
    {
        var c = NewClock(idleSeconds: 60);
        c.M.PageKey(PageIds.Apps, c.Now);
        c.Run(2000);
        c.M.SetIdleSeconds(3, c.Now);
        Assert.Equal(c.Now + 3000, c.M.IdleDeadlineMs, 1e-6);

        c.M.SetIdleSeconds(double.NaN, c.Now); // nonsense changes nothing
        c.M.SetIdleSeconds(-4, c.Now);
        Assert.Equal(c.Now + 3000, c.M.IdleDeadlineMs, 1e-6);

        c.Run(3100);
        Assert.NotEqual(IslandPhase.Open, c.M.Phase);
    }
}
