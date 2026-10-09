using System.Diagnostics;
using System.Globalization;
using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// Section 6 checks, with no real key press: which keybinds Windows accepts, the settings file created
/// with the defaults, the tray icon, one copy only, and idle closing with a short idle time.
/// Uses temporary data folders only, never Dan's real settings or log.
/// </summary>
internal sealed class ShellStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    public async Task RunAsync()
    {
        await FirstCopyAsync();
        await IdleAsync();
        await StartRefusalsAsync();
    }

    /// <summary>
    /// What a start says about files it cannot use and keys Windows will not give, on temporary data folders: a settings file that cannot be read gives SETTINGS_UNREADABLE, one from a newer version
    /// SETTINGS_FROM_NEWER_VERSION, a key another program holds HOTKEY_TAKEN (and a balloon is raised); in every case the file is left exactly as it was. A broken pages, picks or scenes file is left
    /// as it is and starts the island on its defaults, with a line in the log and no words on the screen (that is recorded: new words would be a proposal, PROPOSALS.md).
    /// </summary>
    private async Task StartRefusalsAsync()
    {
        static bool Same(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);

        async Task<(string? Code, bool Untouched, int Balloons, bool Started)> StartWithAsync(string name, string file, Func<AppFiles, string> contents, Action<AppHost>? inspect = null)
        {
            var dir = Path.Combine(tempRoot, name);
            Directory.CreateDirectory(dir);
            var files = new AppFiles(dir);
            var path = Path.Combine(dir, file);
            File.WriteAllText(path, contents(files));
            var before = File.ReadAllBytes(path);
            using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
            if (host is null) return (null, false, 0, false);
            await Task.Delay(400);
            inspect?.Invoke(host);
            return (host.LastRefusalCode, Same(before, File.ReadAllBytes(path)), host.Tray.Notified, true);
        }

        var unreadable = await StartWithAsync("shell-unreadable", "settings.json", _ => "{ this is not a settings file");
        report.Check("a settings file that cannot be read gives SETTINGS_UNREADABLE at the start and is left exactly as it was", unreadable.Started && unreadable.Code == "SETTINGS_UNREADABLE" && unreadable.Untouched, $"code {unreadable.Code}, untouched {unreadable.Untouched}");

        var newer = await StartWithAsync("shell-newer", "settings.json", _ =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(Settings.Defaults.ToJson())!.AsObject();
            node[FileSchema.Key] = 99;
            return node.ToJsonString();
        });
        report.Check("a settings file written by a newer version gives SETTINGS_FROM_NEWER_VERSION at the start and is left exactly as it was", newer.Started && newer.Code == "SETTINGS_FROM_NEWER_VERSION" && newer.Untouched, $"code {newer.Code}, untouched {newer.Untouched}");

        // A page's key that another program holds (the self-test gives a refused MAIN key a stand-in by design, so a page's key is the one that meets it): this thread registers the combination first (the system allows it once), then the start meets "already registered".
        var taken = HotkeyCombo.Parse("Ctrl+Alt+Shift+F8");
        var registered = Native.RegisterHotKey(IntPtr.Zero, 0x5E1F, (uint)taken.Modifiers | Native.ModNoRepeat, (uint)taken.VirtualKey);
        try
        {
            if (registered)
            {
                var held = await StartWithAsync("shell-taken", "settings.json", _ => Settings.Defaults.WithPageKey(PageIds.Media, taken).ToJson(), host => report.Info["takenKeyBalloonsAfterStart"] = host.Tray.Notified);
                report.Check("a page's key that another program holds gives HOTKEY_TAKEN at the start, a balloon is raised, and the key stays in the settings file for the next start", held.Started && held.Code == "HOTKEY_TAKEN" && held.Balloons >= 1 && Settings.Load(Path.Combine(tempRoot, "shell-taken", "settings.json")).Settings.KeyFor(PageIds.Media) == taken, $"code {held.Code}, balloons {held.Balloons}, the page's key kept in the file {Settings.Load(Path.Combine(tempRoot, "shell-taken", "settings.json")).Settings.KeyFor(PageIds.Media) == taken}");
            }
            else
            {
                report.NeedsHumanVerify.Add("The self-test could not take Ctrl+Alt+Shift+F8 itself (another program holds it), so the start's HOTKEY_TAKEN was not checked on this run.");
            }
        }
        finally
        {
            if (registered) Native.UnregisterHotKey(IntPtr.Zero, 0x5E1F);
        }

        // The other three files: left as they are, the island starts on its defaults, and the screen says nothing (recorded; words for it would be a proposal).
        foreach (var file in new[] { "pages.json", "picks.json", "scenes.json" })
        {
            var broken = await StartWithAsync("shell-broken-" + file, file, _ => "[ not valid");
            report.Check($"a {file} that cannot be read is left exactly as it was and the island starts on its defaults (no words are shown for it today)", broken.Started && broken.Untouched && broken.Code is null, $"code {broken.Code ?? "none"}, untouched {broken.Untouched}");
        }
    }

    private async Task FirstCopyAsync()
    {
        var dir = Path.Combine(tempRoot, "shell-a");
        var files = new AppFiles(dir);
        report.Check("before the first start there is no settings file", !File.Exists(files.SettingsPath), "temporary data folder");

        using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
        if (!report.Check("the first copy starts and holds the single-instance lock", host is not null, "AppHost.Start"))
            return;

        report.Check("the settings file was created with the defaults when absent",
            File.Exists(files.SettingsPath) && Settings.Load(files.SettingsPath) is { Status: SettingsStatus.Loaded } load
            && load.Settings == Settings.Defaults with { ShowHide = SelfTestIsolation.StandInUsed ? host!.Settings.ShowHide : Settings.Defaults.ShowHide, Mode = Mode.Focus },
            "created by the first start, read back equal to the defaults (but for the mode, which the self-test names Focus, and the stand-in main key, when another copy holds the real one)");

        // Per keybind: did Windows accept it? A refusal is information, not a failure.
        var rows = host!.HotkeyResults
            .Select(r => new { action = r.Action, combo = r.Combo.ToString(), accepted = r.Accepted, windowsErrorWhenRefused = r.Accepted ? (int?)null : r.LastError })
            .ToList();
        report.Info["keybinds"] = rows;
        // Exactly the entries the settings contain: the main key, plus a page only if it was given its own keybind.
        var wanted = new List<(string Action, string Combo)> { ("showHide", host.Settings.ShowHide.ToString()) };
        wanted.AddRange(Pages.BuiltIn.Where(p => host.Settings.KeyFor(p.Id) is not null).Select(p => (p.Id, host.Settings.KeyFor(p.Id)!.Value.ToString())));
        report.Check("the registered keybinds are exactly the entries the settings contain",
            rows.Select(r => (r.action, r.combo)).SequenceEqual(wanted),
            $"{rows.Count} registered ({string.Join(", ", rows.Select(r => r.combo))}); {rows.Count(r => r.accepted)} accepted by Windows; a refused one means another program on this laptop registered it first");
        var refused = rows.Where(r => !r.accepted).Select(r => r.combo).ToList();
        if (refused.Count > 0)
            report.NeedsHumanVerify.Add($"Windows refused {string.Join(", ", refused)}: another program on this laptop already holds it. Pick another combination in the settings file.");

        report.Check("the tray icon is shown and drawn in code", host.TrayVisible, "NotifyIcon visible with a drawn icon");
        report.Check("the Terminals page is there and, in the ordinary world of the self-test, has no tile (it is handed no real window, console or process list)",
            host.Pages.ById(PageIds.Terminals) is not null && host.Terminals is { Items.Count: 0, ProbeCount: 0 }, $"{host.Terminals?.Items.Count} tiles, {host.Terminals?.ProbeCount} readings");

        ModeSwitching(host, files);
        await WarmUpAsync(host);
        await SecondCopyAsync(host, files, dir);
    }

    /// <summary>
    /// EVALS X1: the three modes, one always on, switched from the tray's Mode entry (the three names, the one that is on ticked) or by the key for the next mode (Focus, Vibe, Do not disturb, Focus);
    /// each switch is in force at once (the gate, the island's edge) and saved. The entry's own opening and click are made without showing the menu.
    /// </summary>
    private void ModeSwitching(AppHost host, AppFiles files)
    {
        var mode = host.Tray.MenuForSelfTest?.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Mode");
        if (mode is null)
        {
            report.Check("the tray's Mode entry lists the three modes with the one that is on ticked", false, "no Mode entry in the tray's menu");
            return;
        }

        (string Names, string Ticked) Open()
        {
            host.Tray.OpenEntryForSelfTest("Mode");
            var items = mode.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().ToList();
            return (string.Join(",", items.Select(i => i.Text)), string.Join(",", items.Where(i => i.Checked).Select(i => i.Text)));
        }

        var first = Open();
        report.Check("the tray's Mode entry lists Focus, Vibe and DND with the one that is on ticked", first.Names == "Focus,Vibe,DND" && first.Ticked == host.Settings.Mode.ToString(), $"{first.Names}; ticked {first.Ticked}; mode {host.Settings.Mode}");

        mode.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().First(i => i.Text == "DND").PerformClick();
        var afterTray = Open();
        report.Check("choosing DND in the tray's Mode entry puts it in force at once (the table the gate asks, the island's edge), ticks it and saves it",
            host.Settings.Mode == Mode.DND && host.Runtime.Gate.Mode == Mode.DND && afterTray.Ticked == "DND" && Settings.Load(files.SettingsPath).Settings.Mode == Mode.DND, $"settings {host.Settings.Mode}, gate {host.Runtime.Gate.Mode}, ticked {afterTray.Ticked}, file {Settings.Load(files.SettingsPath).Settings.Mode}");

        var seen = new List<Mode>();
        for (var i = 0; i < 3; i++)
        {
            host.CycleMode();
            seen.Add(host.Settings.Mode);
        }

        report.Check("the key for the next mode goes Focus, Vibe, Do not disturb, Focus again", seen.SequenceEqual([Mode.Focus, Mode.Vibe, Mode.DND]) && Settings.Load(files.SettingsPath).Settings.Mode == Mode.DND, string.Join(" > ", seen));
        host.SetMode(Mode.Focus); // as the self-test names it

        // The tray's Glass entry: Approved and Darker always (Blur only while it is offered), the glass in use ticked; a choice is in force at once and saved.
        var glass = host.Tray.MenuForSelfTest?.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().FirstOrDefault(i => i.Text == "Glass");
        if (glass is null || !host.Tray.OpenEntryForSelfTest("Glass"))
        {
            report.Check("the tray's Glass entry lists the glasses with the one in use ticked", false, "no Glass entry in the tray's menu");
            return;
        }

        var glassItems = glass.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().ToList();
        var ticked = glassItems.Where(i => i.Checked).Select(i => i.Text).ToList();
        report.Check("the tray's Glass entry lists Approved and Darker (and Blur while it is offered) with the glass in use ticked", glassItems.Select(i => i.Text).Take(2).SequenceEqual(["Approved", "Darker"]) && ticked.SequenceEqual([host.Settings.Glass.ToString()]), $"{string.Join(",", glassItems.Select(i => i.Text))}; ticked {string.Join(",", ticked)}");
        glassItems.First(i => i.Text == "Darker").PerformClick();
        host.Tray.OpenEntryForSelfTest("Glass");
        var darkerTicked = glass.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Where(i => i.Checked).Select(i => i.Text).ToList();
        report.Check("choosing Darker in the tray's Glass entry puts it in force and ticks it", host.Runtime.Glass == GlassKind.Darker && darkerTicked.SequenceEqual(["Darker"]), $"in use {host.Runtime.Glass}, ticked {string.Join(",", darkerTicked)}");
        host.Runtime.SetGlass(GlassKind.Approved);
    }

    /// <summary>
    /// WORK-ORDER-12 section 3, the first summon no longer finds its code cold: after a silent start the app builds an island of invented pages and the settings view over canvases that are in no window,
    /// once, and throws them away. It shows no window, draws no frame of the real island, opens no settings screen, and ends.
    /// </summary>
    private async Task WarmUpAsync(AppHost host)
    {
        var windows = System.Windows.Application.Current.Windows.Count;
        var frames = host.Runtime.Controller.FramesDrawn;
        var done = host.WarmUpAsync();
        var finished = await Task.WhenAny(done, Task.Delay(hangLimit * 2)) == done;
        report.Check("the warm-up after a silent start (the first summon's work done in advance) builds its island and the settings view in no window, shows nothing, and ends",
            finished && !done.IsFaulted && System.Windows.Application.Current.Windows.Count == windows && host.Runtime.Controller.FramesDrawn == frames && host.Runtime.Machine.Phase == IslandPhase.Hidden && !host.Screen.IsOpen,
            $"finished {finished}, windows {windows} to {System.Windows.Application.Current.Windows.Count}, real island frames {host.Runtime.Controller.FramesDrawn - frames}, phase {host.Runtime.Machine.Phase}");
    }

    /// <summary>A second copy tells the running one and exits with the ALREADY_RUNNING code; the running one shows the refusal.</summary>
    private async Task SecondCopyAsync(AppHost host, AppFiles files, string dir)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            report.Check("a second copy exits with the ALREADY_RUNNING code", false, "the program path is unknown");
            return;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--selftest-copy", "--data-dir", Path.Combine(dir, "second") }, // carries --selftest in its name: it meets this self-test's lock, never Dan's
        };
        using var second = OutsideShell.StartOwnCopy(psi);
        if (second is null)
        {
            report.Check("a second copy exits with the ALREADY_RUNNING code", false, "it could not be started");
            return;
        }

        using var cts = new CancellationTokenSource(hangLimit);
        try
        {
            await second.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            second.Kill();
            report.Check("a second copy exits with the ALREADY_RUNNING code", false, "it did not exit within the hang limit");
            return;
        }

        report.Check("a second copy exits with the ALREADY_RUNNING code", second.ExitCode == ExitCodes.AlreadyRunning, $"exit code {second.ExitCode}");

        var shown = await Waiter.UntilAsync(() => File.Exists(files.LogPath) && File.ReadAllText(files.LogPath).Contains("refusal ALREADY_RUNNING"),
            "the running copy showing ALREADY_RUNNING", hangLimit, report);
        report.Check("the running copy showed ALREADY_RUNNING and logged it", shown, "a process that has exited cannot show anything, so the first copy does");
    }

    /// <summary>With a short idle time in the settings file, an open island closes by itself after that time.</summary>
    private async Task IdleAsync()
    {
        const double idleSeconds = 2;
        var dir = Path.Combine(tempRoot, "shell-b");
        Directory.CreateDirectory(dir);
        var files = new AppFiles(dir);
        File.WriteAllText(files.SettingsPath, $$"""{ "idleSeconds": {{idleSeconds.ToString(CultureInfo.InvariantCulture)}} }""");

        using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
        if (!report.Check("a copy with a short idle time starts", host is not null, "AppHost.Start"))
            return;

        var c = host!.Runtime.Controller;
        var m = c.Machine;
        report.Check("the short idle time was read from the settings file", Math.Abs(host.Settings.IdleSeconds - idleSeconds) < 1e-9,
            $"{host.Settings.IdleSeconds} s");

        c.ShowHide();
        await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), "the island open", hangLimit, report);
        var closing = await Waiter.UntilAsync(() => m.Phase is IslandPhase.Closing or IslandPhase.Hidden, "the island closing by itself", hangLimit, report);
        var waited = c.NowMs - c.SummonedMs;
        report.Check("an open island closed by itself after the idle time", closing && waited >= idleSeconds * 1000 - 50 && waited < idleSeconds * 1000 + 1500,
            $"closed {waited.ToString("0", CultureInfo.InvariantCulture)} ms after the summon, idle time {idleSeconds * 1000} ms");
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden again", hangLimit, report);
    }
}
