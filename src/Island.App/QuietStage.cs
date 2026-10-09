using System.Windows;
using System.Windows.Interop;
using Island.Core;
using Island.Core.Terminals;

namespace Island.App;

/// <summary>
/// WORK-ORDER-11 section 4, "kept from coming back" and "the island must still be alive", on the self-test's pretend world (so a pointer that is moved cannot turn a check red).
/// In Focus, on a page of picks with nothing playing, at rest for one lap of the light, only the layers the moving light is drawn in (rim and bloom) are rendered again.
/// In DND, at rest, no frame is drawn at all; with no frame callback the island still leaves after its idle time, still notices that the keyboard went elsewhere, still refreshes
/// the playing line and the reading of the process list, and a session's state changing, a title changing or a tile coming wakes it. Invented names only.
/// </summary>
internal sealed class QuietStage(SelfTestReport report, TimeSpan hangLimit)
{
    private static readonly string[] MovingLightLayers = ["rim", "bloom"];

    private static readonly TerminalTables Tables = new(
        ["INVENTED_TERMINAL_CLASS"],
        [new TerminalProgramRow("alpha-term.exe", "Alpha Term")],
        [],
        [new HelperProgramRow("invented-helper.exe", TerminalConstants.ClaudeCode)],
        new Dictionary<string, HelperColor> { [TerminalConstants.ClaudeCode] = TerminalConstants.ClaudeCodeColor });

    private sealed class CountingProbe : ITerminalProbe
    {
        public int Reads { get; private set; }

        public TerminalProbeFacts Facts { get; set; } = TerminalProbeFacts.Empty;

        public TerminalProbeFacts Read()
        {
            Reads++;
            return Facts;
        }
    }

    public async Task RunAsync()
    {
        await FramesNeverExceedTheScreenAsync();
        await FixedRateDrawsTheLightAtMostFortyTimesASecondAsync();
        await FocusRendersOnlyTheLightAsync();
        await DndDrawsNothingAsync();
        await DndLeavesAfterIdleTimeAsync();
        await DndRefreshesThePlayingLineAsync();
        await DndSeesTheTerminalsAsync();
        await DndNoticesTheKeyboardGoingElsewhereAsync();
    }

    private static PickStore Picks() => new(
    [
        Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null), Pick.ForProgram("Gamma", PageIds.Apps, "gamma.exe", null),
        Pick.ForProgram("Alpha Player", PageIds.Media, "alpha-player.exe", null), Pick.ForSite("Example Site", "example.org", PageIds.Media),
    ]);

    private static PretendWorld World() => new()
    {
        Windows = [new OpenWindow(9001, "alpha.exe", null, "invented", 0), new OpenWindow(9002, "beta.exe", null, "invented", 1)],
    };

    private async Task<bool> UntilQuietAsync(IslandController c, string what) =>
        await Waiter.UntilAsync(() => c.IsQuiet, what, hangLimit, report);

    /// <summary>WORK-ORDER-12 section 2: on the Media page in Do not disturb with something playing (the playing tile moves, so the frame callback runs and a frame is cheap) the island draws no more frames than the screen refreshes.</summary>
    private async Task FramesNeverExceedTheScreenAsync()
    {
        var pretend = new PretendWorld { Sessions = [new MediaSessionInfo("s1", "alpha-player.exe", false, "Alpha track", "Beta artist", PlaybackState.Playing, 30, 3600, new ProgressReport(30, 3600, DateTimeOffset.UtcNow, true, 1))] };
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var store = new PickStore([Pick.ForProgram("Alpha Player", PageIds.Media, "alpha-player.exe", null), Pick.ForSite("Example Site", "example.org", PageIds.Media)]);
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(3600, pages);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        c.SetMode(Mode.DND, instant: true);
        c.PageKey(PageIds.Media);
        await Waiter.UntilAsync(() => c.Machine.IsAtRest && c.Machine.ContentsPageId == PageIds.Media, "the Media page open", hangLimit, report);
        c.MediaChanged();
        await Task.Delay(1500);
        // WORK-ORDER-12 section 4 (EASE): a screen reader is told in words what the island shows: the page, the selected tile with its second line and its place among the tiles.
        var said = rt.Host.Capsule.Root.Description;
        report.Check("the island says in words what it shows, for a screen reader: its window has a name, and its root names the page and the selected tile", rt.Host.Capsule.Title == "Island" && said.Contains("Media", StringComparison.Ordinal) && said.Contains("Alpha Player", StringComparison.Ordinal) && said.Contains(" of ", StringComparison.Ordinal),
            $"\"{said}\"");
        var hz = c.RefreshHz;
        var before = c.FramesDrawn;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(3000);
        var seconds = clock.Elapsed.TotalSeconds;
        var frames = c.FramesDrawn - before;
        report.Info["framesOnMediaInDnd"] = new { frames, seconds = Math.Round(seconds, 2), refreshHz = hz };
        if (hz <= 0)
        {
            report.NeedsHumanVerify.Add("Windows did not say how often this screen refreshes, so the island's cap on frames was not checked here (it then draws on every callback, as before).");
            return;
        }

        report.Check("on the Media page in Do not disturb with something playing, the island draws no more frames than the screen refreshes",
            c.RunsAtFrameRate && frames <= hz * seconds + Island.Core.Speed.FrameBudget.Capacity + 1, $"{frames} frames in {seconds:0.0} s on a screen of {hz:0} Hz ({hz * seconds:0} refreshes)");
    }

    /// <summary>WORK-ORDER-13 (Dan's P25), the fixed-rate light: at rest in Focus on a page of picks the rim and the bloom are rendered at most 40 times a second, and every other layer stands still.</summary>
    private async Task FixedRateDrawsTheLightAtMostFortyTimesASecondAsync()
    {
        var world = AppWorld.Pretend(World(), new OutsideSelfTestActions());
        var store = Picks();
        var pages = new PickPages(() => store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(3600, pages, null, null, takeKeyboard: false);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        c.Light = LightKind.FixedRate; // this stage asks for the kind it tests; every other island of the self-test draws the light as before
        c.SetMode(Mode.Focus, instant: true);
        c.MainKey();
        await Waiter.UntilAsync(() => c.Machine.IsAtRest, "the island open in Focus with the fixed-rate light", hangLimit, report);
        await Task.Delay(TimeSpan.FromSeconds(Math.Ceiling(Island.Core.Speed.FrameBudget.Capacity + 3)));
        var hz = c.RefreshHz;
        var before = rt.View.LayerRenders();
        var frames = c.FramesDrawn;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(3000);
        var seconds = clock.Elapsed.TotalSeconds;
        var after = rt.View.LayerRenders();
        var delta = after.Select((a, i) => (a.Name, Delta: a.Renders - before[i].Renders)).ToList();
        var light = delta.Where(d => MovingLightLayers.Contains(d.Name)).Select(d => d.Delta).DefaultIfEmpty(0).Max();
        var others = delta.Where(d => !MovingLightLayers.Contains(d.Name)).Sum(d => d.Delta);
        report.Info["fixedRateLight"] = new { refreshHz = hz, seconds = Math.Round(seconds, 2), lightRenders = light, otherRenders = others, frames = c.FramesDrawn - frames };
        if (hz <= 0)
        {
            report.NeedsHumanVerify.Add("Windows did not say how often this screen refreshes, so the fixed-rate light was not checked here.");
            return;
        }

        var limit = LightClock.FixedRateHz * seconds + Island.Core.Speed.FrameBudget.Capacity + 1;
        report.Check("with the fixed-rate light at rest in Focus, the rim and the bloom are rendered at most 40 times a second, and nothing else is rendered",
            light > 0 && light <= limit && others == 0, $"{light} renders of the light in {seconds:0.0} s on a screen of {hz:0} Hz (at most {limit:0}: 40 a second), {others} renders of every other layer");
    }

    private async Task FocusRendersOnlyTheLightAsync()
    {
        var world = AppWorld.Pretend(World(), new OutsideSelfTestActions());
        var store = Picks();
        var pages = new PickPages(() => store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(3600, pages, null, null, takeKeyboard: false);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        c.SetMode(Mode.Focus, instant: true);
        c.MainKey();
        await Waiter.UntilAsync(() => c.Machine.IsAtRest, "the island open in Focus", hangLimit, report);
        await Task.Delay(1500);

        var before = rt.View.LayerRenders();
        await Task.Delay(TimeSpan.FromSeconds(5.7)); // one lap of the light is 5.6 s
        var after = rt.View.LayerRenders();
        var rose = after.Select((a, i) => (a.Name, Delta: a.Renders - before[i].Renders)).Where(x => x.Delta > 0).Select(x => x.Name).ToList();
        report.Check("in Focus, on a page of picks with nothing playing, at rest for one lap of the light, only the layers the moving light is drawn in are rendered again",
            rose.Count > 0 && rose.All(n => MovingLightLayers.Contains(n)) && c.Machine.Phase == IslandPhase.Open,
            $"rendered again: {(rose.Count == 0 ? "none" : string.Join(", ", rose))}; every other layer stood still");
        report.Check("in Focus the frame callback runs (the light moves)", c.RunsAtFrameRate && !c.IsQuiet, $"runs {c.RunsAtFrameRate}");
    }

    private async Task DndDrawsNothingAsync()
    {
        var world = AppWorld.Pretend(World(), new OutsideSelfTestActions());
        var store = Picks();
        var pages = new PickPages(() => store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(3600, pages, null, null, takeKeyboard: false);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        c.SetMode(Mode.DND, instant: true);
        c.MainKey();
        await Waiter.UntilAsync(() => c.Machine.IsAtRest, "the island open in DND", hangLimit, report);
        var quiet = await UntilQuietAsync(c, "the DND capsule at rest leaving the frame callback");
        var frames = c.FramesDrawn;
        var renders = rt.View.LayerRenders();
        await Task.Delay(1500);
        var same = rt.View.LayerRenders().Select((r, i) => r.Renders == renders[i].Renders).All(x => x);
        report.Check("in DND, at rest: no frame is drawn at all (no frame callback, no layer rendered again)",
            quiet && !c.RunsAtFrameRate && c.FramesDrawn == frames && same && c.Machine.Phase == IslandPhase.Open, $"{c.FramesDrawn - frames} frames in 1.5 s");

        c.PageKey(PageIds.Media);
        report.Check("in DND, an input wakes the frame callback at once", c.RunsAtFrameRate && !c.IsQuiet, $"runs {c.RunsAtFrameRate}");
        await Waiter.UntilAsync(() => c.Machine.IsAtRest && c.Machine.ContentsPageId == PageIds.Media, "the Media page open", hangLimit, report);
        report.Check("in DND, the capsule is quiet again after the page changed and settled", await UntilQuietAsync(c, "the capsule quiet again"), "frame callback let go of again");

        // Dan's P24 (WORK-ORDER-13): the mouse moving over the capsule draws nothing and does not wake the frame callback; it only starts the idle time again.
        var framesBefore = c.FramesDrawn;
        var rendersBefore = rt.View.LayerRenders();
        for (var i = 0; i < 60; i++) c.PointerMoved();
        await Task.Delay(300);
        var rendersAfter = rt.View.LayerRenders();
        report.Check("in DND, a mouse move over the island draws no frame, renders no layer and does not wake the frame callback",
            c.FramesDrawn == framesBefore && c.IsQuiet && !c.RunsAtFrameRate && rendersAfter.Select((r, i) => r.Renders == rendersBefore[i].Renders).All(x => x), $"{c.FramesDrawn - framesBefore} frames after 60 moves");
    }

    private async Task DndLeavesAfterIdleTimeAsync()
    {
        var world = AppWorld.Pretend(World(), new OutsideSelfTestActions());
        var store = Picks();
        var pages = new PickPages(() => store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(6, pages, null, null, takeKeyboard: false); // quiet from about 2.5 s on, away at 6 s
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        c.SetMode(Mode.DND, instant: true);
        c.MainKey();
        var quiet = await UntilQuietAsync(c, "the DND capsule quiet before its idle time ends");
        var left = await Waiter.UntilAsync(() => c.Machine.Phase == IslandPhase.Hidden, "the quiet island leaving after its idle time", hangLimit, report);
        report.Check("in DND, with no frame callback the island still leaves after its idle time", quiet && left && !c.IsQuiet && !c.RunsAtFrameRate, $"was quiet {quiet}, left {left}");
    }

    private async Task DndRefreshesThePlayingLineAsync()
    {
        var pretend = World();
        pretend.Sessions = [Session("Alpha track")];
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var store = new PickStore([Pick.ForSite("Example Site", "example.org", PageIds.Media)]); // what plays belongs to no pick: no tile dances, nothing moves
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(3600, pages);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        c.SetMode(Mode.DND, instant: true);
        c.PageKey(PageIds.Media);
        await Waiter.UntilAsync(() => c.Machine.IsAtRest && c.Machine.ContentsPageId == PageIds.Media, "the Media page open", hangLimit, report);
        c.MediaChanged();
        await Waiter.UntilAsync(() => rt.View.Contents.TitleText == "Alpha track", "the line showing the playing title", hangLimit, report);
        var quiet = await UntilQuietAsync(c, "the Media page quiet in DND");
        pretend.Sessions = [Session("Beta track")]; // nobody calls anything: the quiet beat reads the sessions
        var refreshed = await Waiter.UntilAsync(() => rt.View.Contents.TitleText == "Beta track", "the quiet island refreshing the playing line", hangLimit, report);
        report.Check("in DND, with no frame callback the island still refreshes the playing line", quiet && refreshed, $"quiet {quiet}, refreshed {refreshed}");
    }

    private static MediaSessionInfo Session(string title) =>
        new("s1", "alpha-player.exe", false, title, "Beta artist", PlaybackState.Playing, 30, 3600, new ProgressReport(30, 3600, DateTimeOffset.UtcNow, true, 1));

    private async Task DndSeesTheTerminalsAsync()
    {
        var pretend = new PretendWorld
        {
            Windows =
            [
                new OpenWindow(5001, "alpha-term.exe", null, "first shell", 0, "INVENTED_TERMINAL_CLASS", 4001),
                new OpenWindow(5002, "alpha-term.exe", null, "second shell", 1, "INVENTED_TERMINAL_CLASS", 4002),
            ],
        };
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var store = Picks();
        var probe = new CountingProbe();
        var helpers = new HelperSessions(Tables);
        var page = new TerminalsPage(world, probe, Tables, readsWindows: true, helpers, synchronousIcons: true);
        var pages = new PickPages(() => store, world, synchronousIcons: true) { Terminals = page };
        using var rt = new IslandRuntime(3600, pages, null, null, takeKeyboard: false);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        var m = c.Machine;
        c.SetMode(Mode.DND, instant: true);
        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open in DND", hangLimit, report);
        c.PageKey(PageIds.Terminals);
        await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Terminals && page.LaidOut && m.ContentsItems.Count == 2, "the Terminals page at rest with two tiles", hangLimit, report);
        var quiet = await UntilQuietAsync(c, "the Terminals page quiet in DND");

        var reads = probe.Reads;
        await Task.Delay(TimeSpan.FromSeconds(Math.Ceiling(TerminalsPage.ProbePeriod.TotalSeconds * 2) + 0.5));
        report.Check("in DND, with no frame callback the process list is still read while the Terminals page is laid out", quiet && c.IsQuiet && probe.Reads >= reads + 2, $"{probe.Reads - reads} readings in {TerminalsPage.ProbePeriod.TotalSeconds * 2 + 0.5:0.#} s");

        // A title that changes is drawn in its tile without any frame callback.
        pretend.Windows = [.. pretend.Windows.Select(w => w.Handle == 5002 ? w with { Title = "second shell, renamed" } : w)];
        world.Raise();
        var renamed = await Waiter.UntilAsync(() => rt.View.Contents.Contents.Items.Count == 2 && rt.View.Contents.Contents.Items[1].Title == "second shell, renamed", "the renamed tile drawn", hangLimit, report);
        report.Check("in DND, a title that changed is drawn in its tile", renamed, "second tile renamed");

        // A tile coming wakes the island.
        pretend.Windows = [.. pretend.Windows, new OpenWindow(5003, "alpha-term.exe", null, "third shell", 2, "INVENTED_TERMINAL_CLASS", 4003)];
        world.Raise();
        var third = await Waiter.UntilAsync(() => rt.View.Contents.TileCount == 3, "a third tile drawn", hangLimit, report);
        await Waiter.UntilAsync(() => m.IsAtRest, "the capsule at rest at its new width", hangLimit, report);
        report.Check("in DND, a tile coming is drawn and the capsule takes its new width", third && Math.Abs(m.DrawnWidth - m.CapsuleTargetWidth) < 1, $"{rt.View.Contents.TileCount} tiles");
        await UntilQuietAsync(c, "the page quiet again");

        // A session's state changing wakes it: a working ring turns, which needs the frame callback; it ends with the callback let go of again.
        probe.Facts = new TerminalProbeFacts([new ConsoleWindowFact(7001, "PseudoConsoleWindow", Environment.ProcessId, 5001)], [new ProcessFact(Environment.ProcessId, 1, "invented-helper.exe")]);
        helpers.Apply(new Island.Core.Agents.Sessions.SessionMessage("claude", "UserPromptSubmit", "", "quiet-stage-session", Environment.TickCount64, "Q:\\Invented\\island", [Environment.ProcessId]));
        page.ProbeOnce();
        var working = await Waiter.UntilAsync(() => m.ContentsItems.Any(i => i.Ring == HelperState.Working), "a working ring on a tile", hangLimit, report);
        await Task.Delay(300);
        report.Check("in DND, a session's state changing wakes the island: the working ring turns on the frame callback", working && c.RunsAtFrameRate, $"working {working}, runs {c.RunsAtFrameRate}");
    }

    private async Task DndNoticesTheKeyboardGoingElsewhereAsync()
    {
        var other = new Window { Title = "Island self-test: stand-in for another program", Width = 240, Height = 90, Left = 40, Top = 400, WindowStartupLocation = WindowStartupLocation.Manual };
        other.Show();
        var before = new WindowInteropHelper(other).EnsureHandle();
        try
        {
            await Task.Delay(150);
            OutsideForeground.BringForward(before);
            if (Native.GetForegroundWindow() != before)
            {
                report.NeedsHumanVerify.Add("Windows did not let the self-test bring its stand-in window forward, so the check that a quiet island notices the keyboard going elsewhere was not made here: press Ctrl+Q, 3 in Do not disturb, click another program and see that the island gives the keyboard back.");
                return;
            }

            using var rt = new IslandRuntime(3600);
            rt.Show();
            await Task.Delay(300);
            var c = rt.Controller;
            var m = c.Machine;
            c.SetMode(Mode.DND, instant: true);
            c.MainKey();
            if (Native.GetForegroundWindow() != rt.Host.Capsule.Handle)
            {
                report.NeedsHumanVerify.Add("The island could not take the keyboard for the quiet-island keyboard check: see KeyboardStage.");
                return;
            }

            await Waiter.UntilAsync(() => m.IsAtRest, "the island open in DND", hangLimit, report);
            var quiet = await UntilQuietAsync(c, "the DND capsule quiet while it holds the keyboard");
            OutsideForeground.BringForward(before);
            var released = await Waiter.UntilAsync(() => !m.HasKeyboard, "the quiet island noticing it lost the keyboard", hangLimit, report);
            report.Check("in DND, with no frame callback the island still notices that the keyboard went elsewhere", quiet && released, $"quiet {quiet}, released {released}");
        }
        finally
        {
            other.Close();
        }
    }
}
