using System.IO;
using System.Windows;
using System.Windows.Interop;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-3 section 3 checks: a page shows the picks of a temporary picks file; its one pick reads closed,
/// then open once a test window exists, then "2 windows" with two; a click on it starts it (refused and counted by
/// the outside gate: the self-test never starts anything) or brings the test window forward, and clicking again
/// goes to the next window. The readers are pretend ones: only the self-test's own test windows are involved, and
/// nothing about the machine is recorded except counts and yes/no.
/// </summary>
internal sealed class PicksStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    private readonly List<Window> _windows = [];

    public async Task RunAsync()
    {
        var ownExe = Path.GetFileName(Environment.ProcessPath) ?? "Island.App.exe";
        var picksPath = Path.Combine(tempRoot, "picks-stage", "picks.json");
        var own = Pick.ForProgram("Self test", PageIds.Apps, ownExe, null);
        report.Check("a temporary picks file with the self-test's own program can be saved", new PickStore([own]).Save(picksPath), "temporary data folder");
        var store = PickStore.Load(picksPath).Store;

        await EmptyPageAsync();
        await IconsDoNotBlockAsync();

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
            var apps = Pages.Get(PageIds.Apps);

            c.PageKey(PageIds.Apps);
            await Waiter.UntilAsync(() => m.IsAtRest, "the Apps page open and at rest", hangLimit, report);
            var items = m.ContentsItems;
            report.Check("the Apps page shows exactly the one pick of the picks file", items.Count == 1 && items[0].PickId == own.Id, $"{items.Count} item(s)");
            report.Check("the capsule is as wide as the width rule says for one item", Math.Abs(m.CapsuleTargetWidth - CapsuleLayout.Width(1, false)) < 0.01,
                $"{m.CapsuleTargetWidth:0.##} wide");
            report.Check("with no window open the pick reads closed", items[0].IsClosed && items[0].Subtitle == "closed", items[0].Subtitle);

            // A click on a closed pick would start it: the gate refuses and counts it, nothing is started.
            var refusedBefore = OutsideGate.Current.Refused(OutsideKind.StartProgram);
            var plan = pages.Click(apps, 0);
            report.Check("a click on the closed pick plans to start it and the gate refuses and counts it",
                plan?.Kind == ClickKind.Start && OutsideGate.Current.Refused(OutsideKind.StartProgram) == refusedBefore + 1,
                $"plan {plan?.Kind}, refused {OutsideGate.Current.Refused(OutsideKind.StartProgram) - refusedBefore}");

            // One test window of the self-test's own: the pick reads open.
            var first = OpenTestWindow(left: 40);
            pretend.Windows = [Describe(first, ownExe, 0)];
            pretend.Raise();
            var open = await Waiter.UntilAsync(() => m.ContentsItems is [{ IsClosed: false, Subtitle: "open" }], "the pick reading open", hangLimit, report);
            report.Check("with a test window open the pick reads open", open, m.ContentsItems.Count == 1 ? m.ContentsItems[0].Subtitle : "?");

            // A second test window: "2 windows" and a count of 2.
            var second = OpenTestWindow(left: 300);
            pretend.Windows = [Describe(second, ownExe, 0), Describe(first, ownExe, 1)];
            pretend.Raise();
            var two = await Waiter.UntilAsync(() => m.ContentsItems is [{ Count: 2 }], "the pick counting two windows", hangLimit, report);
            report.Check("with two test windows the pick reads \"2 windows\" and carries the count 2", two && m.ContentsItems[0].Subtitle == "2 windows",
                m.ContentsItems.Count == 1 ? m.ContentsItems[0].Subtitle : "?");

            // The row is drawn again with the badge (the controller redraws at rest).
            await Task.Delay(200);
            report.Check("the row on screen was rebuilt to carry the count", c.Machine.ContentsItems.Count == 1 && rt.View.Contents.Contents.Items[0].Count == 2,
                $"drawn count {rt.View.Contents.Contents.Items[0].Count}");

            await ClickBringsForward(pages, apps, first, second);
            await PickKeyAsync(rt, pages, apps, own, picksPath, first, second);
        }
        finally
        {
            foreach (var w in _windows) w.Close();
            _windows.Clear();
        }
    }

    private async Task ClickBringsForward(PickPages pages, Page apps, Window first, Window second)
    {
        var top = Handle(second);
        var next = Handle(first);

        var plan = pages.Click(apps, 0);
        await Task.Delay(120);
        var granted = Native.GetForegroundWindow() == new IntPtr(top);
        report.Info["foregroundGrantedForPickClick"] = granted;
        report.Check("a click on an open pick asks for its top-most window", plan is { Kind: ClickKind.BringForward } p && p.Target == top, $"plan {plan?.Kind}");

        var plan2 = pages.Click(apps, 0);
        await Task.Delay(120);
        var grantedNext = Native.GetForegroundWindow() == new IntPtr(next);
        report.Check("clicking again goes to the next window", plan2 is { Kind: ClickKind.BringForward } q && q.Target == next, $"plan {plan2?.Kind}");

        if (!granted || !grantedNext)
            report.NeedsHumanVerify.Add("Windows did not let the self-test bring its own test window forward after a click on a pick (foregroundGranted: false); click an open pick and see that its window comes to the front.");
    }

    /// <summary>
    /// WORK-ORDER-6 section 4, with a temporary settings file and no real key press: the pick that names the self-test's own program is
    /// given a key through the code the settings screen calls (Windows answers; a stand-in combination, so no key of Dan's is touched),
    /// then the key's handler is called. It must do what a click does (the next window comes forward), bring the island in on that pick's
    /// page without the keyboard, and leave the foreground to the program that was brought forward. The key is cleared afterwards and
    /// Windows has it back.
    /// </summary>
    private async Task PickKeyAsync(IslandRuntime rt, PickPages pages, Page apps, Pick own, string picksPath, Window first, Window second)
    {
        var dir = Path.GetDirectoryName(picksPath)!;
        var files = new Island.Core.SettingsEdit.SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), picksPath);
        Settings.Defaults.Save(files.SettingsPath);
        using var hotkeys = new HotkeyHost();
        using var probe = new HotkeyHost();
        var session = new Island.Core.SettingsEdit.SettingsSession(files, Settings.Load(files.SettingsPath), Island.Core.SettingsEdit.PageStore.Load(files.PagesPath), PickStore.Load(picksPath), hotkeys, () => [], () => false);
        const int f8 = 0x77;
        var press = new Island.Core.SettingsEdit.KeyPress(f8, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);

        var given = session.PressKey(own.Id, press);
        report.Info["pickKeyAcceptedByWindows"] = given.Ok;
        var kept = session.Settings.PickKeyFor(own.Id) is not null;
        report.Check("a pick is given a key through the code the screen calls: kept and saved when Windows accepts it, not kept when Windows refuses it",
            given.Ok ? kept && Settings.Load(files.SettingsPath).Settings.PickKeyFor(own.Id) == session.Settings.PickKeyFor(own.Id) : !kept, given.Ok ? "accepted" : "refused by Windows");

        // The island away, then the key's handler: the jump first, then the island on the pick's page, with no keyboard.
        var c = rt.Controller;
        var m = c.Machine;
        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden before the pick key", hangLimit, report);
        var top = Handle(second);
        var done = rt.PickKey(own.Id);
        await Task.Delay(150);
        var forward = pages.LastJump is { Kind: ClickKind.BringForward } plan && plan.Target == top;
        report.Check("the key's handler does what a click on the pick does: the next window is asked to come forward", done && forward, $"plan {pages.LastJump?.Kind}");
        report.Check("it brings the island in on the pick's page without the keyboard", m.Phase is IslandPhase.FlyingIn or IslandPhase.Open && m.PageId == PageIds.Apps && !m.HasKeyboard,
            $"{m.Phase}, page {m.PageId}, keyboard {m.HasKeyboard}");
        var foreground = Native.GetForegroundWindow();
        var islandForeground = foreground == rt.Host.Capsule.Handle || foreground == rt.Host.Shadow.Handle;
        report.Check("the foreground window afterwards is not the island's", !islandForeground, islandForeground ? "the island" : "another window");
        if (foreground != new IntPtr(top))
            report.NeedsHumanVerify.Add("Windows did not let the self-test bring its own test window forward after a pick's key (foregroundGranted: false); give a program a key, press it, and see that its window comes to the front.");

        var next = pages.Click(apps, 0);
        report.Check("the key and a click share one order of windows: the click after the key goes to the window after it", next is { Kind: ClickKind.BringForward } n && n.Target == Handle(first), $"plan {next?.Kind}");

        // Cleared: empty, and Windows has the combination back.
        var combo = HotkeyCombo.Parse("Ctrl+Alt+Shift+F8");
        var cleared = session.ClearKey(own.Id);
        var back = !given.Ok || (cleared.Ok && session.Settings.PickKeyFor(own.Id) is null && probe.TryRegister(combo, out _));
        report.Check("clearing the pick's key leaves it empty and gives the combination back to Windows", back, given.Ok ? "cleared" : "nothing was held");
        report.Check("a key whose pick is gone does nothing", !rt.PickKey("program:gone-for-good"), "no pick, no jump");
    }

    /// <summary>EVALS P8: an empty page looks intended: the width rule for no item and its line reads "Nothing here yet".</summary>
    private async Task EmptyPageAsync()
    {
        var world = AppWorld.Pretend(new PretendWorld(), new OutsideSelfTestActions());
        var pages = new PickPages(() => new PickStore([]), world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages);
        rt.Show();
        await Task.Delay(150);
        var m = rt.Controller.Machine;
        rt.Controller.PageKey(PageIds.Apps);
        await Waiter.UntilAsync(() => m.IsAtRest, "the empty Apps page open and at rest", hangLimit, report);
        report.Check("an empty page looks intended: the capsule is as wide as the width rule says for no item and its line reads \"Nothing here yet\"",
            m.ContentsItems.Count == 0 && Math.Abs(m.CapsuleTargetWidth - CapsuleLayout.Width(0, false)) < 0.01 && rt.View.Contents.TitleText == PickItems.NothingHere,
            $"{m.ContentsItems.Count} item(s), {m.CapsuleTargetWidth:0.##} wide, line \"{rt.View.Contents.TitleText}\"");
    }

    /// <summary>An icon source that waits until it is let go: what a slow place on the network does to the one that reads it. Records the thread it was asked on.</summary>
    private sealed class BlockingIcons(ManualResetEventSlim letGo, IconImage icon) : IIconSource
    {
        public int AskedOnThread { get; private set; } = -1;

        public IconImage? ProgramIcon(string? exeName, string? packageFamily)
        {
            AskedOnThread = Environment.CurrentManagedThreadId;
            letGo.Wait(TimeSpan.FromSeconds(8));
            return icon;
        }

        public IconImage? FolderIcon(string knownFolder) => null;

        public IconImage? PlaceIcon(string realPath) => null;
    }

    /// <summary>EVALS I6: icons never slow the island down: a reader that blocks costs the page nothing (the tile reads its letters at once), and the picture arrives by itself when the reader is let go, from another thread.</summary>
    private async Task IconsDoNotBlockAsync()
    {
        using var letGo = new ManualResetEventSlim(false);
        var pixels = new byte[8 * 8 * 4];
        Array.Fill(pixels, (byte)200);
        var blocking = new BlockingIcons(letGo, new IconImage(8, 8, pixels));
        var pretend = new PretendWorld();
        var world = new AppWorld
        {
            Windows = pretend, Catalog = pretend, Icons = blocking, Folders = pretend, Media = pretend, MediaControl = pretend, Tabs = pretend, TabControl = pretend, Outside = new OutsideSelfTestActions(),
        };
        var store = new PickStore([Pick.ForProgram("Alpha Slow", PageIds.Apps, "alpha-slow.exe", null)]);
        var pages = new PickPages(() => store, world, synchronousIcons: false, plusTile: false);
        var apps = Pages.Get(PageIds.Apps);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var first = pages.ItemsOf(apps);
        var took = clock.Elapsed;
        report.Check("a page whose icon reader blocks is worked out at once with the tile's letters", took < TimeSpan.FromMilliseconds(500) && first.Count == 1 && first[0].Icon is null, $"{took.TotalMilliseconds:0} ms, icon {(first[0].Icon is null ? "none yet" : "there")}");

        var arrived = false;
        pages.Changed += () => arrived = true;
        letGo.Set();
        var shown = await Waiter.UntilAsync(() => { pages.Invalidate(); return pages.ItemsOf(apps)[0].Icon is { Width: > 0 }; }, "the icon arriving after the reader was let go", hangLimit, report);
        report.Check("the picture arrives by itself when the reader is let go, read on a thread that is not the one drawing", shown && blocking.AskedOnThread != -1 && blocking.AskedOnThread != Environment.CurrentManagedThreadId, $"read on thread {blocking.AskedOnThread}, drawing thread {Environment.CurrentManagedThreadId}, changed raised {arrived}");
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
        w.Show();
        _windows.Add(w);
        return w;
    }

    private static long Handle(Window w) => new WindowInteropHelper(w).EnsureHandle().ToInt64();

    private static OpenWindow Describe(Window w, string exe, int z) => new(Handle(w), exe, null, "test window", z);
}
