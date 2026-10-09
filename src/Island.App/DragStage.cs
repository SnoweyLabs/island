using System.IO;
using System.Windows;
using System.Windows.Interop;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-5 §6: dragging a pick off the island, proven by calling the same handlers a mouse reaches, with a temporary picks
/// file and the self-test's own test window (made-up names; no mouse input is faked): press, move, release over the drop zone
/// removes the pick and the test window still exists; press, move, release over the capsule changes nothing; Esc and losing the
/// mouse cancel and never remove; the + tile and every tile while the second row is open cannot be lifted; while a tile is lifted
/// the wheel does nothing. A snapshot of the lifted tile and the drop zone goes to review/choices/drag-off.png.
/// </summary>
internal sealed class DragStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot, string folder)
{
    public async Task RunAsync()
    {
        var ownExe = Path.GetFileName(Environment.ProcessPath) ?? "Island.App.exe";
        var picksPath = Path.Combine(tempRoot, "drag-stage", "picks.json");
        var alpha = Pick.ForProgram("Alpha", PageIds.Apps, ownExe, null);
        var beta = Pick.ForProgram("Beta", PageIds.Apps, "beta-drag.exe", null);
        var window = new Window { Title = "Island self-test window", Width = 200, Height = 80, Left = 40, Top = 560, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle().ToInt64();
            var pretend = new PretendWorld { Windows = [new OpenWindow(handle, ownExe, null, "test window", 0)] };
            var recording = new RecordingOutside();
            var world = AppWorld.Pretend(pretend, recording);
            var book = new PickBook(new PickStore([alpha, beta]), picksPath, canSave: true);
            var pages = new PickPages(() => book.Store, world, synchronousIcons: true);
            using var rt = new IslandRuntime(30, pages, book);
            rt.Show();
            await Task.Delay(150);
            var c = rt.Controller;
            var m = c.Machine;
            var layer = rt.View.Contents;
            var drag = rt.Drag!;
            var overlay = rt.View.Drag;

            c.PageKey(PageIds.Apps);
            await Waiter.UntilAsync(() => m.IsAtRest, "the Apps page open and at rest", hangLimit, report);
            Point Centre(int index) => new(layer.TileCentreX(index), LookConstants.TopGap + LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth);
            var start = Centre(0);

            // Press, move far enough, and the tile lifts: the drop zone shows and the text block says what happens.
            drag.OnPressed(0, start);
            drag.OnMoved(0, new Point(start.X, start.Y + 70));
            report.Check("a tile that is pressed and moved far enough lifts, the drop zone shows and the text block says so",
                drag.IsLifted && overlay.TileLifted && layer.TitleText == "Alpha" && layer.SubtitleText == "let go to remove", $"lifted {drag.IsLifted}, subtitle {layer.SubtitleText}");
            await Task.Delay(250);
            report.Check("the drop zone is under the capsule", overlay.ZoneShown && overlay.ZoneBounds.Top > m.DrawnY + m.DrawnHeight, $"zone top {overlay.ZoneBounds.Top:0}");
            SaveSnapshot(overlay);

            // While a tile is lifted the wheel does nothing.
            var targetBefore = c.Strip.Target;
            c.Wheel(-StripScroll.WheelDelta);
            report.Check("while a tile is lifted the wheel does nothing", c.Strip.Target == targetBefore, "target unchanged");

            // Let go over the drop zone: the pick is removed, nothing is closed, the test window is still there.
            var zone = overlay.ZoneBounds;
            drag.OnReleased(0, new Point(zone.Left + zone.Width / 2, zone.Top + zone.Height / 2));
            await Waiter.UntilAsync(() => m.ContentsItems.Count == 2, "the page without the dragged pick", hangLimit, report);
            report.Check("letting go over the drop zone removes the pick and closes nothing",
                book.Store.ById(alpha.Id) is null && book.Store.ById(beta.Id) is not null && window.IsLoaded && recording.Calls.Count == 0 && !drag.IsLifted && !overlay.TileLifted,
                $"{m.ContentsItems.Count} tiles, test window still open: {window.IsLoaded}, requests to the outside: {recording.Calls.Count}");
            report.Check("the picks file no longer holds it", !File.ReadAllText(picksPath).Contains(ownExe, StringComparison.Ordinal), "temporary picks file");
            await Waiter.UntilAsync(() => m.IsAtRest, "the page at rest", hangLimit, report);

            // Press, move, let go over the capsule: nothing changes.
            var itemsBefore = book.Store.Picks.Count;
            start = Centre(0);
            drag.OnPressed(0, start);
            drag.OnMoved(0, new Point(start.X + 30, start.Y + 2));
            var liftedOverCapsule = drag.IsLifted;
            drag.OnReleased(0, new Point(start.X + 30, start.Y + 2));
            await Task.Delay(400);
            report.Check("letting go over the capsule changes nothing and the tile is back",
                liftedOverCapsule && book.Store.Picks.Count == itemsBefore && layer.TileAt(0).Opacity > 0.99 && !overlay.TileLifted, $"picks {book.Store.Picks.Count}, tile opacity {layer.TileAt(0).Opacity:0.##}");

            // Esc cancels (the key handler's own path), and never removes.
            start = Centre(0);
            drag.OnPressed(0, start);
            drag.OnMoved(0, new Point(start.X, start.Y + 80));
            var wasLifted = drag.IsLifted;
            c.HandleKey(0x1B);
            await Task.Delay(400);
            report.Check("Esc cancels a drag and the island stays", wasLifted && !drag.IsLifted && book.Store.Picks.Count == itemsBefore && m.Phase is IslandPhase.Open or IslandPhase.FlyingIn,
                $"phase {m.Phase}, picks {book.Store.Picks.Count}");

            // Losing the mouse cancels and never removes.
            drag.OnPressed(0, start);
            drag.OnMoved(0, new Point(start.X, start.Y + 80));
            drag.OnMouseLost(0);
            await Task.Delay(400);
            report.Check("losing the mouse cancels and never removes", !drag.IsLifted && book.Store.Picks.Count == itemsBefore && layer.TileAt(0).Opacity > 0.99, $"picks {book.Store.Picks.Count}");

            // The + tile cannot be dragged: dragged away it is not a click (the row does not open); pressed and let go in place it is one.
            var plusIndex = m.ContentsItems.Count - 1;
            var plus = Centre(plusIndex);
            drag.OnPressed(plusIndex, plus);
            drag.OnMoved(plusIndex, new Point(plus.X, plus.Y + 90));
            var plusLifted = drag.IsLifted;
            drag.OnReleased(plusIndex, new Point(plus.X, plus.Y + 90));
            var awayDidNothing = !m.SecondRowOpen;
            drag.OnPressed(plusIndex, plus);
            drag.OnReleased(plusIndex, plus);
            await Waiter.UntilAsync(() => m.SecondRowOpen, "the second row opened by the click on the + tile", hangLimit, report);
            report.Check("the + tile cannot be lifted, dragged away it is no click, and a press and release in place is a click", !plusLifted && awayDidNothing && m.SecondRowOpen,
                $"lifted {plusLifted}, row opened by the drag away {!awayDidNothing}, row open after the click {m.SecondRowOpen}");

            // No pick can be lifted while the second row is open.
            start = Centre(0);
            drag.OnPressed(0, start);
            drag.OnMoved(0, new Point(start.X, start.Y + 90));
            var liftedWithRow = drag.IsLifted;
            drag.Cancel();
            report.Check("no pick can be lifted while the second row is open", !liftedWithRow, $"lifted {liftedWithRow}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The island with a tile lifted and the drop zone shown, with invented names, for review/choices/drag-off.png.</summary>
    private void SaveSnapshot(DragView live)
    {
        var page = Pages.Get(PageIds.Apps);
        var contents = new PageContents(page, [new Item("Alpha", "open", "Al", 215, PickId: "program:alpha"), new Item("Beta", "closed", "Be", 20, PickId: "program:beta", IsClosed: true), new Item(PlusRow.AddTitle, PlusRow.AddSubtitle(0), "+", 0, IsPlus: true)]);
        var scene = new OffscreenScene(new Rgb(27, 31, 58));
        var frame = OffscreenScene.RestFrame(contents, 0.3);
        scene.Render(frame, contents, 2);
        var tile = scene.Contents.TileAt(0);
        tile.SetLifted(true);
        var centre = new Point(scene.Contents.TileCentreX(0), LookConstants.TopGap + LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth);
        var home = new Point(centre.X - LookConstants.ItemSize / 2, centre.Y - LookConstants.ItemSize / 2);
        var pointer = new Point(centre.X + 30, centre.Y + 62);
        scene.Drag.Lift(contents.Items[0], Rgb.FromHex(page.Color), home, centre);
        scene.Drag.MoveTo(pointer);
        scene.Drag.ShowZone(new Rect(frame.CentreX - frame.Width / 2, frame.Top, frame.Width, frame.Height), immediate: true);
        scene.Contents.ShowHoverText("Alpha", "let go to remove");
        var path = Path.Combine(folder, "choices", "drag-off.png");
        scene.Capture(2).SavePng(path);
        report.Check("drag-off.png was drawn with invented names only", File.Exists(path) && live is not null, "review/choices");
    }
}
