using Island.Core;

namespace Island.App;

/// <summary>
/// WO2-KEYS section 2 checks: the island takes the keyboard when it is called with the main key, returns it
/// where it came from, and never takes it when it appears by itself. No key is pressed and no input is faked:
/// the same handlers the keybind and the island's key messages call are called directly, and Windows is only
/// asked which window is in front. Windows and the foreground are recorded as same/different, never by name.
/// </summary>
internal sealed class KeyboardStage(SelfTestReport report, TimeSpan hangLimit)
{
    private const int Escape = 0x1B;
    private const int Digit3 = 0x33;
    private const int Digit9 = 0x39;
    private const int OtherKey = 0x41;
    private const double ShortIdleSeconds = 1.5;

    public async Task RunAsync()
    {
        // The "other program" the keyboard comes from and goes back to is a plain window of the self-test's own
        // process: the self-test never brings any other program forward (the outside gate would refuse it).
        var other = new System.Windows.Window
        {
            Title = "Island self-test: stand-in for another program",
            Width = 240,
            Height = 90,
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            Left = 40,
            Top = 400,
            Topmost = false,
        };
        other.Show();
        var before = new System.Windows.Interop.WindowInteropHelper(other).EnsureHandle();
        try
        {
            await Task.Delay(150);
            OutsideForeground.BringForward(before);
            var granted = Native.GetForegroundWindow() == before;
            report.Info["foregroundGranted"] = granted;
            if (!granted)
            {
                report.NeedsHumanVerify.Add("Windows did not let the self-test bring its own stand-in window forward (foregroundGranted: false), so taking and returning the keyboard was not checked here: press Ctrl+Q, 3, Esc in another program and see whether you land back in the same place.");
                return;
            }

            using var rt = new IslandRuntime(ShortIdleSeconds);
            rt.Show();
            await Task.Delay(200);
            var island = rt.Host.Capsule.Handle;

            await MainKeyRoundTrip(rt, before, island);
            await StartupSummon(rt, before);
            await IdleReturnsTheKeyboard(rt, before, island);
            await ClickingElsewhere(rt, before, island);
            await ClicksDoNotTakeIt(rt, before);
        }
        finally
        {
            other.Close();
        }
    }

    private async Task MainKeyRoundTrip(IslandRuntime rt, IntPtr before, IntPtr island)
    {
        var c = rt.Controller;
        var m = c.Machine;

        c.MainKey();
        var inFront = Native.GetForegroundWindow() == island;
        report.Info["foregroundAfterMainKeySummon"] = inFront ? "island" : "other";
        report.Check("after a main-key summon the foreground window is the island's", inFront && m.HasKeyboard,
            inFront ? "island" : "another window; Windows did not let the self-test take the foreground");
        report.Check("the island window holds the keyboard focus after a main-key summon", Native.FocusWindowOfForeground() == island,
            "GetGUIThreadInfo focus window is the island's");
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Open, "the island open after the main key", hangLimit, report);

        c.HandleKey(Digit3);
        report.Check("a digit key handler call switches the page", m.PageId == PageIds.Apps, $"page {m.PageId}");
        c.HandleKey(Digit9);
        c.HandleKey(OtherKey);
        report.Check("a digit beyond the last page and any other key change nothing", m.PageId == PageIds.Apps && m.Phase == IslandPhase.Open,
            $"page {m.PageId}, phase {m.Phase}");
        await Waiter.UntilAsync(() => m.ContentsPageId == PageIds.Apps && m.IsAtRest, "the apps page open and at rest", hangLimit, report);

        c.HandleKey(Escape);
        report.Check("Esc dismisses the island", m.Phase == IslandPhase.Closing, $"phase {m.Phase}");
        var back = Native.GetForegroundWindow() == before;
        report.Info["foregroundAfterEsc"] = back ? "same" : "different";
        report.Check("after the dismissal the foreground window is the same one as before the summon", back,
            back ? "same" : "different");
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden after Esc", hangLimit, report);

        // The main key a second time dismisses too, and gives the keyboard back.
        c.MainKey();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Open, "the island open for the main-key dismissal", hangLimit, report);
        c.MainKey();
        var backAgain = Native.GetForegroundWindow() == before;
        report.Check("the main key dismisses and gives the keyboard back", m.Phase == IslandPhase.Closing && backAgain,
            $"phase {m.Phase}, foreground {(backAgain ? "same" : "different")}");
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden after the main key", hangLimit, report);
    }

    /// <summary>What start-up and the tray menu call: the island shows and the foreground never moves.</summary>
    private async Task StartupSummon(IslandRuntime rt, IntPtr before)
    {
        var c = rt.Controller;
        var m = c.Machine;
        using var watch = new FocusWatch(rt.Host.Capsule.Handle, rt.Host.Shadow.Handle);

        c.ShowHide();
        var pageBefore = m.PageId; // the island comes back in the page it last showed
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Open, "the island open after a start-up summon", hangLimit, report);
        c.HandleKey(Digit3);
        c.HandleKey(Escape);
        var same = Native.GetForegroundWindow() == before;
        report.Info["foregroundAfterStartupSummon"] = same ? "same" : "different";
        report.Check("after a start-up summon the foreground window is unchanged", same && !m.HasKeyboard && !watch.OurWindowWasForeground,
            $"{(same ? "same" : "different")}, island never in front: {!watch.OurWindowWasForeground}");
        report.Check("with no keyboard, digit and Esc calls do nothing", m.PageId == pageBefore && m.Phase == IslandPhase.Open,
            $"page {m.PageId}, phase {m.Phase}");

        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden after the start-up summon", hangLimit, report);
    }

    /// <summary>A short idle time in a temporary run: an open island that has the keyboard closes by itself and returns it.</summary>
    private async Task IdleReturnsTheKeyboard(IslandRuntime rt, IntPtr before, IntPtr island)
    {
        var c = rt.Controller;
        var m = c.Machine;

        c.MainKey();
        var took = Native.GetForegroundWindow() == island;
        var closing = await Waiter.UntilAsync(() => m.Phase is IslandPhase.Closing or IslandPhase.Hidden, "the island closing by itself", hangLimit, report);
        var back = Native.GetForegroundWindow() == before;
        report.Check("an open island that has the keyboard closes by itself and returns the keyboard", took && closing && !m.HasKeyboard && back,
            $"took it: {took}, closed: {closing}, foreground afterwards {(back ? "same" : "different")}");
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden after the idle time", hangLimit, report);
    }

    /// <summary>
    /// The person clicks into another window while the island is open: simulated by bringing the earlier
    /// window to the front ourselves. The island stays until its idle time, no longer has the keyboard,
    /// and does not pull it back.
    /// </summary>
    private async Task ClickingElsewhere(IslandRuntime rt, IntPtr before, IntPtr island)
    {
        var c = rt.Controller;
        var m = c.Machine;

        c.MainKey();
        var pageBefore = m.PageId;
        if (Native.GetForegroundWindow() != island)
        {
            report.Check("the island could take the keyboard for the click-elsewhere check", false, "Windows did not let the self-test take the foreground");
            return;
        }

        OutsideForeground.BringForward(before);
        var released = await Waiter.UntilAsync(() => !m.HasKeyboard, "the island noticing it lost the keyboard", hangLimit, report);
        c.HandleKey(Digit3);
        var stays = m.Phase is IslandPhase.FlyingIn or IslandPhase.Open && m.PageId == pageBefore;
        report.Check("when another window is clicked the island keeps showing but no longer has the keyboard", released && stays,
            $"released {released}, phase {m.Phase}, page {m.PageId}");

        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island leaving after its idle time", hangLimit, report);
        report.Check("the island did not take the keyboard back from the window the person chose", Native.GetForegroundWindow() == before,
            "foreground same as the window the person clicked");
    }

    /// <summary>A click on the capsule counts as use but never takes the keyboard.</summary>
    private async Task ClicksDoNotTakeIt(IslandRuntime rt, IntPtr before)
    {
        var c = rt.Controller;
        var m = c.Machine;
        OutsideForeground.BringForward(before);
        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Open, "the island open for the click check", hangLimit, report);
        c.ItemClick(1);
        c.Activity();
        report.Check("a click on the island does not take the keyboard", !m.HasKeyboard && Native.GetForegroundWindow() == before, "foreground same");
        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden after the click check", hangLimit, report);
    }
}
