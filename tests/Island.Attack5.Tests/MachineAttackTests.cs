using Island.Core;

namespace Island.Attack5.Tests;

/// <summary>WORK-ORDER-5 section 5: IslandMachine's second row, in every phase, order and millisecond.</summary>
public class MachineAttackTests
{
    private static readonly Page Games = new("games", "Games", "#12AB34", "dot", null, false);
    private static readonly Page Music = new("music", "Music", "#AB1234", "dot", null, false);

    private static void ExpectHeightAgrees(IslandMachine m, string what)
    {
        // The row flag and the height spring's target may disagree only while the island is not an open capsule.
        if (m.Phase != IslandPhase.Open) return;
        var expected = m.SecondRowOpen ? ChoiceConstants.TwoRowHeight : LookConstants.CapsuleHeight;
        Assert.True(m.Height.Target == expected, $"{what}: SecondRowOpen={m.SecondRowOpen} but the height target is {m.Height.Target}");
    }

    // ---- what held ----

    [Fact]
    public void Holds_The_Row_Opens_And_Closes_Through_The_Height_Spring_Only()
    {
        var c = Clock.OpenOnApps();
        var h0 = c.M.Height.Value;
        c.M.ToggleSecondRow(c.Now);
        Assert.True(c.M.SecondRowOpen);
        Assert.Equal(ChoiceConstants.TwoRowHeight, c.M.Height.Target);
        Assert.Equal(h0, c.M.Height.Value); // not jumped: the spring moves it on the next frames
        c.Run(2500);
        Assert.Equal(150, c.M.DrawnHeight, 1);
        Assert.True(c.M.IsAtRest);

        c.M.ToggleSecondRow(c.Now);
        Assert.False(c.M.SecondRowOpen);
        c.Run(2500);
        Assert.Equal(76, c.M.DrawnHeight, 1);
    }

    [Fact]
    public void Holds_The_Row_Never_Opens_While_Hidden_Flying_In_Or_Out()
    {
        var m = new IslandMachine();
        double now = 0;
        m.ToggleSecondRow(now); // hidden
        Assert.False(m.SecondRowOpen);

        m.ShowHideKey(now); // flying in: the capsule is still a ball
        m.ToggleSecondRow(now + 100);
        Assert.False(m.SecondRowOpen);
        Assert.Equal(IslandPhase.FlyingIn, m.Phase);

        var c = new Clock(m) { Now = now + 100 };
        c.Run(3000);
        Assert.Equal(IslandPhase.Open, m.Phase);
        m.ToggleSecondRow(c.Now);
        Assert.True(m.SecondRowOpen);
        m.ShowHideKey(c.Now); // flying out
        Assert.False(m.SecondRowOpen);
        m.ToggleSecondRow(c.Now + 20);
        Assert.False(m.SecondRowOpen);
        Assert.Equal(IslandPhase.Closing, m.Phase);
        c.Run(5000);
        Assert.Equal(IslandPhase.Hidden, m.Phase);
        m.CloseSecondRow(c.Now); // closing a row that is not there is nothing
        Assert.False(m.SecondRowOpen);
    }

    [Fact]
    public void Holds_Non_Finite_Times_Are_Ignored_By_Every_Row_Input()
    {
        var c = Clock.OpenOnApps();
        c.M.ToggleSecondRow(c.Now);
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var before = (c.M.Phase, c.M.SecondRowOpen, c.M.IdleDeadlineMs, c.M.SelectedItem, c.M.RowClosedCount, c.M.PageId);
            c.M.ToggleSecondRow(bad);
            c.M.CloseSecondRow(bad);
            c.M.EscapeKey(bad);
            c.M.PageKey(PageIds.Folders, bad);
            c.M.DigitKey(2, bad);
            c.M.MainKey(bad);
            c.M.ContentsChanged(bad);
            c.M.SetPages(Pages.BuiltIn, bad);
            c.M.Tick(bad);
            Assert.Equal(before, (c.M.Phase, c.M.SecondRowOpen, c.M.IdleDeadlineMs, c.M.SelectedItem, c.M.RowClosedCount, c.M.PageId));
        }
    }

    [Fact]
    public void Holds_One_Escape_Does_One_Thing_Even_On_The_Same_Millisecond()
    {
        var c = Clock.OpenOnApps();
        c.M.MainKey(c.Now); // dismiss (the main key toggles)
        c.Run(3000);
        c.M.MainKey(c.Now); // summon with the keyboard
        c.Run(3000);
        Assert.True(c.M.HasKeyboard);

        c.M.ToggleSecondRow(c.Now);
        c.M.EscapeKey(c.Now); // closes the row, the island stays
        Assert.False(c.M.SecondRowOpen);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.M.EscapeKey(c.Now); // same millisecond, the next press: sends the island away
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
        c.M.EscapeKey(c.Now); // a third does nothing
        Assert.Equal(IslandPhase.Closing, c.M.Phase);
    }

    [Fact]
    public void Holds_Escape_Without_The_Keyboard_Does_Nothing_And_Leaves_The_Row_Open()
    {
        var c = Clock.OpenOnApps(); // summoned by the page key: no keyboard
        c.M.FocusLost(c.Now);
        c.M.ToggleSecondRow(c.Now);
        c.M.EscapeKey(c.Now);
        Assert.True(c.M.SecondRowOpen);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
    }

    [Fact]
    public void Holds_A_Page_Change_Closes_The_Row_Before_The_Page_Changes_On_The_Same_Millisecond()
    {
        var c = Clock.OpenOnApps();
        c.M.ToggleSecondRow(c.Now);
        c.M.PageKey(PageIds.Folders, c.Now);
        Assert.False(c.M.SecondRowOpen);
        Assert.True(c.M.RowClosedOrder > 0 && c.M.RowClosedOrder < c.M.PageChangedOrder);
        Assert.Equal(PageIds.Folders, c.M.PageId);
        ExpectHeightAgrees(c.M, "after the page key");
        c.Run(2500);
        Assert.Equal(76, c.M.DrawnHeight, 1);

        // the same page again changes nothing and does not close the row
        c.M.ToggleSecondRow(c.Now);
        c.M.PageKey(PageIds.Folders, c.Now);
        c.M.DigitKey(2, c.Now); // folders is the second page
        Assert.True(c.M.SecondRowOpen);
    }

    [Fact]
    public void Holds_The_Contents_Changing_Keeps_The_Row_Open_And_The_Height_Target()
    {
        var count = 5;
        var c = Clock.OpenOnApps(p => Make.Items(p.Id == PageIds.Apps ? count : 2));
        c.M.ToggleSecondRow(c.Now);
        c.Run(500);
        count = 6;
        c.M.ContentsChanged(c.Now);
        Assert.True(c.M.SecondRowOpen);
        c.Run(2500);
        Assert.True(c.M.SecondRowOpen);
        Assert.Equal(ChoiceConstants.TwoRowHeight, c.M.Height.Target);
        Assert.Equal(150, c.M.DrawnHeight, 1);
        ExpectHeightAgrees(c.M, "after the contents changed");
    }

    [Fact]
    public void Holds_SetPages_Closes_The_Row_And_Replaces_A_Removed_Page()
    {
        var m = new IslandMachine(30, [.. Pages.BuiltIn, Games]);
        var c = new Clock(m);
        m.PageKey(Games.Id, 0);
        c.Run(2000);
        m.ToggleSecondRow(c.Now);
        Assert.True(m.SecondRowOpen);

        m.SetPages(Pages.BuiltIn, c.Now); // the page being shown is removed in the settings
        Assert.False(m.SecondRowOpen);
        Assert.Equal(PageIds.Media, m.PageId);
        c.Run(2500);
        Assert.Equal(PageIds.Media, m.ContentsPageId);
        Assert.Equal(76, m.DrawnHeight, 1);
        m.SetPages([], c.Now); // an empty list is ignored
        Assert.Equal(PageIds.Media, m.PageId);
    }

    [Fact]
    public void Holds_The_Idle_Clock_Dismisses_A_Capsule_With_The_Row_Open()
    {
        var c = Clock.OpenOnApps(idleSeconds: 3);
        c.M.ToggleSecondRow(c.Now);
        c.Run(3500);
        Assert.False(c.M.SecondRowOpen);
        Assert.NotEqual(IslandPhase.Open, c.M.Phase);
        c.Run(5000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
    }

    [Fact]
    public void Holds_Time_Going_Backwards_Does_Not_Throw()
    {
        var c = Clock.OpenOnApps();
        c.M.ToggleSecondRow(c.Now);
        c.M.CloseSecondRow(c.Now - 500);
        c.M.ToggleSecondRow(c.Now - 1000);
        c.M.PageKey(PageIds.Vibe, c.Now - 2000);
        c.M.EscapeKey(c.Now - 3000);
        c.M.Tick(c.Now - 4000);
        Assert.True(double.IsFinite(c.M.Height.Value));
    }

    [Fact]
    public void Holds_A_Storm_Of_Every_Input_With_Static_Pages()
    {
        foreach (var seed in new[] { 1, 22, 333, 4444, 55555 }) Storm(seed, dynamicItems: false);
    }

    [Fact]
    public void Holds_A_Storm_Of_Every_Input_With_Picks_That_Come_And_Go_Except_The_Selection_Range()
    {
        // The selection range is Defect_Selection_Points_Past_The_End...; everything else holds with changing picks.
        foreach (var seed in new[] { 7, 88, 999 }) Storm(seed, dynamicItems: true, checkSelection: false);
    }

    private static void Storm(int seed, bool dynamicItems, bool checkSelection = true)
    {
        const double step = 1000.0 / 120;
        var rng = new Random(seed);
        var counts = new Dictionary<string, int>();
        IReadOnlyList<Page> pages = [.. Pages.BuiltIn, Games, Music];
        var m = new IslandMachine(
            idleSeconds: 2,
            pages,
            p => Make.Items(dynamicItems ? counts.GetValueOrDefault(p.Id, 4) : 4, plus: true),
            p => dynamicItems ? counts.GetValueOrDefault(p.Id, 4) - 1 : 0); // may ask for a selection past the end
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
                    if (after.Steps < before.Steps) continue; // a snap, from a ball that had settled out of sight
                    Assert.True(after.Steps - before.Steps is 0 or 1, $"{what}: spring {i} took {after.Steps - before.Steps} steps at once");
                    if (after.Steps == before.Steps + 1)
                        Assert.True(Math.Abs(after.Value - (before.Value + after.Velocity * Spring.StepSeconds)) < 1e-9, $"{what}: spring {i} jumped from {before.Value} to {after.Value}");
                    else
                        Assert.Equal(before.Value, after.Value);
                }
            }

            _ = m.Page;
            _ = m.ContentsPage;
            _ = m.Contents;
            if (checkSelection) Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
            if (m.SecondRowOpen) Assert.Equal(IslandPhase.Open, m.Phase);
            if (m.Phase == IslandPhase.Hidden)
            {
                Assert.False(m.ContentsVisible, $"{what}: contents shown while hidden");
                Assert.False(m.SecondRowOpen);
                Assert.False(m.HasKeyboard);
            }

            if (m.Phase is IslandPhase.Closing) Assert.False(m.HasKeyboard);
            ExpectHeightAgrees(m, what);
            Assert.True(double.IsFinite(m.Height.Value) && double.IsFinite(m.Width.Value) && double.IsFinite(m.Y.Value));
            if (m.Phase is IslandPhase.FlyingIn or IslandPhase.Open) Assert.True(double.IsFinite(m.IdleDeadlineMs));
            previous = current;
        }

        for (var n = 0; n < 24_000; n++)
        {
            var start = now;
            now = n * step;
            if (rng.NextDouble() < 0.06)
            {
                var burst = rng.Next(1, 4); // up to three inputs on the very same millisecond
                var at = start + rng.NextDouble() * (now - start);
                for (var b = 0; b < burst; b++)
                {
                    Input(m, rng, at, pages, counts, ref pages);
                    Check("input");
                }
            }

            m.Tick(now);
            Check("tick");
        }

        if (m.Phase is IslandPhase.FlyingIn or IslandPhase.Open) m.ShowHideKey(now);
        for (var n = 0; n < 1200 && m.Phase != IslandPhase.Hidden; n++)
        {
            now += step;
            m.Tick(now);
            Check("settle");
        }

        Assert.Equal(IslandPhase.Hidden, m.Phase);
    }

    private static void Input(IslandMachine m, Random rng, double at, IReadOnlyList<Page> current, Dictionary<string, int> counts, ref IReadOnlyList<Page> pages)
    {
        double[] bad = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        if (rng.Next(25) == 0)
        {
            // a non-finite time must change nothing
            var before = (m.Phase, m.SecondRowOpen, m.PageId, m.SelectedItem, m.IdleDeadlineMs);
            var t = bad[rng.Next(3)];
            m.ToggleSecondRow(t);
            m.EscapeKey(t);
            m.PageKey(PageIds.Vibe, t);
            m.ContentsChanged(t);
            Assert.Equal(before, (m.Phase, m.SecondRowOpen, m.PageId, m.SelectedItem, m.IdleDeadlineMs));
            return;
        }

        switch (rng.Next(16))
        {
            case 0: m.ShowHideKey(at); break;
            case 1: m.MainKey(at); break;
            case 2: m.PageKey(current[rng.Next(current.Count)].Id, at); break;
            case 3: m.PageKey("no-such-page", at); break;
            case 4: m.DigitKey(rng.Next(-1, 12), at); break;
            case 5: m.EscapeKey(at); break;
            case 6:
            case 7: m.ToggleSecondRow(at); break;
            case 8: m.CloseSecondRow(at); break;
            case 9: m.ItemClick(rng.Next(-1, 9), at); break;
            case 10: m.FocusLost(at); break;
            case 11: m.OtherKey(at); break;
            case 12: m.Activity(at); break;
            case 13:
                foreach (var p in current) counts[p.Id] = rng.Next(0, 12);
                m.ContentsChanged(at);
                break;
            case 14:
                // pages come and go: the settings screen made or removed some
                var next = new List<Page>(Pages.BuiltIn);
                if (rng.Next(2) == 0) next.Add(Games);
                if (rng.Next(2) == 0) next.Add(Music);
                pages = next;
                m.SetPages(next, at);
                break;
            default: m.SetPages([], at); break;
        }
    }

    private static (IslandPhase Phase, Spring[] Springs) Snapshot(IslandMachine m) => (m.Phase, [m.Y, m.Width, m.Height, m.Radius]);

    // ---- what broke ----

    [Fact]
    public void Defect_Selection_Points_Past_The_End_After_The_Selected_Pick_Is_Removed_While_Open()
    {
        // Apps shows five picks; Dan lifts the fifth off the island. The app calls ContentsChanged, which only schedules the
        // swap 120 ms later; until then SelectedItem is 4 on a page of 3 rows, and ContentsItems[SelectedItem] throws.
        var count = 5;
        var c = Clock.OpenOnApps(_ => Make.Items(count, plus: false));
        c.M.ItemClick(4, c.Now);
        Assert.Equal(4, c.M.SelectedItem);

        count = 3;
        c.M.ContentsChanged(c.Now);
        Assert.InRange(c.M.SelectedItem, 0, c.M.ContentsItems.Count - 1);
        c.Run(60);
        Assert.InRange(c.M.SelectedItem, 0, c.M.ContentsItems.Count - 1);
    }

    [Fact]
    public void Holds_A_Preferred_Selection_Past_The_End_Is_Clamped_At_Once_By_A_Summon()
    {
        // The Media page opens on the pick that is playing (preferredSelection). A callback that is out of date and names an item
        // past the end never leaves SelectedItem out of range, not even during the flight.
        var m = new IslandMachine(30, Pages.BuiltIn, _ => Make.Items(2), _ => 9);
        m.PageKey(PageIds.Media, 0);
        Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
        var c = new Clock(m);
        c.Run(2000);
        Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
    }

    [Fact]
    public void Defect_Resummon_Within_The_First_110_Ms_Of_Leaving_Keeps_The_Capsule_Two_Rows_High_But_Empty()
    {
        // The row is open; the island is sent away; before the shrink to a ball fires (110 ms) it is summoned again. Summon
        // closes the row without retargeting the height: the target stays 150 for the 320 ms until Expand, while SecondRowOpen is false
        // and the contents are hidden. A 150 high empty capsule is drawn for a third of a second.
        var c = Clock.OpenOnApps();
        c.M.ToggleSecondRow(c.Now);
        c.Run(2500);
        c.M.ShowHideKey(c.Now); // away
        c.Run(50);
        c.M.ShowHideKey(c.Now); // back
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.False(c.M.SecondRowOpen);
        Assert.NotEqual(ChoiceConstants.TwoRowHeight, c.M.Height.Target);
    }

    [Fact]
    public void Defect_Closing_The_Row_With_Escape_Does_Not_Reset_The_Idle_Clock()
    {
        // Every other key reaching the island with the keyboard (digits, any key) and every click pokes the idle clock;
        // Esc that closes the second row does not, so the island can leave under the person's fingers a moment after the Esc.
        var c = Clock.OpenOnApps(idleSeconds: 5);
        c.M.MainKey(c.Now);
        c.Run(6000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        c.M.MainKey(c.Now);
        c.Run(3000);
        c.M.ToggleSecondRow(c.Now);
        c.Run(4000);
        var deadline = c.M.IdleDeadlineMs;
        c.M.EscapeKey(c.Now);
        Assert.False(c.M.SecondRowOpen);
        Assert.True(c.M.IdleDeadlineMs > deadline, "Esc closed the row but the idle deadline did not move");
    }

    [Fact]
    public void Defect_A_Row_Closed_By_The_Island_Leaving_Is_Not_Counted_As_Closed()
    {
        // RowClosedCount is documented "how many times the second row has closed" and the work order lists "with the island"
        // among the ways it closes. Dismiss (and Summon) clear the row without counting it or stamping its order.
        var c = Clock.OpenOnApps();
        c.M.ToggleSecondRow(c.Now);
        c.M.ShowHideKey(c.Now); // the island is sent away with the row open
        Assert.False(c.M.SecondRowOpen);
        Assert.Equal(1, c.M.RowClosedCount);
    }
}
