using System.IO;
using System.Windows.Controls.Primitives;
using Island.Core;

namespace Island.App;

/// <summary>
/// Dan's tutorial of the first start (WORK-ORDER-13, DECISIONS 8 Oct 2026), on the self-test's own windows, invented programs and a temporary picks file, with no real key press: the keys are given to the
/// handler a key reaches. The balloon says the first step; the main key, Right, Enter, Tab, Down, Shift+Enter, Delete twice and Esc each finish their step in turn; while practising Enter starts nothing,
/// Shift+Enter adds nothing and the second Delete removes nothing; the island stays open past its idle time; "Skip step" and "Skip tutorial" work with the mouse; an island closed by mistake is told how
/// to come back; at the end everything is put back and the island is closed. The balloon is drawn into a window that is never shown here.
/// </summary>
internal sealed class PracticeStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    private const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Tab = 0x09, Enter = 0x0D, Delete = 0x2E, Escape = 0x1B;

    public async Task RunAsync()
    {
        var picksPath = Path.Combine(tempRoot, "practice-stage", "picks.json");
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
        using var rt = new IslandRuntime(2, pageSource, book, takeKeyboard: false); // an idle time of two seconds: the practice holds the island past it
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;
        var removed = 0;
        c.RemoveRequested += _ => removed++;
        var activated = 0;
        c.ItemActivated += _ => activated++;

        using var practice = new PracticeSession(rt, "Ctrl+Q");
        var ended = 0;
        practice.Ended += () => ended++;
        practice.Start();
        var run = practice.Run;
        var balloon = practice.Balloon;
        report.Check("the practice starts with the first step in the balloon and the island set to practise",
            run.Index == 0 && balloon.TextNow == "Press Ctrl+Q" && balloon.CountNow == $"Step 1 of {run.Count}" && c.Practice && m.HoldOpen,
            $"step {run.Index + 1}: \"{balloon.TextNow}\", {balloon.CountNow}, practice {c.Practice}, hold {m.HoldOpen}");

        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open after the main key", hangLimit, report);
        report.Check("the main key finishes the first step", run.Index == 1 && balloon.TextNow.StartsWith("Press →"), $"step {run.Index + 1}");

        await Task.Delay(3000); // longer than the idle time of two seconds
        report.Check("the island stays open past its idle time while the person practises", m.Phase == IslandPhase.Open && run.Index == 1, $"phase {m.Phase}");

        c.PageKey(PageIds.Apps);
        await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Apps, "the Apps page at rest", hangLimit, report);
        var calls = recording.Calls.Count;
        c.HandleKey(Right);
        report.Check("Right finishes the second step", run.Index == 2, $"step {run.Index + 1}");
        c.HandleKey(Enter);
        report.Check("Enter on a tile finishes the third step and starts nothing", run.Index == 3 && recording.Calls.Count == calls && activated == 0, $"step {run.Index + 1}, {recording.Calls.Count - calls} calls, {activated} activations");
        c.HandleKey(new KeyInput(Tab));
        await Waiter.UntilAsync(() => m.IsAtRest, "the next page at rest", hangLimit, report);
        report.Check("Tab finishes the fourth step", run.Index == 4, $"step {run.Index + 1}");

        c.PageKey(PageIds.Apps);
        await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Apps, "the Apps page at rest again", hangLimit, report);
        c.HandleKey(Down);
        await Waiter.UntilAsync(() => m.SecondRowOpen && m.SecondRowSelected == 0, "the second row opened by Down with its first tile selected", hangLimit, report);
        report.Check("Down finishes the fifth step", run.Index == 5, $"step {run.Index + 1}");
        var pickCount = book.Store.Picks.Count;
        c.HandleKey(new KeyInput(Enter, Shift: true));
        report.Check("Shift+Enter on a tile of the second row finishes the sixth step and adds nothing", run.Index == 6 && book.Store.Picks.Count == pickCount, $"step {run.Index + 1}, {book.Store.Picks.Count} picks");
        c.HandleKey(Up);
        c.HandleKey(Delete);
        report.Check("Delete on a tile finishes the seventh step", run.Index == 7, $"step {run.Index + 1}");
        c.HandleKey(Delete);
        report.Check("the second Delete finishes the eighth step and removes nothing", run.Index == 8 && removed == 0 && book.Store.Picks.Count == pickCount, $"step {run.Index + 1}, {removed} removals, {book.Store.Picks.Count} picks");

        // Esc on the way to the last step: the island closes by mistake at step 9? No: Esc IS step 9. A different run below checks the mistake.
        c.HandleKey(Escape);
        var closed = await Waiter.UntilAsync(() => ended == 1, "the practice ended by the last Esc", hangLimit, report);
        report.Check("Esc closes the island and ends the practice: everything is put back", closed && run.IsDone && !c.Practice && !m.HoldOpen && m.Phase is IslandPhase.Closing or IslandPhase.Hidden,
            $"ended {ended}, practice {c.Practice}, hold {m.HoldOpen}, phase {m.Phase}");
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden", hangLimit, report);

        await SkipsAsync(rt, book);
    }

    private async Task SkipsAsync(IslandRuntime rt, PickBook book)
    {
        var c = rt.Controller;
        var m = c.Machine;

        // "Skip step" with the mouse, then an island closed by mistake, then "Skip tutorial" with the island up.
        using var practice = new PracticeSession(rt, "Ctrl+Q");
        var ended = 0;
        practice.Ended += () => ended++;
        practice.Start();
        var run = practice.Run;
        var balloon = practice.Balloon;
        balloon.SkipStepButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
        report.Check("\"Skip step\" goes to the next step without the island being touched", run.Index == 1 && m.Phase == IslandPhase.Hidden && c.Practice, $"step {run.Index + 1}, island {m.Phase}");

        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open", hangLimit, report);
        report.Check("the main key does not finish a step that is not the first (the person skipped it)", run.Index == 1, $"step {run.Index + 1}");

        c.HandleKey(Escape);
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island closed by Esc", hangLimit, report);
        report.Check("an island closed by mistake is told how to come back and the step waits", run.Index == 1 && balloon.HintNow.Contains("Press Ctrl+Q to bring it back"), $"step {run.Index + 1}: \"{balloon.HintNow}\"");
        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open again", hangLimit, report);
        report.Check("and the words go when it is back", !balloon.HintNow.Contains("bring it back"), $"\"{balloon.HintNow}\"");

        balloon.SkipTutorialButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
        await Waiter.UntilAsync(() => ended == 1 && m.Phase is IslandPhase.Closing or IslandPhase.Hidden, "the practice skipped with the island up", hangLimit, report);
        report.Check("\"Skip tutorial\" ends the practice at once, puts everything back and closes the island",
            ended == 1 && run.IsDone && !c.Practice && !m.HoldOpen && m.Phase is IslandPhase.Closing or IslandPhase.Hidden, $"ended {ended}, practice {c.Practice}, phase {m.Phase}");
        report.Check("a practice that was skipped changed no pick", book.Store.Picks.Count == 3, $"{book.Store.Picks.Count} picks");
    }
}
