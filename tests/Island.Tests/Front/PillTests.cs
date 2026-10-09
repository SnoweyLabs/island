using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-7 section 2: the small pill and how it shares the one window with the capsule.</summary>
public class PillTests
{
    private static Clock NewClock(double idleSeconds = 5) => new(new IslandMachine(idleSeconds));

    private static Clock PillUp()
    {
        var c = NewClock();
        c.M.SetPill(wanted: true, allowed: true, c.Now);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.ShowsPill);
        return c;
    }

    [Fact]
    public void Appears_When_A_Counting_Source_Starts()
    {
        var c = NewClock();
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);

        c.M.SetPill(wanted: true, allowed: true, c.Now);

        // The ball flies in as always and opens into the pill, without the keyboard.
        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.True(c.M.ShowsPill);
        Assert.False(c.M.HasKeyboard);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(PillLayout.WidestWidth, c.M.Width.Target);
        Assert.Equal(PillLayout.Height, c.M.Height.Target);
        Assert.Equal(PillLayout.Radius, c.M.Radius.Target);
        Assert.InRange(c.M.DrawnWidth, PillLayout.WidestWidth - 0.2, PillLayout.WidestWidth + 0.2);
        Assert.True(c.M.ContentsVisible);

        // It stays for as long as the source plays and the table allows it: no idle time ends it.
        c.Run(120_000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);

        // Playing stops: it leaves after the idle time, through the springs.
        c.M.SetPill(wanted: false, allowed: true, c.Now);
        c.Run(4900);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.Run(3000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        Assert.False(c.M.ShowsPill);

        // Its width follows its title.
        var again = PillUp();
        again.M.SetPillWidth(60, again.Now);
        again.Run(2500);
        Assert.InRange(again.M.DrawnWidth, PillLayout.Width(60) - 0.2, PillLayout.Width(60) + 0.2);
    }

    [Fact]
    public void Main_Key_Grows_It_Into_The_Capsule()
    {
        var c = PillUp();

        c.M.MainKey(c.Now);

        // The pill grows into the capsule and the island takes the keyboard, as on any main-key summon.
        Assert.False(c.M.ShowsPill);
        Assert.True(c.M.HasKeyboard);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.IsAtRest);
        Assert.Equal(CapsuleLayout.SizeFor(c.M.Contents).Width, c.M.Width.Target);
        Assert.Equal(LookConstants.CapsuleHeight, c.M.Height.Target);

        // A page's key, and the tray menu, grow it without the keyboard.
        var page = PillUp();
        page.M.PageKey(PageIds.Apps, page.Now);
        Assert.False(page.M.ShowsPill);
        Assert.False(page.M.HasKeyboard);
        Assert.Equal(PageIds.Apps, page.M.PageId);
        var tray = PillUp();
        tray.M.ShowHideKey(tray.Now);
        Assert.False(tray.M.ShowsPill);
        Assert.False(tray.M.HasKeyboard);
    }

    [Fact]
    public void Capsule_Shrinks_Back_Only_If_The_Table_Allows_The_Pill()
    {
        // The table allows it and something plays: Esc, the main key and the idle time turn the capsule into the pill, never fly it out.
        var c = PillUp();
        c.M.MainKey(c.Now);
        c.Run(2500);
        c.M.EscapeKey(c.Now);
        Assert.True(c.M.ShowsPill);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(PillLayout.Height, c.M.Height.Target);

        c.M.MainKey(c.Now);
        c.Run(2500);
        c.M.MainKey(c.Now); // the main key on the capsule
        Assert.True(c.M.ShowsPill);
        c.Run(2500);
        c.M.PageKey(PageIds.Vibe, c.Now);
        c.Run(2500);
        c.Run(8000); // the idle time
        Assert.True(c.M.ShowsPill);
        Assert.Equal(IslandPhase.Open, c.M.Phase);

        // The table does not allow the pill at that moment: the capsule flies out as always.
        var away = PillUp();
        away.M.MainKey(away.Now);
        away.Run(2500);
        away.M.SetPill(wanted: true, allowed: false, away.Now);
        Assert.Equal(IslandPhase.Open, away.M.Phase); // the capsule is not the pill: nothing leaves yet
        away.M.EscapeKey(away.Now);
        Assert.Equal(IslandPhase.Closing, away.M.Phase);
        away.Run(3000);
        Assert.Equal(IslandPhase.Hidden, away.M.Phase);

        // Nothing plays any more: the capsule flies out as always.
        var silent = PillUp();
        silent.M.MainKey(silent.Now);
        silent.Run(2500);
        silent.M.SetPill(wanted: false, allowed: true, silent.Now);
        silent.M.EscapeKey(silent.Now);
        Assert.Equal(IslandPhase.Closing, silent.M.Phase);
    }

    [Fact]
    public void Shrinking_Returns_The_Keyboard()
    {
        // The keyboard goes back at the moment the shrinking begins, whichever way it begins: the pill never holds it.
        foreach (var way in new[] { "esc", "main", "idle" })
        {
            var c = PillUp();
            c.M.MainKey(c.Now);
            c.Run(2500);
            Assert.True(c.M.HasKeyboard);

            switch (way)
            {
                case "esc": c.M.EscapeKey(c.Now); break;
                case "main": c.M.MainKey(c.Now); break;
                default: c.Run(6000); break;
            }

            Assert.True(c.M.ShowsPill, way);
            Assert.False(c.M.HasKeyboard, way);
        }

        // And a pill that is up never has it, whatever is pressed.
        var up = PillUp();
        up.M.DigitKey(2, up.Now);
        up.M.EscapeKey(up.Now);
        up.M.OtherKey(up.Now);
        Assert.False(up.M.HasKeyboard);
        Assert.True(up.M.ShowsPill);
    }

    [Fact]
    public void Setting_Off_Means_No_Pill()
    {
        // "Show the pill while something plays" off: the app says nothing is wanted, and the pill never comes, however long it plays.
        var c = NewClock();
        c.M.SetPill(wanted: false, allowed: true, c.Now);
        c.Run(5000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
        Assert.False(c.M.ShowsPill);

        // A table that does not allow it keeps it away too.
        c.M.SetPill(wanted: true, allowed: false, c.Now);
        c.Run(5000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);

        // The setting is kept, off or on, and on by default.
        Assert.True(Settings.Defaults.ShowPill);
        foreach (var on in new[] { true, false })
        {
            var back = Settings.Parse((Settings.Defaults with { ShowPill = on }).ToJson());
            Assert.Equal(on, back.Settings.ShowPill);
        }

        Assert.Equal(SettingsStatus.Unreadable, Settings.Parse("""{ "showPill": "yes" }""").Status);
    }

    [Fact]
    public void A_Visible_Pill_Leaves_When_The_Table_Says_Stay_Away()
    {
        // Something that appeared by itself also leaves by itself: the foreground changes to a fullscreen program (or the mode changes), the
        // app asks the table again, and the pill leaves through the springs.
        foreach (var (front, mode) in new[] { (FrontState.FullscreenProgram, Mode.Vibe), (FrontState.FullscreenProgram, Mode.Focus), (FrontState.Presentation, Mode.Vibe), (FrontState.Clear, Mode.DND), (FrontState.ExclusiveFullscreen, Mode.Focus) })
        {
            var c = PillUp();
            Assert.Equal(ShowAnswer.Show, ShowDecision.Decide(FrontState.Clear, Appearer.Pill, ShowOrigin.ByItself, Mode.Vibe));

            var allowed = ShowDecision.Decide(front, Appearer.Pill, ShowOrigin.ByItself, mode) == ShowAnswer.Show;
            c.M.SetPill(wanted: true, allowed, c.Now);

            Assert.False(allowed, $"{front}/{mode}");
            Assert.Equal(IslandPhase.Closing, c.M.Phase);
            c.Run(3000);
            Assert.Equal(IslandPhase.Hidden, c.M.Phase);

            // The table allows it again (the program left, or the mode changed): it comes back by itself.
            c.M.SetPill(wanted: true, allowed: true, c.Now);
            Assert.True(c.M.ShowsPill);
            Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        }

        // Leaving while it is still on its way out is reversible: it turns round in place.
        var turn = PillUp();
        turn.M.SetPill(true, false, turn.Now);
        turn.Run(100);
        Assert.Equal(IslandPhase.Closing, turn.M.Phase);
        turn.M.SetPill(true, true, turn.Now);
        Assert.Equal(IslandPhase.FlyingIn, turn.M.Phase);
        turn.Run(3000);
        Assert.Equal(IslandPhase.Open, turn.M.Phase);
    }

    [Fact]
    public void The_Pill_Is_Forty_Four_High_And_Its_Width_Is_The_Sum_Of_Its_Parts()
    {
        Assert.Equal(44, PillLayout.Height);
        Assert.Equal(22, PillLayout.Radius);
        // 7 + 30 + 6 + title + 6 + four buttons of 26 with 6 between + 8.
        Assert.Equal(7 + 30 + 6 + 150 + 6 + 4 * 26 + 3 * 6 + 8, PillLayout.Width(150));
        Assert.Equal(PillLayout.Width(150), PillLayout.Width(9000)); // never wider than 150 of title
        Assert.Equal(PillLayout.Width(PillLayout.TitleMinWidth), PillLayout.Width(0));
        Assert.Equal(PillLayout.WidestWidth, PillLayout.Width(double.NaN));
    }
}
