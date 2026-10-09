using System.IO;
using System.Windows;
using System.Windows.Interop;
using Island.Core;
using Island.Sources.Programs;

namespace Island.App;

/// <summary>
/// WORK-ORDER-3 section 2 checks with the real readers, on the self-test's own test windows only: a normal test
/// window is in the list and a tool window is not; of two test windows, "bring forward" makes the asked one the
/// foreground window (or foregroundGranted: false); the icon of explorer.exe is not empty and not generic; the
/// outside gate refuses what is not the self-test's own and counts it. Only counts and yes/no go into selftest.json.
/// </summary>
internal sealed class ProgramsStage(SelfTestReport report, TimeSpan hangLimit)
{
    public async Task RunAsync()
    {
        using var catalog = new CatalogScope();
        using var lister = new WindowLister();
        using var icons = new ProgramIcons(catalog.Catalog);
        var outside = new OutsideActions(catalog.Catalog);

        lister.WaitForFirstRead(hangLimit);
        report.Info["windowsListed"] = lister.Windows.Count;
        report.Info["windowHooksInstalled"] = lister.HooksInstalled;
        report.Check("the window lister read the open windows", lister.Windows.Count > 0, $"{lister.Windows.Count} windows");

        var normal = Show(new Window { Title = "Island self-test window one", Width = 200, Height = 70, Left = 40, Top = 620, ShowActivated = false });
        var second = Show(new Window { Title = "Island self-test window two", Width = 200, Height = 70, Left = 260, Top = 620, ShowActivated = false });
        var tool = Show(new Window { Title = "Island self-test tool window", Width = 200, Height = 70, Left = 480, Top = 620, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow });
        try
        {
            var h1 = Handle(normal);
            var h2 = Handle(second);
            var ht = Handle(tool);

            var seen = await Waiter.UntilAsync(() => lister.Windows.Any(w => w.Handle == h1) && lister.Windows.Any(w => w.Handle == h2), "the test windows in the window list", hangLimit, report);
            report.Check("a normal test window is in the window list", seen, "own process, normal window");
            report.Check("a tool window is not in the window list", lister.Windows.All(w => w.Handle != ht), "tool windows are left out like Alt+Tab does");
            var mine = Path.GetFileName(Environment.ProcessPath);
            var ours = lister.Windows.Where(w => string.Equals(w.ExeName, mine, StringComparison.OrdinalIgnoreCase)).ToList();
            report.Check("the list carries only a file name for the program, never a path", ours.Count >= 2 && ours.All(w => w.ExeName is { } e && e.IndexOfAny(['\\', '/', ':']) < 0), $"{ours.Count} windows of the self-test's own program");

            // Bringing forward: own windows are allowed (and Windows may or may not grant it), another program's window is refused and counted.
            var refusedBefore = OutsideGate.Current.Refused(OutsideKind.BringForward);
            outside.BringForward(h1);
            await Task.Delay(100);
            outside.BringForward(h2);
            await Task.Delay(100);
            var granted = Native.GetForegroundWindow() == new IntPtr(h2);
            report.Info["foregroundGrantedForBringForward"] = granted;
            if (!granted) report.NeedsHumanVerify.Add("Windows did not let the self-test bring one of its own test windows forward through the outside layer (foregroundGranted: false); click an open pick and see that its window comes to the front.");

            var foreign = Native.GetShellWindow();
            var refused = !outside.BringForward(foreign.ToInt64()) && OutsideGate.Current.Refused(OutsideKind.BringForward) == refusedBefore + 1;
            report.Check("bringing forward a window that is not the self-test's own is refused and counted", refused, "numbers only");
            report.Check("starting a program, opening a folder and a site are refused and counted",
                !outside.StartProgram(Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)) && !outside.OpenFolder("Downloads") && !outside.OpenSite("example.org")
                && OutsideGate.Current.Refused(OutsideKind.StartProgram) >= 1 && OutsideGate.Current.Refused(OutsideKind.OpenFolder) >= 1 && OutsideGate.Current.Refused(OutsideKind.OpenAddress) >= 1,
                "nothing was started");

            // Icons: the icon of explorer.exe is not empty and not the generic icon of a file with none.
            var explorer = await Task.Run(() => icons.ProgramIcon("explorer.exe", null));
            var generic = explorer is not null && icons.IsGenericFileIcon(explorer);
            report.Info["explorerIconPixels"] = explorer is null ? 0 : explorer.Width;
            report.Check("the icon read for explorer.exe is not empty and is not the generic file icon", explorer is { Width: > 0 } && !generic, explorer is null ? "none" : $"{explorer.Width} px");
            report.Check("the icon is read as large as the program offers: explorer.exe's has at least 48 pixels (EVALS I5: the sizes are tried from the largest down)", explorer is { Width: >= 48 }, explorer is null ? "none" : $"{explorer.Width} px");
            var folder = await Task.Run(() => icons.FolderIcon("Downloads"));
            report.Check("the icon of a known folder can be read", folder is { Width: > 0 }, folder is null ? "none" : $"{folder.Width} px");

            // Counts about this laptop, never names.
            catalog.Catalog.WaitForLoad(hangLimit);
            report.Info["installedPrograms"] = catalog.Catalog.Installed.Count;
            report.Info["starterProgramsFound"] = StarterPicks.CountFound(catalog.Catalog.Installed);
            report.Info["starterProgramsTotal"] = StarterPicks.Programs.Count;
            report.Check("the installed programs were read", catalog.Catalog.Installed.Count > 0, $"{catalog.Catalog.Installed.Count} programs, {StarterPicks.CountFound(catalog.Catalog.Installed)} of {StarterPicks.Programs.Count} starter programs found");
            // The first-run list, made from the real installed programs into a temporary file: names only, never a path.
            var starterPath = Path.Combine(Path.GetTempPath(), "island-selftest-starter-" + Guid.NewGuid().ToString("N"), "picks.json");
            try
            {
                var started = PickStore.OpenOrStart(starterPath, catalog.Catalog.Installed, selfTest: false);
                var text = File.Exists(starterPath) ? File.ReadAllText(starterPath) : string.Empty;
                report.Info["starterPicksMade"] = started.Store.Picks.Count;
                report.Check("the first-run starter list made from the real installed programs holds names only, never a path",
                    started.Store.Picks.Count > 0 && text.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Za-z]:\\|\\\\"), $"{started.Store.Picks.Count} picks in a temporary file");
            }
            finally
            {
                try { Directory.Delete(Path.GetDirectoryName(starterPath)!, recursive: true); }
                catch (IOException) { /* temporary folder */ }
            }

            if (StarterPicks.CountFound(catalog.Catalog.Installed) == 0)
                report.NeedsHumanVerify.Insert(0, "None of the starter programs was found among the installed programs (starterProgramsFound: 0): the program pages will start empty. Tell Claude.");
        }
        finally
        {
            normal.Close();
            second.Close();
            tool.Close();
        }
    }

    private static Window Show(Window w)
    {
        w.Show();
        return w;
    }

    private static long Handle(Window w) => new WindowInteropHelper(w).EnsureHandle().ToInt64();

    /// <summary>Owns the catalog so it can be used in a using block (it has nothing to dispose itself).</summary>
    private sealed class CatalogScope : IDisposable
    {
        public InstalledProgramCatalog Catalog { get; } = new();

        public void Dispose()
        {
        }
    }
}
