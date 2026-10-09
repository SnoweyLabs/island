using System.IO;
using System.Windows;
using System.Windows.Interop;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 section 5, with two test windows of the self-test's own and no real mouse: before any click the close handler does
/// nothing; the click handler on the pick brings one window forward and arms the button; the close handler then makes that window go away
/// and leaves the other; a new summon dims the button again. (The refusal of a window of another process is checked by the isolation
/// stage, which has a second process.) No stage ever aims a close at a window the self-test did not make.
/// </summary>
internal sealed class CloseStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    private readonly List<Window> _windows = [];
    private readonly HashSet<Window> _closed = [];

    public async Task RunAsync()
    {
        var ownExe = Path.GetFileName(Environment.ProcessPath) ?? "Island.App.exe";
        var picksPath = Path.Combine(tempRoot, "close-stage", "picks.json");
        var own = Pick.ForProgram("Self test", PageIds.Apps, ownExe, null);
        new PickStore([own]).Save(picksPath);
        var store = PickStore.Load(picksPath).Store;

        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages);
        try
        {
            rt.Show();
            await Task.Delay(150);
            var c = rt.Controller;
            var m = c.Machine;
            c.PageKey(PageIds.Apps);
            await Waiter.UntilAsync(() => m.IsAtRest, "the Apps page open and at rest", hangLimit, report);

            var first = OpenTestWindow(40);
            var second = OpenTestWindow(300);
            pretend.Windows = [Describe(second, ownExe, 0), Describe(first, ownExe, 1)];
            pretend.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems is [{ Count: 2 }], "the pick counting two windows", hangLimit, report);
            await Task.Delay(200);

            var close = rt.Close!;
            var contents = rt.View.Contents;
            report.Check("the Apps page has an X, dimmed before any click", contents.HasCloseButton && close.State == CloseState.Dimmed && !contents.CloseDrawnReady,
                $"state {close.State}");

            // Before any click the close handler does nothing: both windows are still there.
            contents.RaiseCloseForSelfTest();
            await Task.Delay(200);
            report.Check("before any click the close handler does nothing", _closed.Count == 0 && Native.IsWindow(new IntPtr(Handle(first))) && Native.IsWindow(new IntPtr(Handle(second))), "both test windows are still open");

            // The click handler on their pick brings the top window forward and arms the X.
            var refusedBefore = OutsideGate.Current.Refused(OutsideKind.CloseWindow);
            c.TileClicked(0);
            await Task.Delay(120);
            report.Check("a click on the pick arms the X, which is then drawn at full strength", close.State == CloseState.Ready && contents.CloseDrawnReady, $"state {close.State}");

            // The close handler makes the window that was brought forward go away, and only that one.
            contents.RaiseCloseForSelfTest();
            await Waiter.UntilAsync(() => _closed.Contains(second), "the window that was brought forward closing", hangLimit, report);
            report.Check("the close handler closes the window the click brought forward and leaves the other one",
                _closed.Contains(second) && !_closed.Contains(first) && Native.IsWindow(new IntPtr(Handle(first))), $"{_closed.Count} closed");
            report.Check("the gate allowed it: a window of the self-test's own process, nothing refused", OutsideGate.Current.Refused(OutsideKind.CloseWindow) == refusedBefore, "no refusal counted");
            report.Check("the X is used up and dimmed again", close.State == CloseState.Dimmed && !contents.CloseDrawnReady, $"state {close.State}");

            // A new summon dims it again.
            pretend.Windows = [Describe(first, ownExe, 0)];
            pretend.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems is [{ Count: 1 }], "the pick counting one window", hangLimit, report);
            c.TileClicked(0);
            await Task.Delay(120);
            var armed = close.State == CloseState.Ready;
            c.ShowHide();
            await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden", hangLimit, report);
            c.ShowHide();
            await Waiter.UntilAsync(() => m.Phase != IslandPhase.Hidden, "the island summoned again", hangLimit, report);
            report.Check("a new summon dims the armed X again", armed && close.State == CloseState.Dimmed && !contents.CloseDrawnReady, $"armed {armed}, state {close.State}");
        }
        finally
        {
            foreach (var w in _windows.Where(w => !_closed.Contains(w))) w.Close();
            _windows.Clear();
        }
    }

    private Window OpenTestWindow(double left)
    {
        var w = new Window
        {
            Title = "Island self-test window",
            Width = 220,
            Height = 80,
            Left = left,
            Top = 520,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        w.Closed += (_, _) => _closed.Add(w);
        w.Show();
        _windows.Add(w);
        return w;
    }

    private static long Handle(Window w) => new WindowInteropHelper(w).EnsureHandle().ToInt64();

    private static OpenWindow Describe(Window w, string exe, int z) => new(Handle(w), exe, null, "test window", z);
}
