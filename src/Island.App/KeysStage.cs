using System.IO;
using System.Windows;
using System.Windows.Interop;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-10 §2 with no real key press: after a main-key summon, the keys are given to the handler a key reaches (<see cref="IslandController.HandleKey(KeyInput)"/>),
/// on the self-test's own windows, invented programs and a temporary picks file. Right twice and Enter ask the gate for the third pick's click; Tab goes round the pages;
/// Down opens the second row and Up closes it; Delete twice removes one pick and closes nothing (the self-test's own test window is still open at the end); with Ctrl held
/// the same keys do nothing. The foreground window at the end is the one from before, or foregroundGranted: false.
/// </summary>
internal sealed class KeysStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    private const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Tab = 0x09, Enter = 0x0D, Delete = 0x2E;

    public async Task RunAsync()
    {
        var other = new Window { Title = "Island self-test: stand-in for another program", Width = 240, Height = 90, Left = 40, Top = 400, WindowStartupLocation = WindowStartupLocation.Manual };
        other.Show();
        var before = new WindowInteropHelper(other).EnsureHandle();
        await Task.Delay(150);
        OutsideForeground.BringForward(before);
        var granted = Native.GetForegroundWindow() == before;
        report.Info["foregroundGrantedForKeys"] = granted;

        var picksPath = Path.Combine(tempRoot, "keys-stage", "picks.json");
        var picks = new[] { "alpha", "beta", "gamma" }.Select(n => Pick.ForProgram(char.ToUpperInvariant(n[0]) + n[1..], PageIds.Apps, n + ".exe", null)).ToArray();
        var pretend = new PretendWorld
        {
            Windows = [new OpenWindow(1, "alpha.exe", null, "t", 0), new OpenWindow(2, "beta.exe", null, "t", 1), new OpenWindow(3, "gamma.exe", null, "t", 2), new OpenWindow(4, "delta.exe", null, "t", 3)],
            Installed = [new InstalledProgram("Delta Tool", "delta.exe", null, "launch")],
        };
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var book = new PickBook(new PickStore(picks), picksPath, canSave: true);
        var pageSource = new PickPages(() => book.Store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(30, pageSource, book, takeKeyboard: false); // the keys are tested here; taking and returning the keyboard is KeyboardStage's
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;

        try
        {
            c.MainKey();
            await Waiter.UntilAsync(() => m.IsAtRest, "the island open after the main key", hangLimit, report);
            report.Check("the main key gives the island the keyboard", m.HasKeyboard, $"keyboard {m.HasKeyboard}");
            c.PageKey(PageIds.Apps);
            await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Apps, "the Apps page at rest", hangLimit, report);
            var start = m.SelectedItem;

            // Right twice and Enter: the third pick's click, asked of the gate.
            var calls = recording.Calls.Count;
            c.HandleKey(Right);
            c.HandleKey(Right);
            report.Check("two Right keys move the selection two tiles along", m.SelectedItem == start + 2, $"selected {m.SelectedItem}");
            c.HandleKey(Enter);
            report.Check("Enter does what a click on the selected tile does: it asks the gate to bring the third pick's window forward",
                recording.Calls.Count == calls + 1, $"{recording.Calls.Count - calls} request");

            // The ends: more Right keys stop at the + tile, Left keys at the first tile.
            for (var i = 0; i < 6; i++) c.HandleKey(Right);
            var last = m.ContentsItems.Count - 1;
            report.Check("the selection stops at the + tile and does not wrap", m.SelectedItem == last && m.ContentsItems[last].IsPlus, $"selected {m.SelectedItem} of {m.ContentsItems.Count}");
            for (var i = 0; i < 8; i++) c.HandleKey(Left);
            report.Check("and at the first tile going back", m.SelectedItem == 0, $"selected {m.SelectedItem}");

            // Down opens the second row and goes into it, onto its one tile (a fourth program is open and not picked); Enter jumps to it once and adds nothing;
            // Shift+Enter adds it; Up goes back and closes the row.
            c.HandleKey(Down);
            await Waiter.UntilAsync(() => m.SecondRowOpen && m.SecondRowSelected == 0, "the second row opened by Down with its first tile selected", hangLimit, report);
            report.Check("Down opens the second row and the selection goes into it, onto the first tile", m.SecondRowOpen && m.SecondRowSelected == 0, $"row open {m.SecondRowOpen}, selected {m.SecondRowSelected}");
            var picksInRow = book.Store.Picks.Count;
            var callsInRow = recording.Calls.Count;
            c.HandleKey(Enter);
            report.Check("Enter on a tile of the second row jumps to it once and adds nothing", recording.Calls.Count == callsInRow + 1 && book.Store.Picks.Count == picksInRow, $"{recording.Calls.Count - callsInRow} request, {book.Store.Picks.Count} picks");
            c.HandleKey(new KeyInput(Enter, Shift: true));
            report.Check("Shift+Enter on it adds it to the page", book.Store.Picks.Count == picksInRow + 1, $"{book.Store.Picks.Count} picks");
            c.HandleKey(Up);
            report.Check("Up goes back to the first row, closes the second row and leaves the island open", !m.SecondRowOpen && m.SecondRowSelected == -1 && m.Phase is IslandPhase.Open or IslandPhase.FlyingIn, $"row open {m.SecondRowOpen}, phase {m.Phase}");
            await Waiter.UntilAsync(() => m.IsAtRest, "the page at rest again", hangLimit, report);

            // Tab goes to the next page, Shift+Tab back; the wrap is the pure rule's.
            var page = m.PageId;
            c.HandleKey(new KeyInput(Tab));
            report.Check("Tab goes to the next page", m.PageId != page, $"page changed: {m.PageId != page}");
            c.HandleKey(new KeyInput(Tab, Shift: true));
            report.Check("Shift+Tab goes back to the page before", m.PageId == page, $"back on the same page: {m.PageId == page}");
            await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Apps, "the Apps page at rest again", hangLimit, report);

            // With Ctrl held, none of these keys acts.
            var selected = m.SelectedItem;
            c.HandleKey(new KeyInput(Right, Ctrl: true));
            report.Check("with Ctrl held an arrow does nothing", m.SelectedItem == selected, $"selected {m.SelectedItem}");

            // Delete twice removes the selected pick, and closes nothing.
            while (m.SelectedItem > 0) c.HandleKey(Left);
            var removed = m.ContentsItems[0].PickId;
            var picksBefore = book.Store.Picks.Count;
            var callsBefore = recording.Calls.Count;
            c.HandleKey(Delete);
            await Task.Delay(120);
            report.Check("a first Delete names the pick and asks to press it again", m.PendingDeleteId == removed && rt.View.Contents.SubtitleText == IslandController.DeleteAgainNote && rt.View.Contents.TitleText == m.ContentsItems[0].Title,
                $"pending {m.PendingDeleteId is not null}, line: {rt.View.Contents.SubtitleText}");
            c.HandleKey(new KeyInput(Delete, IsRepeat: true));
            report.Check("a held Delete never removes", book.Store.Picks.Count == picksBefore, $"{book.Store.Picks.Count} picks");
            c.HandleKey(Delete);
            report.Check("the second Delete removes that pick and leaves one pick fewer", book.Store.Picks.Count == picksBefore - 1 && book.Store.ById(removed!) is null, $"{book.Store.Picks.Count} picks");
            report.Check("nothing that is open was closed or touched by the removal, and the self-test's own test window is still open",
                recording.Calls.Count == callsBefore && other.IsVisible, $"{recording.Calls.Count - callsBefore} requests; test window visible: {other.IsVisible}");

            await Task.Delay(150);
            var back = Native.GetForegroundWindow() == before;
            report.Check("after the keys the foreground window is the one from before, or foregroundGranted: false", back || !granted, granted ? (back ? "same" : "different") : "foregroundGranted: false");
            if (!granted)
                report.NeedsHumanVerify.Add("Windows did not let the self-test bring its stand-in window forward (foregroundGranted: false), so the foreground after the island's keys was not checked: Ctrl+Q, a few arrows, Esc, and see that you are back where you were.");
        }
        finally
        {
            other.Close();
        }
    }
}
