using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Bridge;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.App;

/// <summary>
/// WORK-ORDER-4 section 3 checks, with temporary files and no real key press: the screen opens and closes (and the
/// foreground afterwards is the window from before, or foregroundGranted: false); a key change made through the same
/// code the screen calls is in force and saved, and a refused one leaves the old key in force; a new page appears on the
/// island with its colour and its number; snapshots of each section go to review/settings.
/// </summary>
internal sealed class SettingsStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot, string folder)
{
    private const int KeyK = 0x4B;
    private const int Digit6 = 0x36;

    /// <summary>A registrar that behaves like Windows: one combination is held by "another program" and refused.</summary>
    private sealed class PretendRegistrar(HotkeyCombo taken) : IHotkeyRegistrar
    {
        public List<string> Log { get; } = [];

        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = combo == taken ? 1409 : 0;
            Log.Add((error == 0 ? "register " : "refused ") + combo);
            return error == 0;
        }

        public void Release(HotkeyCombo combo) => Log.Add("release " + combo);
    }

    public async Task RunAsync()
    {
        var dir = Path.Combine(tempRoot, "settings-stage");
        Directory.CreateDirectory(dir);
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"), Path.Combine(dir, "scenes.json"));
        var taken = HotkeyCombo.Parse("Ctrl+Alt+J");
        var registrar = new PretendRegistrar(taken);
        Settings.Defaults.Save(files.SettingsPath);
        var installed = StarterPicks.Programs.Select(p => new InstalledProgram(p.Name, p.ExeCandidates[0], null, "launch-" + p.Name)).ToList();
        var picks = new PickStore(StarterPicks.Build(installed));

        SettingsSession NewSessionWith(IAddonStatus? addon) => new(files, Settings.Load(files.SettingsPath, PageStore.Load(files.PagesPath).Store.Pages), PageStore.Load(files.PagesPath),
            new PickStoreLoad(picks, PickStoreStatus.Loaded, null), registrar, () => installed, () => false,
            new StartupSwitch(new MemoryStartupRegistry(), @"C:\Island\Island.App.exe"), // in memory: the real registry is never touched here
            scenes: SceneStore.Load(files.ScenesPath!), addon: addon);
        SettingsSession NewSession() => NewSessionWith(null);

        KeyChanges(NewSession, files);
        NoLightSetting(files);
        var session = NewSession();
        Snapshots(session);
        AddPanelSnapshots(files, registrar);
        MovePick(session);
        await ScreenAsync(NewSession, files);
        await AddonRowAsync(NewSessionWith);
    }

    // ---- the row "Browser add-on" (WORK-ORDER-9 section 2) ------------------------

    /// <summary>The text of the element of the screen that carries this automation id, or null.</summary>
    private static string? TextOfId(DependencyObject root, string id)
    {
        if (System.Windows.Automation.AutomationProperties.GetAutomationId(root) == id && root is TextBlock text) return text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (TextOfId(VisualTreeHelper.GetChild(root, i), id) is { } found) return found;
        return null;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// A bridge on a free port of its own (never the real ones) and a pretend add-on: the General section reads "Not connected", then "Connected" after the
    /// add-on's hello (while the screen is open and nothing was rebuilt), then "Not connected" after it leaves; a snapshot of the connected row goes to
    /// review/settings/addon-row.png. Numbers and words only: nothing the pretend add-on says about tabs is shown or written.
    /// </summary>
    private async Task AddonRowAsync(Func<IAddonStatus?, SettingsSession> newSession)
    {
        var port = FreePort();
        using var bridge = new TabBridge([port]);
        report.Check("the bridge for the row's check listens on a port of its own", bridge.Start() == port, "a port of its own, never the real ones");

        var view = new SettingsView(newSession(bridge)) { Section = SettingsSection.General };
        var window = new Window
        {
            Width = 1000, Height = 700, Left = -4000, Top = 0, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None, Content = view,
        };
        window.Show();
        try
        {
            await Task.Delay(100);
            var first = TextOfId(view, AddonText.StatusId);
            report.Check("with no add-on connected the row reads \"Not connected\"", first == AddonText.Status(0), first ?? "no row found");

            await using var addon = new PretendAddon(port);
            var announced = await addon.AnnounceAsync("self-test", TimeSpan.FromSeconds(5));
            var connected = announced && await Waiter.UntilAsync(() => TextOfId(view, AddonText.StatusId) == AddonText.Status(1), "the row reading Connected", hangLimit, report);
            report.Check("after the add-on's hello the open screen's row reads \"Connected\"", connected, TextOfId(view, AddonText.StatusId) ?? "no row found");

            SnapshotRow(newSession(bridge));

            await addon.DisposeAsync();
            var left = await Waiter.UntilAsync(() => TextOfId(view, AddonText.StatusId) == AddonText.Status(0), "the row reading Not connected", hangLimit, report);
            report.Check("after the add-on leaves the row reads \"Not connected\" again", left, TextOfId(view, AddonText.StatusId) ?? "no row found");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private void SnapshotRow(SettingsSession session)
    {
        var dir = Path.Combine(folder, "settings");
        Directory.CreateDirectory(dir);
        var view = new SettingsView(session);
        view.FreezeAnimations(1.0);
        view.Section = SettingsSection.General;
        var stage = new Grid { Width = 1920, Height = 1080, Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) };
        stage.Children.Add(view);
        stage.Measure(new Size(1920, 1080));
        stage.Arrange(new Rect(0, 0, 1920, 1080));
        stage.UpdateLayout();
        var path = Path.Combine(dir, "addon-row.png");
        var word = TextOfId(stage, AddonText.StatusId);
        Snapshot.Of(stage, 1920, 1080, 96).SavePng(path);
        stage.Children.Clear();
        report.Check("a snapshot of the General section with the add-on connected was drawn", File.Exists(path) && word == AddonText.Status(1), "review/settings/addon-row.png (a snapshot proves it draws, not that it looks right)");
    }

    // ---- a key change ------------------------------------------------------------

    private void KeyChanges(Func<SettingsSession> newSession, SettingsFiles files)
    {
        var session = newSession();
        var old = session.Settings.ShowHide;

        var refused = session.PressKey(KeybindEditor.MainId, new KeyPress(0x4A, HotkeyModifiers.Control | HotkeyModifiers.Alt)); // Ctrl+Alt+J: held by "another program"
        report.Check("a main key that Windows says another program holds is refused and the old key stays in force",
            !refused.Ok && session.Settings.ShowHide == old && Settings.Load(files.SettingsPath).Settings.ShowHide == old, refused.Refusal is null ? "accepted" : "refused with a reason");

        var changed = session.PressKey(KeybindEditor.MainId, new KeyPress(KeyK, HotkeyModifiers.Control | HotkeyModifiers.Alt));
        var saved = Settings.Load(files.SettingsPath).Settings.ShowHide;
        report.Check("a main key changed through the screen's own code is in force and saved at once",
            changed.Ok && session.Settings.ShowHide.ToString() == "Ctrl+Alt+K" && saved == session.Settings.ShowHide, session.Settings.ShowHide.ToString());

        var again = session.RestoreKey(KeybindEditor.MainId);
        report.Check("restore default brings the original main key back and saves it", again.Ok && Settings.Load(files.SettingsPath).Settings.ShowHide == Settings.Defaults.ShowHide, "Ctrl+Q");

        var unsafeKey = session.PressKey(KeybindEditor.MainId, new KeyPress(0x51, HotkeyModifiers.None));
        report.Check("a plain key with no modifier is refused", !unsafeKey.Ok && !unsafeKey.Waiting, "typing must stay typing");
    }

    // ---- "Add…" (WORK-ORDER-10 §3): the panel, with an invented list of programs and no path anywhere ----------------

    /// <summary>
    /// The four states of the panel go to review/settings: open and empty, the program list narrowed by typed text, an address that was understood (the host it will keep is shown),
    /// and nonsense that was refused with its reason. The programs are an invented list; the chooser is a pretend one; no snapshot shows a path.
    /// </summary>
    private void AddPanelSnapshots(SettingsFiles files, IHotkeyRegistrar registrar)
    {
        var dir = Path.Combine(folder, "settings");
        Directory.CreateDirectory(dir);
        var invented = new[] { "Alpha Tool", "Beta Studio", "Gamma Player", "Delta Notes", "Epsilon Draw", "Gamer Hub", "Zeta Mail" }
            .Select(n => new InstalledProgram(n, n.Split(' ')[0].ToLowerInvariant() + ".exe", null, "launch-" + n)).ToList();
        var session = new SettingsSession(files, Settings.Load(files.SettingsPath, PageStore.Load(files.PagesPath).Store.Pages), PageStore.Load(files.PagesPath),
            new PickStoreLoad(new PickStore([Pick.ForProgram("Alpha Tool", PageIds.Media, "alpha.exe", null), Pick.ForSite("Example", "example.org", PageIds.Media)]), PickStoreStatus.Loaded, null),
            registrar, () => invented, () => false)
        {
            Chooser = new PretendChooser(),
        };
        var view = new SettingsView(session);
        view.FreezeAnimations(1.0);
        var written = 0;
        foreach (var (file, narrow, site) in new (string, string?, string?)[]
                 {
                     ("add-panel.png", null, null),
                     ("add-panel-narrowed.png", "gam", null),
                     ("add-panel-site.png", null, "www.Example.org/watch/something?x=1"),
                     ("add-panel-refused.png", null, "not a site at all"),
                 })
        {
            view.ShowAddPanel(PageIds.Media, narrow, site);
            var stage = new Grid { Width = 1920, Height = 1500, Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) };
            stage.Children.Add(view);
            stage.Measure(new Size(1920, 1500));
            stage.Arrange(new Rect(0, 0, 1920, 1500));
            stage.UpdateLayout();
            Snapshot.Of(stage, 1920, 1500, 96).SavePng(Path.Combine(dir, file));
            stage.Children.Clear();
            written++;
        }

        report.Check("snapshots of the \"Add…\" panel (open, narrowed list, an address understood, nonsense refused) were drawn with an invented list of programs and no path", written == 4 && File.Exists(Path.Combine(dir, "add-panel-refused.png")), "review/settings (a snapshot proves it draws, not that it looks right)");
    }

    // ---- a pick moved to another page (the control of "On the island"; the right-click menu is gone) ----

    private void MovePick(SettingsSession session)
    {
        var pick = session.Picks.Picks.First();
        var other = session.Pages.Pages.First(p => p.Id != pick.PageId);
        var result = session.MovePick(pick.Id, other.Id);
        report.Check("a pick can be moved to another page through the session the screen uses, and it is on exactly one page",
            result is { Changed: true, Ok: true } && session.Picks.ById(pick.Id)?.PageId == other.Id && session.Picks.Picks.Count(p => p.Id == pick.Id) == 1,
            $"moved to {other.Name}");
    }

    // ---- no setting for the moving light (WORK-ORDER-13) ---------------------------

    /// <summary>
    /// Dan's answer Q1: "Moving light" is gone from General. A settings file that still has the field loads and is saved without it; the island picks the light itself.
    /// </summary>
    private void NoLightSetting(SettingsFiles files)
    {
        var text = File.ReadAllText(files.SettingsPath);
        var withField = text.TrimEnd().TrimEnd('}').TrimEnd() + ", \"movingLight\": \"halfrate\" }";
        File.WriteAllText(files.SettingsPath, withField);
        var load = Settings.Load(files.SettingsPath);
        report.Check("a settings file that still holds the old movingLight field loads", load.Status == SettingsStatus.Loaded, $"{load.Status}");
        var saved = load.Settings.Save(files.SettingsPath) && !File.ReadAllText(files.SettingsPath).Contains("movingLight", StringComparison.OrdinalIgnoreCase);
        report.Check("saving the settings again does not write the moving light", saved, "movingLight is not in the file");
    }

    private void SnapshotGeneral(SettingsSession session, string file)
    {
        var dir = Path.Combine(folder, "settings");
        Directory.CreateDirectory(dir);
        var view = new SettingsView(session);
        view.FreezeAnimations(1.0);
        view.Section = SettingsSection.General;
        var stage = new Grid { Width = 1920, Height = 1080, Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) };
        stage.Children.Add(view);
        stage.Measure(new Size(1920, 1080));
        stage.Arrange(new Rect(0, 0, 1920, 1080));
        stage.UpdateLayout();
        Snapshot.Of(stage, 1920, 1080, 96).SavePng(Path.Combine(dir, file));
        stage.Children.Clear();
        report.Check($"a snapshot of the moving light choices was drawn ({file})", File.Exists(Path.Combine(dir, file)), $"review/settings/{file} (a snapshot proves it draws, not that it looks right)");
    }

    // ---- snapshots of each section -------------------------------------------------

    private void Snapshots(SettingsSession session)
    {
        var dir = Path.Combine(folder, "settings");
        Directory.CreateDirectory(dir);
        session.CreatePage("Alpha games", "#7CE04A");
        session.CreateScene("Evening");
        var evening = session.Scenes.Items[0];
        foreach (var pick in session.AllPicks.Take(2)) session.SetSceneThing(evening.Id, pick, true);
        var view = new SettingsView(session);
        view.FreezeAnimations(1.0);
        var written = 0;
        foreach (var (section, file) in new[] { (SettingsSection.Key, "key.png"), (SettingsSection.Pages, "pages.png"), (SettingsSection.OnTheIsland, "island.png"), (SettingsSection.Scenes, "scenes.png"), (SettingsSection.Mode, "mode.png"), (SettingsSection.Glass, "glass.png"), (SettingsSection.CodingAgents, "agents.png"), (SettingsSection.General, "general.png") })
        {
            view.Section = section;
            var stage = new Grid { Width = 1920, Height = 1080, Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) };
            stage.Children.Add(view);
            stage.Measure(new Size(1920, 1080));
            stage.Arrange(new Rect(0, 0, 1920, 1080));
            stage.UpdateLayout();
            Snapshot.Of(stage, 1920, 1080, 96).SavePng(Path.Combine(dir, file));
            stage.Children.Clear();
            written++;
        }

        report.Check("a snapshot of each section of the settings screen was drawn", written == 8 && File.Exists(Path.Combine(dir, "general.png")) && File.Exists(Path.Combine(dir, "mode.png")), "review/settings: key, pages, island, scenes, mode, glass, agents, general (a snapshot proves it draws, not that it looks right)");
    }

    // ---- the screen opening and closing, and a new page on the island ------------------

    private async Task ScreenAsync(Func<SettingsSession> newSession, SettingsFiles files)
    {
        var other = new Window { Title = "Island self-test: stand-in for another program", Width = 240, Height = 90, Left = 40, Top = 400, WindowStartupLocation = WindowStartupLocation.Manual };
        other.Show();
        var before = new WindowInteropHelper(other).EnsureHandle();
        await Task.Delay(150);
        OutsideForeground.BringForward(before);
        var granted = Native.GetForegroundWindow() == before;
        report.Info["foregroundGrantedForSettings"] = granted;

        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new RecordingOutside());
        var book = new PickBook(PickStore.Empty, null, canSave: false);
        var pages = new PickPages(() => book.Store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(30, pages, book);
        rt.Show();

        var screen = new SettingsScreen(newSession, () => GlassKind.Approved);
        screen.Changed += (area, s) =>
        {
            if (area == SettingsArea.Pages) rt.SetPages(s.Pages.Pages);
        };
        var gone = false;
        screen.Closed += () => gone = true;

        try
        {
            screen.Open();
            // EVALS G1: the ball in the middle opens into the full screen with the island's own spring and no jump: the window's width is sampled while it opens.
            var widths = new List<double>();
            for (var i = 0; i < 400 && screen.Phase != SettingsScreen.SettingsScreenPhase.Open; i++)
            {
                if (screen.DrawnWidthForSelfTest is { } w) widths.Add(w);
                await Task.Delay(8);
            }

            var open = await Waiter.UntilAsync(() => screen.Phase == SettingsScreen.SettingsScreenPhase.Open, "the settings screen open", hangLimit, report);
            if (screen.DrawnWidthForSelfTest is { } opened) widths.Add(opened);
            var final = widths.Count == 0 ? 0 : widths.Max();
            var biggestStep = 0.0;
            for (var i = 1; i < widths.Count; i++) biggestStep = Math.Max(biggestStep, Math.Abs(widths[i] - widths[i - 1]));
            report.Check("the settings screen's ball grows into the full screen without a jump: it starts small, grows over many samples and no sample is more than 40 percent of the full width from the one before",
                widths.Count >= 5 && widths[0] < final * 0.5 && biggestStep <= final * 0.4, $"{widths.Count} samples, from {(widths.Count > 0 ? widths[0] : 0):0} to {final:0} wide, biggest step {biggestStep:0}");
            report.Check("the settings screen expands from a ball to the full screen with the island's spring", open && screen.View is not null, $"phase {screen.Phase}");

            if (granted) report.Check("while it is open the screen holds the keyboard", Native.GetForegroundWindow() != before, "the stand-in window is no longer in front");

            // A new page made through the screen's own code appears on the island with its colour and the next number.
            var made = screen.Session!.CreatePage("Beta games", "#3FD0FF");
            report.Check("a page made on the screen is accepted", made.Ok, made.Refusal ?? "ok");
            var newest = screen.Session.Pages.Pages[^1];
            rt.Controller.PageKey(newest.Id);
            await Waiter.UntilAsync(() => rt.Machine.IsAtRest, "the new page open and at rest", hangLimit, report);
            report.Check("the new page appears on the island with its colour and is reached with the next number key",
                rt.Machine.PageId == newest.Id && rt.Machine.Page.Color == "#3FD0FF" && rt.Machine.DigitKeyCount == 8, $"{rt.Machine.DigitKeyCount} pages by number");
            rt.Controller.HandleKey(Digit6);

            screen.Close();
            var closed = await Waiter.UntilAsync(() => gone, "the settings screen closed", hangLimit, report);
            report.Check("the screen closes by the reverse motion", closed && screen.Phase == SettingsScreen.SettingsScreenPhase.Closed, $"phase {screen.Phase}");
            await Task.Delay(150);
            var back = Native.GetForegroundWindow() == before;
            report.Check("after the screen closed the foreground window is the one from before", back || !granted, granted ? (back ? "same" : "different") : "foregroundGranted: false");

            // EVALS G3: Esc closes the screen (the routed key event the window's own handler gets; no input is sent to the system).
            gone = false;
            screen.Open();
            var reopened = await Waiter.UntilAsync(() => screen.Phase == SettingsScreen.SettingsScreenPhase.Open, "the settings screen open again", hangLimit, report);
            var window = screen.WindowForSelfTest;
            if (reopened && window is not null && PresentationSource.FromVisual(window) is { } source)
            {
                window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                var escGone = await Waiter.UntilAsync(() => gone, "the settings screen closed by Esc", hangLimit, report);
                report.Check("Esc closes the settings screen", escGone && screen.Phase == SettingsScreen.SettingsScreenPhase.Closed, $"phase {screen.Phase}");
            }
            else
            {
                report.Check("Esc closes the settings screen", false, "the screen's window could not be reached");
            }
            if (!granted)
                report.NeedsHumanVerify.Add("Windows did not let the self-test bring its stand-in window forward (foregroundGranted: false), so the settings screen's keyboard hand-over was not checked: open Settings from the tray, press Esc, and see that you are back where you were.");
        }
        finally
        {
            other.Close();
        }
    }
}
