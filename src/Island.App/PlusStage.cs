using System.IO;
using System.Text.RegularExpressions;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-4 section 1 and WORK-ORDER-5 section 5 checks, on simulated windows with made-up program names and a temporary
/// picks file: the + opens a second row that holds what is open and not picked (the second program, not the picked first
/// one); a click on a tile jumps once and adds nothing; adding puts it on the page and it leaves the row; a second click on
/// the + and Esc close it. (Removing a pick is the drag of WORK-ORDER-5 §6, built in DragStage.) A snapshot with placeholder
/// names only goes to review/choices.
/// </summary>
internal sealed class PlusStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot, string folder)
{
    public async Task RunAsync()
    {
        var picksPath = Path.Combine(tempRoot, "plus-stage", "picks.json");
        var alpha = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var pretend = new PretendWorld
        {
            Windows = [new OpenWindow(1, "alpha.exe", null, "t", 0), new OpenWindow(2, "beta.exe", null, "t", 1), new OpenWindow(3, "beta.exe", null, "t", 2)],
            Installed = [new InstalledProgram("Beta Tool", "beta.exe", null, "launch")],
        };
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var book = new PickBook(new PickStore([alpha]), picksPath, canSave: true);
        var pages = new PickPages(() => book.Store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(30, pages, book);
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;

        c.PageKey(PageIds.Apps);
        await Waiter.UntilAsync(() => m.IsAtRest, "the Apps page open and at rest", hangLimit, report);
        var items = m.ContentsItems;
        report.Check("the last tile of the page is a + and the pick comes before it", items.Count == 2 && items[1].IsPlus && items[0].PickId == alpha.Id, $"{items.Count} tiles");

        // The + tile opens the second row: the capsule grows into two rows through the height spring.
        c.TogglePlusRow();
        var grown = await Waiter.UntilAsync(() => m.IsAtRest && Math.Abs(m.Height.Value - ChoiceConstants.TwoRowHeight) < 0.1, "the capsule two rows high", hangLimit, report);
        report.Check("a click on the + tile opens the second row and the capsule grows to two rows", m.SecondRowOpen && grown, $"drawn height {m.Height.Value:0.#}");
        await Task.Delay(300); // the row's contents fade in
        // The room round the two-row capsule still belongs to other windows (the island's window holds the whole shape, and lets every click through but the capsule's own).
        Native.GetWindowRect(rt.Host.Capsule.Handle, out var rect2);
        var scale2 = System.Windows.Media.VisualTreeHelper.GetDpi(rt.Host.Capsule).DpiScaleX;
        int Px2(double dip) => (int)Math.Round(dip * scale2);
        var cx = rect2.Left + (rect2.Right - rect2.Left) / 2;
        var top2 = rect2.Top + Px2(LookConstants.TopGap);
        bool Ours2(Native.Point point) => Native.ProcessIdOf(Native.WindowFromPoint(point)) == (uint)Environment.ProcessId;
        var middle2 = new Native.Point(cx, top2 + Px2(m.Height.Value / 2));
        var below2 = new Native.Point(cx, top2 + Px2(m.Height.Value) + Px2(8));
        var beside2 = new Native.Point(cx + Px2(m.Width.Value / 2) + Px2(14), top2 + Px2(m.Height.Value / 2));
        report.Check("with the second row open the middle of the two-row capsule belongs to this process, the room below it and beside it to other windows",
            Ours2(middle2) && !Ours2(below2) && !Ours2(beside2), $"middle {Ours2(middle2)}, below {Ours2(below2)}, beside {Ours2(beside2)}");

        var second = rt.SecondRow!;
        report.Check("the second row holds the second program and not the picked first one", second.Keys.SequenceEqual(["program:beta.exe"]) && rt.View.Contents.SecondRowState.Tiles == 1 && rt.View.Contents.SecondRowIsShown,
            "made-up names");
        report.Check("the + tile reads \"Add something\" and how many things are open", m.ContentsItems[1].Title == PlusRow.AddTitle && m.ContentsItems[1].Subtitle == PlusRow.AddSubtitle(1), m.ContentsItems[1].Subtitle);
        SaveSecondRowSnapshot();

        // A click on a tile jumps to that thing once and adds nothing.
        var picksBefore = book.Store.Picks.Count;
        second.Jump(0);
        report.Check("a click on a tile of the second row jumps to it once and adds nothing", recording.Calls.Count == 1 && book.Store.Picks.Count == picksBefore, $"{recording.Calls.Count} request, {book.Store.Picks.Count} picks");

        // Pointing at a tile names it in the text block.
        rt.View.Contents.ShowHoverText("Beta Tool", PlusRow.HoverSubtitle);
        report.Check("pointing at a tile puts its name in the text block with \"open · not on the island\" under it",
            rt.View.Contents.TitleText == "Beta Tool" && rt.View.Contents.SubtitleText == PlusRow.HoverSubtitle, rt.View.Contents.SubtitleText);
        rt.View.Contents.ShowHoverText(null, null);

        // Add (the small +): the entry goes on the page being shown, leaves the second row and appears in the first.
        second.Add(0);
        await Waiter.UntilAsync(() => m.ContentsItems.Count == 3, "the page showing the added pick", hangLimit, report);
        await Task.Delay(200);
        report.Check("adding puts the program on the page being shown and it leaves the second row",
            book.Store.ById("program:beta") is { PageId: PageIds.Apps } && second.Keys.Count == 0 && m.ContentsItems.Count == 3 && m.SecondRowOpen,
            $"{m.ContentsItems.Count} tiles, on page: {book.Store.ById("program:beta")?.PageId}, row keys {second.Keys.Count}, row open {m.SecondRowOpen}");
        var saved = File.ReadAllText(picksPath);
        report.Check("the picks file holds the new pick by name and executable, never a path",
            saved.Contains("beta.exe", StringComparison.Ordinal) && !Regex.IsMatch(saved, @"[A-Za-z]:\\|\\\\"), "temporary picks file");

        // A second click on the + closes the row; a click on a pick closes it too.
        c.TogglePlusRow();
        report.Check("a second click on the + closes the second row", !m.SecondRowOpen, $"row open {m.SecondRowOpen}");
        c.TogglePlusRow();
        c.CloseSecondRow();
        report.Check("the second row can be closed by a click on a pick (the runtime does it before the pick's own action)", !m.SecondRowOpen, $"row open {m.SecondRowOpen}");
        await Waiter.UntilAsync(() => m.IsAtRest, "the page at rest again", hangLimit, report);

        // Esc closes the row first and the island next (only while the island has the keyboard).
        if (m.HasKeyboard)
        {
            c.TogglePlusRow();
            c.HandleKey(0x1B);
            var rowClosed = !m.SecondRowOpen && m.Phase is IslandPhase.Open or IslandPhase.FlyingIn;
            report.Check("Esc closes the second row and leaves the island open", rowClosed, $"phase {m.Phase}");
        }

    }

    /// <summary>The capsule two rows high with invented names, for review/choices/plus-row.png.</summary>
    private void SaveSecondRowSnapshot()
    {
        var page = Pages.Get(PageIds.Apps);
        var contents = new PageContents(page, [new Item("Alpha", "open", "Al", 215), new Item("Beta", "closed", "Be", 20, IsClosed: true), new Item(PlusRow.AddTitle, PlusRow.AddSubtitle(3), "+", 0, IsPlus: true)]);
        RowTile[] row =
        [
            new("program:gamma.exe", new Item("Gamma", PlusRow.HoverSubtitle, "Ga", 130)),
            new("program:delta.exe", new Item("Delta", PlusRow.HoverSubtitle, "De", 300)),
            new("program:epsilon.exe", new Item("Epsilon", PlusRow.HoverSubtitle, "Ep", 60)),
        ];
        var scene = new OffscreenScene(new Rgb(27, 31, 58));
        scene.Render(OffscreenScene.RestFrame(contents, 0.3, twoRows: true), contents, 2, secondRow: row).SavePng(Path.Combine(folder, "choices", "plus-row.png"));
        report.Check("plus-row.png was drawn with invented names only", File.Exists(Path.Combine(folder, "choices", "plus-row.png")), "review/choices");
    }
}
