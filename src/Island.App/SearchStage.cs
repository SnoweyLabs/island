using System.IO;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-7 section 3, with no real key press, through the handlers a key reaches: after a main-key summon the text handler with "tu" on a temporary
/// picks list that has picks named "Tunes" and "Tutorial" shows two tiles with "Tunes" selected; Enter's handler is refused by the gate for a program that
/// is not the self-test's own, and counted; Esc twice returns to the page; the foreground window at the end is the one from before. The only text ever typed
/// here is "tu". The keyboard grant itself is pretended (it depends on whom the person last clicked): whether Windows gives the island the keyboard for
/// search is a check for a person. A snapshot goes to review/choices/search.png.
/// </summary>
internal sealed class SearchStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    public async Task RunAsync()
    {
        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var tunes = Pick.ForProgram("Tunes", PageIds.Apps, "tunes.exe", null);
        var tutorial = Pick.ForProgram("Tutorial", PageIds.Apps, "tutorial.exe", null);
        var book = new PickBook(new PickStore([tunes, tutorial]), null, canSave: false);
        var pages = new PickPages(() => book.Store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages, book);
        rt.Keyboard.PretendGranted = true;
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;
        var search = rt.Search!;
        var before = Native.GetForegroundWindow();

        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open and at rest after the main key", hangLimit, report);
        report.Check("the main key brings the island with the keyboard", m.HasKeyboard && !m.SearchOpen, $"keyboard {m.HasKeyboard}");

        // A character that is not a digit opens search with that character in it; the next goes after it.
        c.HandleText("t");
        c.HandleText("u");
        await Waiter.UntilAsync(() => m.SearchOpen && m.IsAtRest && rt.View.Search.FieldText == "tu", "search open with the text at rest", hangLimit, report);
        report.Check("typed text opens search and goes into the field", m.SearchOpen && rt.View.Search.FieldText == "tu", $"search open: {m.SearchOpen}");
        // Dan's three tiles (version 1.0.1): YouTube, Google and this computer after the matches, always. Until then: the two matches and Google's tile.
        report.Check("\"tu\" shows its two matches and the three tiles YouTube, Google and this computer, Tunes selected", search.TileCount == 5 && search.Selected == 0 && rt.View.Search.Lines.Title == "Tunes" && rt.View.Search.TilesDrawn == 5,
            $"{search.TileCount} tiles, the first named {rt.View.Search.Lines.Title}");
        report.Check("the text block says Enter opens it", rt.View.Search.Lines.Subtitle == "Enter to open", rt.View.Search.Lines.Subtitle);
        report.Check("the capsule's width follows the search layout", Math.Abs(m.Width.Target - SearchLayout.Width(2, 5)) < 0.01, $"{m.Width.Target:0.#} wide");

        // Right and Left move the selection.
        c.HandleKey(0x27);
        await Task.Delay(120);
        var moved = search.Selected == 1 && rt.View.Search.Lines.Title == "Tutorial";
        c.HandleKey(0x25);
        await Task.Delay(120);
        report.Check("Right and Left move the selection", moved && search.Selected == 0, "Tunes, Tutorial, Tunes");

        // Enter does what a click on the selected tile does: that pick is closed, so it would be started, which the gate refuses and counts.
        c.HandleKey(0x27);
        await Task.Delay(120);
        var refusedBefore = OutsideGate.Current.Refused(OutsideKind.StartProgram);
        c.HandleKey(0x0D);
        await Task.Delay(200);
        report.Check("Enter's handler is refused by the gate for a program that is not the self-test's own, and counted",
            OutsideGate.Current.Refused(OutsideKind.StartProgram) == refusedBefore + 1, $"refused {OutsideGate.Current.Refused(OutsideKind.StartProgram) - refusedBefore}");
        await Waiter.UntilAsync(() => !m.SearchOpen && m.IsAtRest, "the page back after Enter", hangLimit, report);
        report.Check("after Enter search is over and the page shows again", !m.SearchOpen && !search.IsOpen, "the page's row is back");

        // Esc twice: the text first, then search itself.
        c.HandleText("t");
        c.HandleText("u");
        await Waiter.UntilAsync(() => m.SearchOpen && m.IsAtRest && rt.View.Search.FieldText == "tu", "search open again", hangLimit, report);
        c.HandleKey(0x1B);
        await Task.Delay(150);
        var cleared = m.SearchOpen && rt.View.Search.FieldText.Length == 0;
        c.HandleKey(0x1B);
        await Waiter.UntilAsync(() => !m.SearchOpen && m.IsAtRest, "the page back after the second Esc", hangLimit, report);
        report.Check("Esc clears the text first, then leaves search and returns to the page", cleared && !m.SearchOpen && m.Phase == IslandPhase.Open, $"cleared first: {cleared}");

        // The digits keep switching pages while the field is empty.
        c.HandleKey(0x33);
        await Task.Delay(150);
        report.Check("with search closed a digit still switches the page", m.PageId == Pages.BuiltIn[2].Id, m.PageId);

        report.Check("the foreground window at the end is the one from before, or foregroundGranted: false", Native.GetForegroundWindow() == before, "search took nothing");

        Snapshot();
    }

    private void Snapshot()
    {
        var data = new SearchViewData("tu",
            [
                new Item("Tunes", "closed", "Tu", 200, PickId: "program:tunes", IsClosed: false),
                new Item("Tutorial", "closed", "Tu", 140, PickId: "program:tutorial", IsClosed: true),
                new Item("Search tu on YouTube", "Enter to open", "YT", 200),
            ],
            Selected: 0, NeedsClick: false, Title: "Tunes", Subtitle: "Enter to open", ServiceTiles: 1);
        var scene = new OffscreenScene(new Rgb(27, 31, 58));
        var colour = Rgb.FromHex(LookConstants.AppsColor);
        scene.RenderSearch(data, colour, 2).SavePng(Path.Combine(folder, "choices", "search.png"));
        report.Check("search with two matches and the service's tile was drawn into review/choices/search.png", File.Exists(Path.Combine(folder, "choices", "search.png")), "the text \"tu\" only; a snapshot proves it draws, not that it looks right");
    }
}
