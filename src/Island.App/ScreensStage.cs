using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 section 1: after a summon the island's windows really are where Island.Core says they go on this laptop's screen
/// (centred on the work area's width, at its top), the number of screens goes into selftest.json (a count, nothing else), and
/// with one screen the two-screen checks are recorded as "NO-VERIFIER — one screen". The tests of the choice itself, with invented
/// screen layouts, are ScreenChoiceTests; this stage proves the wiring.
/// </summary>
internal sealed class ScreensStage(SelfTestReport report, TimeSpan hangLimit)
{
    public async Task RunAsync()
    {
        using var rt = new IslandRuntime(30);
        rt.Show();
        await Task.Delay(150);
        var placer = rt.Placer!;
        var c = rt.Controller;
        var m = c.Machine;

        await Waiter.UntilAsync(() => placer.ScreenCount > 0, "the screens read", hangLimit, report);
        report.Info["screens"] = placer.ScreenCount;
        report.Check("at least one screen was read", placer.ScreenCount >= 1, $"{placer.ScreenCount} screen(s)");

        c.ShowHide();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open", hangLimit, report);

        var expected = ScreenChooser.Choose(placer.Reader.Pointer, placer.Reader.Screens, rt.Host.WidthDip, rt.Host.HeightDip);
        var capsule = rt.Host.Capsule.RealRectangle;
        var shadow = rt.Host.Shadow.RealRectangle;
        report.Check("after a summon the capsule window and the shadow window are where Island.Core says: centred on the work area, at its top",
            !expected.IsFallback && capsule == expected.Window && shadow == expected.Window, capsule == expected.Window ? "same rectangle" : "different rectangle");

        if (placer.ScreenCount < 2) report.Info["twoScreenChecks"] = "NO-VERIFIER — one screen";
        report.Info["placementChoices"] = placer.Choices;

        // A screen narrower than the island shows fewer picks at once, and the capsule then fits it; the real screen brings seven back.
        var narrow = new ScreenInfo(new PixelRect(0, 0, 640, 480), new PixelRect(0, 0, 640, 440), 1.0, IsPrimary: true);
        var twelve = new PageContents(Pages.Get(PageIds.Apps), [.. Enumerable.Range(0, 12).Select(i => new Item($"P{i}", "open", "Pk", i, PickId: $"program:p{i}")), new Item("Add", "", "+", 0, IsPlus: true)]);
        ScreenPlacer.ApplyLimit(narrow);
        var lowered = StripLayout.VisibleLimit;
        var fitsNarrow = CapsuleLayout.SizeFor(twelve).Width <= narrow.WorkWidthDip;
        ScreenPlacer.ApplyLimit(expected.Screen);
        var restored = StripLayout.VisibleLimit;
        report.Check("a screen narrower than the island shows fewer picks and the capsule fits it; the real screen shows seven",
            lowered < ChoiceConstants.MaxVisibleTiles && lowered >= 1 && fitsNarrow && restored == ChoiceConstants.MaxVisibleTiles, $"{lowered} picks on the narrow screen, {restored} on this one");

        // While the island is visible the placement is not chosen again.
        var choices = placer.Choices;
        c.PageKey(PageIds.Folders);
        await Waiter.UntilAsync(() => m.IsAtRest, "the page switched", hangLimit, report);
        report.Check("a visible island is not placed again", placer.Choices == choices, $"{placer.Choices - choices} new choice(s)");
    }
}
