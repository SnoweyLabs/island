using System.Diagnostics;
using System.IO;
using System.Windows;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.App;

/// <summary>
/// The app as Dan runs it: one copy only, settings and log files, the island in its two windows, the
/// six keybinds, the tray icon and the refusals. Created by a normal start and, with a temporary data
/// folder, by the self-test.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private readonly AppFiles _files;
    private readonly SingleInstance _single;
    private readonly HotkeyHost _hotkeys = new();
    private readonly TrayIcon _tray;
    private readonly Action _quit;
    private readonly List<HotkeyResult> _results = [];
    private readonly IPackageFacts _package = new OutsidePackage();

    private RealWorld? _real;
    private Island.Agents.AgentPipeServer? _agents;
    private AgentNoticeHost? _notices;
    private PageStoreLoad _pagesLoad = new(PageStore.Default, PageStoreStatus.Missing, null);
    private PickStoreStatus _picksStatus = PickStoreStatus.Loaded;
    private SettingsScreen? _screen;
    private SceneStoreLoad _scenes = new(SceneStore.Empty, SceneStoreStatus.Missing, null);
    private readonly HeldKeyGuard _held = new();

    /// <summary>The picks as they are now (added, removed or moved from the island).</summary>
    public PickBook? Book { get; private set; }

    private AppHost(AppFiles files, SingleInstance single, IslandRuntime runtime, Settings settings, Action quit)
    {
        _files = files;
        _single = single;
        Runtime = runtime;
        Settings = settings;
        _quit = quit;
        _tray = new TrayIcon(runtime.Controller.ShowHide, OpenSettingsFile, quit, () => Settings.Glass, SetGlass, () => BlurOffered, OpenSettingsScreen, () => Settings.Mode, SetMode,
            () => [.. _scenes.Store.Items.Select(s => (s.Id, s.Name))], RunScene);
    }

    /// <summary>How the app runs: unpackaged, or as a package (for the start of the app, which asks before the host exists).</summary>
    internal static IPackageFacts NewPackageFacts() => new OutsidePackage();

    /// <summary>The settings screen (tray menu: Settings). Null until it was first needed.</summary>
    public SettingsScreen Screen => _screen ??= MakeScreen();

    public PageStore Pages => _pagesLoad.Store;

    /// <summary>The Terminals page's own reader (for the self-test, which checks that under it the page sees nothing real).</summary>
    internal TerminalsPage? Terminals { get; private set; }

    private HelperSessions? _helpers;

    private SettingsScreen MakeScreen()
    {
        var screen = new SettingsScreen(NewSession, () => Settings.Glass, ChosenScreen);
        screen.Changed += OnSessionChanged;
        screen.SetupFinished += () => Runtime.Controller.ShowHideByItself(); // "Done": the ball has flown to the top and the island takes its place, open
        screen.PracticeRequested += () => StartPractice(screen);
        return screen;
    }

    private void OpenSettingsScreen() => Screen.Open();

    private PracticeSession? _practice;

    /// <summary>The practice now running, for the self-test.</summary>
    internal PracticeSession? Practice => _practice;

    /// <summary>
    /// The step Try it of the setup (Dan's tutorial, WORK-ORDER-13): the screen has stepped back; a balloon and the real island take over until the practice ends or is skipped, and the screen returns on the next step.
    /// </summary>
    private void StartPractice(SettingsScreen screen)
    {
        _practice?.Dispose();
        var practice = _practice = new PracticeSession(Runtime, Settings.ShowHide.ToString());
        practice.Ended += () =>
        {
            if (ReferenceEquals(_practice, practice)) _practice = null;
            screen.ResumeAfterPractice();
        };
        practice.Start();
    }

    /// <summary>The first-start steps (a first start, or "Run the setup again" in General). Never opened by anything else.</summary>
    internal void OpenSetup() => Screen.OpenSetup();

    /// <summary>The full rectangle of the screen that holds the pointer, by the same code that places the island (WORK-ORDER-6 §1).</summary>
    private PixelRect? ChosenScreen() =>
        Runtime.Placer is { } placer && ScreenChooser.Choose(placer.Reader.Pointer, placer.Reader.Screens, 1, 1) is { IsFallback: false } placement
            ? placement.Screen.Full
            : null;

    /// <summary>
    /// What the first Ctrl+Q and the first opening of Settings would otherwise do cold, done once while the island is hidden (WORK-ORDER-12 section 3): an island of invented pages built over
    /// canvases that are in no window, and the settings view built over a session of the current settings and never shown. Shows nothing and changes nothing.
    /// </summary>
    private async Task WarmUpAfterAsync(TimeSpan wait)
    {
        await Task.Delay(wait);
        await WarmUpAsync();
    }

    internal Task WarmUpAsync() => IslandWarmUp.RunAsync(System.Windows.Threading.Dispatcher.CurrentDispatcher, () => new Island.SettingsUi.SettingsView(NewSession()));

    /// <summary>True while the graphics-card light can be had here (WORK-ORDER-12 section 2). False until it is fitted in, and wherever the compositor or its window could not be made.</summary>
    public bool GraphicsLightAvailable { get; set; }

    /// <summary>Puts the kind of moving light of the settings in force: the chosen kind, or half rate where the graphics card was chosen and cannot be had.</summary>
    internal void ApplyAnimations() => Runtime.Controller.AnimationsProbe = () => WindowsAnimations.On; // Dan's P1: Windows' "Show animations"

    private void ApplyLight() => Runtime.Controller.Light = OutsideGate.Current.SelfTest ? LightKind.AsBefore : GraphicsLightAvailable ? LightKind.GraphicsCard : LightKind.FixedRate; // no setting (WORK-ORDER-13): the island picks; the pictures of the self-test are drawn with the old light

    private SettingsSession NewSession()
    {
        var session = NewSessionCore();
        session.GraphicsLightAvailable = () => GraphicsLightAvailable;
        session.FindAddonFolder = () => AddonFolder.Find(AppContext.BaseDirectory, File.Exists); // the add-on's folder, beside the island or above it (Dan's P11)
        session.OpenFolderAt = path => _real?.World.Outside.OpenFolderAt(path) == true;
        // What "Add…" needs to store a place: the profile folder (written %USERPROFILE%) and where the known folders really are, read in memory.
        session.HandContextSource = () => new HandContext(PickPages.ProfileFolder, [.. Pick.KnownFolders.Select(k => (Name: k, Path: Island.Sources.Programs.KnownFolders.PathOf(k) ?? string.Empty)).Where(k => k.Path.Length > 0)]);
        return session;
    }

    private SettingsSession NewSessionCore() => new(
        new SettingsFiles(_files.SettingsPath, _files.PagesPath, _files.PicksPath, _files.ScenesPath),
        new SettingsLoad(Settings, SettingsStatus, null),
        _pagesLoad,
        new PickStoreLoad(Book?.Store ?? PickStore.Empty, _picksStatus, null),
        _hotkeys,
        () => _real?.Catalog.Installed ?? [],
        () => BlurOffered,
        StartupChoice.For(_package, () => new StartupSwitch(new OutsideStartupRegistry(), Environment.ProcessPath ?? string.Empty), () => new OutsideStartupTask()),
        RunningPrograms,
        OutsideGate.Current.SelfTest ? null : new OutsideAgentConnector(_package), // the self-test has none: it never touches a real file
        _scenes,
        _real?.Tabs, // the add-on's listener: how many browsers are connected (a number); null under the self-test
        OutsideGate.Current.SelfTest ? null : [new OutsideCodexConnector(_package)]); // Codex's own connector (WORK-ORDER-11 section 5); none under the self-test

    /// <summary>The programs that have a window now, by executable file name (never a path), for the "never over this" list. Read in memory; nothing is written.</summary>
    private IReadOnlyList<RunningProgram> RunningPrograms()
    {
        var own = Path.GetFileName(Environment.ProcessPath);
        var named = (_real?.Catalog.Installed ?? []).Where(p => p.ExeName is not null).ToLookup(p => p.ExeName!, StringComparer.OrdinalIgnoreCase);
        return [.. (_real?.World.Windows.Windows ?? [])
            .Where(w => !string.IsNullOrEmpty(w.ExeName) && !string.Equals(w.ExeName, own, StringComparison.OrdinalIgnoreCase))
            .Select(w => new RunningProgram(named[w.ExeName!].FirstOrDefault()?.Name ?? Path.GetFileNameWithoutExtension(w.ExeName!), w.ExeName!))];
    }

    /// <summary>What the settings screen changed is already saved; here the running island takes it over, with no restart.</summary>
    private void OnSessionChanged(SettingsArea area, SettingsSession session)
    {
        switch (area)
        {
            case SettingsArea.Keys:
                Settings = session.Settings;
                break;
            case SettingsArea.Glass:
                Settings = Settings with { Glass = session.Settings.Glass };
                Runtime.SetGlass(session.EffectiveGlass);
                break;
            case SettingsArea.Pages:
                Settings = session.Settings; // a new page may have been given a key at the same time
                _pagesLoad = new PageStoreLoad(session.Pages, _pagesLoad.Status, null);
                Runtime.SetPages(session.Pages.Pages);
                break;
            case SettingsArea.Picks:
                Settings = session.Settings; // a pick that went may have given its key back at the same time
                Book?.Adopt(session.Picks);
                break;
            case SettingsArea.Scenes:
                Settings = session.Settings; // a scene that went gave its key back at the same time
                _scenes = new SceneStoreLoad(session.Scenes, _scenes.Status, null);
                break;
            case SettingsArea.General:
                Settings = session.Settings;
                Runtime.SetIdleSeconds(Settings.IdleSeconds);
                ApplyLight();
                ApplyAnimations();
                WindowsTextSize.Start();
                Runtime.ShowPill = Settings.ShowPill;
                if (_notices is not null) _notices.Seconds = Settings.NoticeSeconds;
                Runtime.RefreshPill();
                break;
            case SettingsArea.Mode:
                Settings = session.Settings;
                ApplyMode(instant: false);
                break;
        }
    }

    public IslandRuntime Runtime { get; }
    public Settings Settings { get; private set; }

    /// <summary>True while the Blur glass is offered in the tray menu (decided when the blur layer is fitted in; off until then).</summary>
    public bool BlurOffered { get; set; }

    /// <summary>Switches the glass now and remembers the choice, unless the settings file could not be read (it is then left exactly as it was).</summary>
    public void SetGlass(GlassKind kind)
    {
        Settings = Settings with { Glass = kind };
        Runtime.SetGlass(kind);
        if (SettingsStatus != SettingsStatus.Unreadable && !Settings.Save(_files.SettingsPath)) _files.Log("could not save the glass choice");
    }

    /// <summary>
    /// Puts the mode and the "never over this" list of the settings in force: the table the gate asks, and the mark on the island's edge
    /// (WORK-ORDER-7 section 1). With <paramref name="instant"/> there is no cross-fade (the start of the app).
    /// </summary>
    private void ApplyMode(bool instant)
    {
        Runtime.Gate.Mode = Settings.Mode;
        Runtime.Gate.NeverOver = Settings.NeverOver;
        Runtime.Controller.SetMode(Settings.Mode, instant);
        Runtime.ShowPill = Settings.ShowPill;
        if (!instant) Runtime.RefreshPill(); // a change of mode asks the table again
    }

    /// <summary>Switches the mode now (the tray menu, or the key for the next mode) and remembers it, unless the settings file could not be read (it is then left exactly as it was).</summary>
    public void SetMode(Mode mode)
    {
        Settings = Settings with { Mode = mode };
        ApplyMode(instant: false);
        if (SettingsStatus != SettingsStatus.Unreadable && !Settings.Save(_files.SettingsPath)) _files.Log("could not save the mode");
    }

    /// <summary>Focus, then Vibe, then DND, then Focus again: what the key for the next mode does.</summary>
    internal void CycleMode() => SetMode(Settings.Mode switch { Mode.Focus => Mode.Vibe, Mode.Vibe => Mode.DND, _ => Mode.Focus });

    public SettingsStatus SettingsStatus { get; private set; }
    public IReadOnlyList<HotkeyResult> HotkeyResults => _results;
    public bool TrayVisible => _tray.Visible && _tray.HasDrawnIcon;
    public AppFiles Files => _files;

    /// <summary>Starts the app. Returns null when another copy already runs (it has been told).</summary>
    public static AppHost? Start(AppFiles files, Action quit, bool summonOnStart)
    {
        var single = SingleInstance.TryAcquire();
        if (single is null)
        {
            SingleInstance.SignalRunningCopy(quit: false);
            return null;
        }

        // Whether this is a first start is decided before the app writes its own settings file (WORK-ORDER-7 section 6).
        var firstStart = FirstStart.ShouldRun(File.Exists(files.SettingsPath), startedByWindows: !summonOnStart, selfTest: OutsideGate.Current.SelfTest);

        // The self-test's own temporary settings name the mode Focus, so that every check of the look sees the look it was written for.
        if (Settings.EnsureExists(files.SettingsPath) && OutsideGate.Current.SelfTest) (Settings.Defaults with { Mode = Mode.Focus }).Save(files.SettingsPath); // the self-test's own settings name the mode Focus and the light "as before", so every check sees the look it was written for
        var pagesLoad = PageStore.Load(files.PagesPath);
        if (pagesLoad.Status == PageStoreStatus.Unreadable) files.Log("the pages file could not be read and was left untouched");
        // Under the self-test, a main key that Windows says another program holds (Dan's copy) is replaced by a stand-in
        // in the temporary settings BEFORE any key is registered.
        if (OutsideGate.Current.SelfTest) SelfTestIsolation.UseStandInKeyIfTaken(files.SettingsPath, pagesLoad.Store.Pages);
        var load = Settings.Load(files.SettingsPath, pagesLoad.Store.Pages, bindIdleTime: !OutsideGate.Current.SelfTest);

        // The readers of this laptop, then the picks (the starter list is made once, from the installed programs).
        var selfTest = OutsideGate.Current.SelfTest;
        var real = RealWorld.Create(listenForAddon: !selfTest, files.Log);
        var scenesLoad = SceneStore.Load(files.ScenesPath);
        if (scenesLoad.Status == SceneStoreStatus.Unreadable) files.Log("the scenes file could not be read and was left untouched");
        var picksLoad = PickStore.OpenOrStart(files.PicksPath, StartersInstalled(real, files, selfTest), selfTest);
        if (picksLoad.Status == PickStoreStatus.Unreadable) files.Log("the picks file could not be read and was left untouched");
        var book = new PickBook(picksLoad.Store, files.PicksPath, canSave: picksLoad.Status != PickStoreStatus.Unreadable);
        book.SaveFailed += () => files.Log("the picks could not be saved");

        // The window is sized once, for the largest shape any page can take (WORK-ORDER-5): seven picks, the + tile and the Media controls.
        var pages = new PickPages(() => book.Store, real.World, ownExe: Path.GetFileName(Environment.ProcessPath));
        // The Terminals page fills itself from what is open. Under the self-test it is handed no windows and no probe: it never sees the real desktop (WORK-ORDER-11).
        var helpers = new HelperSessions(Island.Core.Terminals.TerminalTables.Default);
        var terminals = pages.Terminals = new TerminalsPage(real.World, selfTest ? null : new Island.Sources.Programs.TerminalProbe(), Island.Core.Terminals.TerminalTables.Default, readsWindows: !selfTest, helpers);
        if (!WindowMetrics.TryConfigure(WindowMetrics.LargestCapsuleWidth)) files.Log("the window size was already fixed");
        var runtime = new IslandRuntime(load.Settings.IdleSeconds, pages, book, pagesLoad.Store.Pages);
        var host = new AppHost(files, single, runtime, load.Settings, quit)
        {
            SettingsStatus = load.Status,
            _real = real,
            Book = book,
            _pagesLoad = pagesLoad,
            _picksStatus = picksLoad.Status,
            _scenes = scenesLoad,
            Terminals = terminals,
            _helpers = helpers,
        };

        pages.Refused += refusal => host.Refuse(refusal);
        pages.StartTargetProbe(new PickTargetProbe());
        book.Changed += host.ReleaseKeysOfMissingPicks; // a pick removed on the island (dragged off) gives its key back at once
        // Nobody looks at what File Explorer shows while the island is hidden, so its reader rests then (WORK-ORDER-6 section 6).
        real.Folders.SetQuiet(true);
        runtime.Controller.SettingsShowing = () => host._screen is { Phase: not SettingsScreen.SettingsScreenPhase.Closed, SteppedBack: false }; // none of the island's keys act while the screen shows
        runtime.Controller.Summoned += () => real.Folders.SetQuiet(false);
        runtime.Controller.Left += () => real.Folders.SetQuiet(true);
        runtime.PlacementLog = files.Log;
        runtime.Log = files.Log;
        runtime.Show();
        host.BlurOffered = runtime.BlurAvailable;
        runtime.BlurFellBack += reason =>
        {
            files.Log("blur glass not available: " + reason);
            host.Refuse(Refusals.BlurUnavailable);
        };
        single.AnotherCopyStarted += () => host.Refuse(Refusals.AlreadyRunning);
        single.QuitRequested += quit;
        runtime.SetGlass(load.Settings.Glass);
        host.ApplyMode(instant: true);
        if (!selfTest)
        {
            runtime.EnableMovingLight();
            host.GraphicsLightAvailable = runtime.MovingLightAvailable;
            if (!runtime.MovingLightAvailable) files.Log("graphics-card light not available: " + runtime.MovingLightUnavailableReason);
        }

        host.ApplyLight();
        host.ApplyAnimations();
        host.StartAgentNotices(selfTest);
        host.RegisterHotkeys();
        // A file from a newer version of Island is left as it is and the app runs on defaults (WORK-ORDER-8 section 1): said once, in its own words.
        var newer = FileSchema.IsNewer(load.Detail) || FileSchema.IsNewer(pagesLoad.Detail) || FileSchema.IsNewer(picksLoad.Detail) || FileSchema.IsNewer(scenesLoad.Detail);
        if (newer) host.Refuse(Refusals.SettingsFromNewerVersion);
        else if (load.Status == SettingsStatus.Unreadable) host.Refuse(Refusals.SettingsUnreadable, load.Detail);

        files.Log("started");
        if (firstStart) host.OpenSetup(); // the island comes when "Done" is pressed
        else if (summonOnStart) runtime.Controller.ShowHideByItself(); // so Dan sees it is alive; it leaves after the idle time
        else if (!selfTest) _ = host.WarmUpAfterAsync(TimeSpan.FromSeconds(5)); // a silent start: the first Ctrl+Q will find its code compiled
        return host;
    }

    /// <summary>The installed programs, waited for only when the starter list has to be made (no picks file yet, not the self-test).</summary>
    private static IReadOnlyList<InstalledProgram> StartersInstalled(RealWorld real, AppFiles files, bool selfTest)
    {
        if (selfTest || File.Exists(files.PicksPath)) return [];
        real.Catalog.WaitForLoad(TimeSpan.FromSeconds(10));
        return real.Catalog.Installed;
    }

    /// <summary>
    /// The notice "Your agent is done": the island listens on the pipe Island.Notify writes to. The real pipe name is used by the app started for real
    /// and by nothing else: the self-test and every test listen on names of their own (WORK-ORDER-7 section 4).
    /// </summary>
    private void StartAgentNotices(bool selfTest)
    {
        if (selfTest || _real is null) return;
        _notices = new AgentNoticeHost(Runtime, _real.World, Settings.NoticeSeconds);
        _agents = new Island.Agents.AgentPipeServer(Island.Core.AgentPipe.ForThisUser());
        var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _agents.NoticeReceived += notice => ui.BeginInvoke(() => _notices.Post(notice));
        if (_helpers is { } sessions) _agents.MessageReceived += sessions.Apply; // the conversations of helpers inside terminals, in memory (WORK-ORDER-11 section 3)
        if (!_agents.Start()) _files.Log("could not listen for coding agents (the name is taken)");
    }

    private void RegisterHotkeys()
    {
        _hotkeys.Pressed += OnHotkey;

        _results.Add(_hotkeys.Register(KeybindEditor.MainId, Settings.ShowHide));
        if (Settings.ModeKey is { } modeKey) _results.Add(_hotkeys.Register(KeybindEditor.ModeNextId, modeKey));
        foreach (var page in _pagesLoad.Store.Pages)
        {
            if (Settings.KeyFor(page.Id) is { } combo) _results.Add(_hotkeys.Register(page.Id, combo));
        }

        ReleaseKeysOfMissingPicks(); // keys of picks that are gone are dropped before any is registered
        foreach (var key in Settings.PickKeys) _results.Add(_hotkeys.Register(key.PickId, key.Combo));

        foreach (var r in _results)
        {
            _files.Log($"hotkey {(KeybindEditor.IsSceneAction(r.Action) ? "scene" : PickKeysJson.IsPickId(r.Action) ? "pick" : r.Action)} {r.Combo} {(r.Accepted ? "accepted" : "refused by Windows, last error " + r.LastError)}");
            if (r.Accepted) continue;
            var name = KeybindEditor.IsSceneAction(r.Action)
                ? _scenes.Store.ById(KeybindEditor.SceneIdOf(r.Action))?.Name ?? "a scene"
                : PickKeysJson.IsPickId(r.Action)
                ? Book?.Store.ById(r.Action)?.Name ?? "a pick"
                : r.Action == KeybindEditor.ModeNextId ? SettingsText.ModeNextActionName
                : _pagesLoad.Store.Pages.FirstOrDefault(p => p.Id == r.Action)?.Name ?? "Show / hide";
            Refuse(Refusals.ForHotkeyTaken(r.Combo, name));
        }
    }

    /// <summary>A pick that is gone gives its key back to Windows at once and its entry leaves the settings file (never when that file could not be read).</summary>
    private void ReleaseKeysOfMissingPicks()
    {
        if (Book is not { } book || SettingsStatus == SettingsStatus.Unreadable || Settings.PickKeys.Count == 0) return;
        var editor = new KeybindEditor(_hotkeys, id => id);
        var change = editor.ReleaseKeysOfMissingPicks(Settings, id => KeybindEditor.IsSceneAction(id) ? _scenes.Store.ById(KeybindEditor.SceneIdOf(id)) is not null : book.Store.ById(id) is not null, next => next.Save(_files.SettingsPath));
        if (change.Changed) Settings = change.Settings;
        if (change.Refusal is not null) _files.Log("a key of a pick that is gone could not be released");
    }

    /// <summary>Hands a combination to the app the way Windows does when a registered key is pressed (the self-test, which presses no key).</summary>
    internal void Press(HotkeyCombo combo) => OnHotkey(combo);

    /// <summary>What a pressed combination means is read from the settings in force, so a changed key needs no other bookkeeping.</summary>
    private void OnHotkey(HotkeyCombo combo)
    {
        var action = KeybindEditor.ActionFor(Settings, combo);
        if (action == KeybindEditor.MainId) Runtime.Controller.MainKey();
        else if (action == KeybindEditor.ModeNextId) CycleMode();
        else if (action is not null && KeybindEditor.IsSceneAction(action)) RunSceneKey(action);
        else if (action is not null && PickKeysJson.IsPickId(action)) Runtime.PickKey(action);
        else if (action is not null) Runtime.Controller.PageKey(action);
    }

    /// <summary>A scene's key: a key held down runs the scene once, not again and again.</summary>
    private void RunSceneKey(string action)
    {
        if (_held.Accept(action, Environment.TickCount64)) RunScene(KeybindEditor.SceneIdOf(action));
    }

    /// <summary>Runs a scene (its key, or the tray menu): opens what is closed, brings forward what is open, and says once what was skipped.</summary>
    internal void RunScene(string sceneId)
    {
        if (_scenes.Store.ById(sceneId) is not { } scene) return;
        var plan = Runtime.RunScene(scene, _real?.Catalog.Installed ?? []);
        if (plan?.PartMissing is { } missing) Refuse(missing);
    }

    /// <summary>Shows a refusal from the tray icon and writes it to the log.</summary>
    public void Refuse(Refusal refusal, string? detail = null)
    {
        _files.Log($"refusal {refusal.Code}: {refusal.Message}" + (detail is null ? string.Empty : $" ({detail})"));
        LastRefusalCode = refusal.Code;
        _tray.Notify(refusal);
    }

    /// <summary>The tray icon (for the self-test, which counts what was handed to it).</summary>
    internal TrayIcon Tray => _tray;

    /// <summary>The code of the latest refusal shown (for the self-test).</summary>
    internal string? LastRefusalCode { get; private set; }

    private void OpenSettingsFile()
    {
        // A package keeps its files in a private place that another program may not see under the usual path: the screen opens instead (WORK-ORDER-8 section 3).
        if (TrayChoice.OpenSettingsFile(_package.IsPackaged) == TrayChoice.SettingsFileAction.OpenTheScreen)
        {
            OpenSettingsScreen();
            return;
        }

        try
        {
            Settings.EnsureExists(_files.SettingsPath);
            OutsideShell.OpenFile(_files.SettingsPath);
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            _files.Log("could not open the settings file: " + e.GetType().Name); // the kind only: a message can carry a path
        }
    }

    public void Dispose()
    {
        _screen?.Close();
        _agents?.Dispose();
        _notices?.Dispose();
        _hotkeys.Dispose();
        _tray.Dispose();
        Runtime.Dispose();
        _real?.Dispose();
        _single.Dispose();
        _files.Log("stopped");
    }
}
