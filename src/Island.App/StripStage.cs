using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-5 §4: a page of twelve picks slides. A temporary picks file with twelve picks on one page, the second of which
/// has two (invented) windows: the capsule is as wide as the width rule says for seven picks and the + tile, that pick still
/// shows its two dots, and after the wheel handler has been given five full notches and the spring has settled the first pick
/// is not drawn and the twelfth is. Pretend readers only; nothing of the machine is read or recorded but counts and yes/no.
/// </summary>
internal sealed class StripStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    public async Task RunAsync()
    {
        var picksPath = Path.Combine(tempRoot, "strip-stage", "picks.json");
        var picks = Enumerable.Range(1, 12).Select(i => Pick.ForProgram($"Program {i}", PageIds.Apps, $"strip{i}.exe", null)).ToList();
        report.Check("a temporary picks file with twelve picks on one page can be saved", new PickStore(picks).Save(picksPath), "temporary data folder");
        var store = PickStore.Load(picksPath).Store;

        var pretend = new PretendWorld
        {
            Windows = [new OpenWindow(2001, "strip2.exe", null, "invented", 0), new OpenWindow(2002, "strip2.exe", null, "invented", 1)],
        };
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var pages = new PickPages(() => store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(30, pages);
        rt.Show();
        await Task.Delay(150);

        var c = rt.Controller;
        var m = c.Machine;
        c.PageKey(PageIds.Apps);
        await Waiter.UntilAsync(() => m.IsAtRest, "the Apps page open and at rest", hangLimit, report);

        report.Check("the page has the twelve picks and the + tile", m.ContentsItems.Count == 13 && m.ContentsItems[^1].IsPlus, $"{m.ContentsItems.Count} items");
        report.Check("the capsule is as wide as the width rule says for seven picks and the + tile",
            Math.Abs(m.CapsuleTargetWidth - CapsuleLayout.Width(8, isMedia: false)) < 0.01, $"{m.CapsuleTargetWidth:0.##} wide, rule {CapsuleLayout.Width(8, isMedia: false):0.##}");

        var layer = rt.View.Contents;
        report.Check("the second pick still shows its two dots", layer.TileAt(1).DotCount == 2, $"{layer.TileAt(1).DotCount} dots");
        report.Check("at the start the first seven picks are drawn, with an arrow on the right only",
            layer.VisibleRange == (0, 6) && layer.ArrowsShown == (false, true), $"range {layer.VisibleRange}, arrows {layer.ArrowsShown}");

        for (var i = 0; i < 5; i++) c.Wheel(-StripScroll.WheelDelta); // five full notches toward the user
        var settled = await Waiter.UntilAsync(() => !c.Strip.Moving && m.IsAtRest, "the spring settled", hangLimit, report);
        await Task.Delay(100); // a frame or two with the final position

        var range = layer.VisibleRange;
        var firstDrawn = range.First <= 0 && 0 <= range.Last;
        var twelfthDrawn = range.First <= 11 && 11 <= range.Last;
        report.Check("after five notches and the settled spring the first pick is not drawn and the twelfth is",
            settled && !firstDrawn && twelfthDrawn && layer.ArrowsShown == (true, false), $"range {range}, arrows {layer.ArrowsShown}");
        report.Check("the capsule did not change width while the picks slid", Math.Abs(m.CapsuleTargetWidth - CapsuleLayout.Width(8, isMedia: false)) < 0.01, $"{m.CapsuleTargetWidth:0.##} wide");

        // An arrow click moves by the number of tiles shown (here limited by the end of the row).
        layer.RaiseArrowForSelfTest(right: false);
        await Waiter.UntilAsync(() => !c.Strip.Moving, "the spring settled after the left arrow", hangLimit, report);
        report.Check("the left arrow slides the picks back by the tiles shown, as far as the beginning", c.Strip.Target == 0, $"target {c.Strip.Target}");

        // A new summon starts at the beginning again.
        c.MainKey();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Closing || m.Phase == IslandPhase.Hidden, "the island leaving", hangLimit, report);
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden", hangLimit, report);
        c.PageKey(PageIds.Apps);
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open again", hangLimit, report);
        report.Check("a new summon starts the row at the beginning", layer.VisibleRange == (0, 6) && c.Strip.Target == 0, $"range {layer.VisibleRange}");
    }
}
