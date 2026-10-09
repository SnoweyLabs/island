namespace Island.Core.SettingsEdit;

/// <summary>The files the settings screen keeps. The caller gives the paths (a temporary folder under the self-test).</summary>
public sealed record SettingsFiles(string SettingsPath, string PagesPath, string PicksPath, string? ScenesPath = null);

/// <summary>A program that is running now, as the "never over this" list wants it: a name to show and an executable file name (never a path).</summary>
public sealed record RunningProgram(string Name, string ExeFileName);

/// <summary>What a change touched, so the app knows what to redraw or re-register.</summary>
public enum SettingsArea
{
    Keys,
    Pages,
    Picks,
    Glass,
    General,
    Mode,
    Scenes,
}

/// <summary>
/// What one edit did. <see cref="Refusal"/> is plain words and null when the edit was accepted;
/// <see cref="Warning"/> is a one-line note that does not block; <see cref="Waiting"/> is true while a key press
/// was only a modifier and the screen should keep listening.
/// </summary>
public sealed record SessionResult(bool Changed, string? Refusal, string? Warning = null, bool Waiting = false)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// Everything the settings screen can do, behind one object, so the screen only draws and the app only owns
/// this. A change is in force and saved before the call returns: the keys through the registrar, the files
/// through their stores. A file that could not be read at start is never overwritten: edits to it are refused.
/// Use it from the interface thread only. Start a new session each time the screen opens, so it holds the
/// app's current picks.
/// </summary>
public sealed class SettingsSession
{
    private readonly SettingsFiles _files;
    private readonly KeybindEditor _keys;
    private readonly Func<IReadOnlyList<InstalledProgram>> _installed;
    private readonly Func<bool> _blurAvailable;
    private readonly IStartupSwitch? _startup;
    private readonly IAgentConnector? _agents;
    private readonly IReadOnlyList<IAgentConnector> _moreAgents;
    private readonly Func<IReadOnlyList<RunningProgram>> _running;
    private readonly bool _settingsLocked;
    private readonly bool _pagesLocked;
    private readonly bool _picksLocked;
    private readonly bool _scenesLocked;

    public SettingsSession(
        SettingsFiles files,
        SettingsLoad settings,
        PageStoreLoad pages,
        PickStoreLoad picks,
        IHotkeyRegistrar registrar,
        Func<IReadOnlyList<InstalledProgram>> installed,
        Func<bool> blurAvailable,
        IStartupSwitch? startup = null,
        Func<IReadOnlyList<RunningProgram>>? running = null,
        IAgentConnector? agents = null,
        SceneStoreLoad? scenes = null,
        IAddonStatus? addon = null,
        IReadOnlyList<IAgentConnector>? moreAgents = null)
    {
        Addon = addon;
        _agents = agents;
        _moreAgents = moreAgents ?? [];
        _scenesLocked = scenes?.Status == SceneStoreStatus.Unreadable;
        Scenes = scenes?.Store ?? SceneStore.Empty;
        _running = running ?? (() => []);
        _startup = startup;
        _files = files;
        _installed = installed;
        _blurAvailable = blurAvailable;
        _settingsLocked = settings.Status == SettingsStatus.Unreadable;
        _pagesLocked = pages.Status == PageStoreStatus.Unreadable;
        _picksLocked = picks.Status == PickStoreStatus.Unreadable;
        Settings = settings.Settings;
        Pages = pages.Store;
        TerminalsNoRoom = pages.TerminalsNoRoom;
        Picks = picks.Store;
        _keys = new KeybindEditor(registrar, ActionName);
    }

    /// <summary>The pages file holds as many pages as the island can hold, so the Terminals page was not added (TERMINALS_PAGE_NO_ROOM): the list of pages says so.</summary>
    public bool TerminalsNoRoom { get; }

    public Settings Settings { get; private set; }

    public PageStore Pages { get; private set; }

    public PickStore Picks { get; private set; }

    /// <summary>The scenes (WORK-ORDER-7 section 5).</summary>
    public SceneStore Scenes { get; private set; }

    /// <summary>Raised after a change is in force and saved.</summary>
    public event Action<SettingsArea>? Changed;

    public string ActionName(string actionId) =>
        actionId == KeybindEditor.MainId ? SettingsText.MainActionName
        : actionId == KeybindEditor.ModeNextId ? SettingsText.ModeNextActionName
        : KeybindEditor.IsSceneAction(actionId) ? Scenes.ById(KeybindEditor.SceneIdOf(actionId))?.Name ?? actionId
        : PickKeysJson.IsPickId(actionId) ? Picks.ById(actionId)?.Name ?? actionId
        : Pages.ById(actionId)?.Name ?? actionId;

    // ---- Keys --------------------------------------------------------------

    /// <summary>A key press while a row is waiting for a new combination.</summary>
    public SessionResult PressKey(string actionId, KeyPress press)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (actionId != KeybindEditor.MainId && actionId != KeybindEditor.ModeNextId)
        {
            ReleaseKeysOfMissingPicks();
            if (KeybindEditor.IsSceneAction(actionId))
            {
                if (Scenes.ById(KeybindEditor.SceneIdOf(actionId)) is null) return new SessionResult(false, SceneText.NoSuchScene);
            }
            else if (PickKeysJson.IsPickId(actionId) ? Picks.ById(actionId) is null : Pages.ById(actionId) is null)
                return new SessionResult(false, PickKeysJson.IsPickId(actionId) ? SettingsText.NoSuchPick : SettingsText.NoSuchPage);
        }

        if (KeybindEditor.Capture(press) is { Waiting: true }) return new SessionResult(false, null, Waiting: true);
        return KeyResult(_keys.AssignPress(Settings, actionId, press, SaveSettings));
    }

    /// <summary>Takes a page's or a pick's key away (the same as "restore default" for either: empty).</summary>
    public SessionResult ClearKey(string actionId)
    {
        _keysOfSwitchedOff.Remove(actionId);
        return Locked(_settingsLocked, "settings.json") ?? KeyResult(_keys.Clear(Settings, actionId, SaveSettings));
    }

    /// <summary>
    /// A pick that is no longer there gives its key back to Windows. Called after every change of the picks and before every key edit,
    /// so a key that could not be let go of the first time is tried again. Returns the words to tell the person when a key is still held.
    /// </summary>
    private string? ReleaseKeysOfMissingPicks()
    {
        if (_settingsLocked || Settings.PickKeys.Count == 0) return null;
        var change = _keys.ReleaseKeysOfMissingPicks(Settings, ThingExists, SaveSettings);
        if (change.Changed)
        {
            Settings = change.Settings;
            Changed?.Invoke(SettingsArea.Keys);
        }

        return change.Refused ? SettingsText.KeyStillHeld : null;
    }

    /// <summary>Whether what a pick key or a scene key belongs to is still there.</summary>
    private bool ThingExists(string actionId) =>
        KeybindEditor.IsSceneAction(actionId) ? Scenes.ById(KeybindEditor.SceneIdOf(actionId)) is not null : Picks.ById(actionId) is not null;

    public SessionResult RestoreKey(string actionId) =>
        Locked(_settingsLocked, "settings.json") ?? KeyResult(_keys.RestoreDefault(Settings, actionId, SaveSettings));

    /// <summary>How many keys "Restore the original keys" would take away or put back: the main key when it was changed, every page key, pick key and the mode key.</summary>
    public int KeysSetByYou =>
        (Settings.ShowHide.Equals(Settings.Defaults.ShowHide) ? 0 : 1) + Settings.PageKeys.Count(k => k.Combo is not null) + Settings.PickKeys.Count + (Settings.ModeKey is null ? 0 : 1);

    public SessionResult RestoreAllKeys()
    {
        var result = Locked(_settingsLocked, "settings.json") ?? KeyResult(_keys.RestoreDefaults(Settings, SaveSettings));
        if (result.Ok) _keysOfSwitchedOff.Clear(); // the keys of switched-off picks are taken away with the rest: none comes back (only when the restore was done)
        return result;
    }

    private SessionResult KeyResult(KeyChange change)
    {
        if (change.Changed) Settings = change.Settings;
        if (change.Refusal is { } refusal) return new SessionResult(change.Changed, refusal.Message);
        if (change.Changed) Changed?.Invoke(SettingsArea.Keys);
        return new SessionResult(change.Changed, null);
    }

    private bool SaveSettings(Settings next) => next.Save(_files.SettingsPath);

    // ---- Pages -------------------------------------------------------------

    /// <summary>A new page, and its own global key when one is given. A key that is refused means no page is made.</summary>
    public SessionResult CreatePage(string name, string colour, HotkeyCombo? key = null)
    {
        if (Locked(_pagesLocked, "pages.json") is { } locked) return locked;
        if (key is not null && Locked(_settingsLocked, "settings.json") is { } keyLocked) return keyLocked;

        var edit = Pages.Create(name, colour);
        if (edit.Page is not { } page) return new SessionResult(false, edit.Refusal);

        if (key is { } combo)
        {
            var assigned = _keys.Assign(Settings, page.Id, combo, SaveSettings);
            if (assigned.Refusal is { } refusal) return new SessionResult(false, refusal.Message);
            Settings = assigned.Settings;
        }

        if (!edit.Store.Save(_files.PagesPath))
        {
            if (key is not null) DropPageKey(page.Id);
            return new SessionResult(false, NotSaved("pages.json"));
        }

        Pages = edit.Store;
        Changed?.Invoke(SettingsArea.Pages);
        return new SessionResult(true, null, edit.Warning);
    }

    public SessionResult RenamePage(string id, string name) =>
        Locked(_pagesLocked, "pages.json") ?? ApplyPageEdit(Pages.Rename(id, name));

    public SessionResult RecolourPage(string id, string colour) =>
        Locked(_pagesLocked, "pages.json") ?? ApplyPageEdit(Pages.Recolour(id, colour));

    private SessionResult ApplyPageEdit(PageEdit edit)
    {
        if (edit.Refusal is { } refusal) return new SessionResult(false, refusal);
        if (!edit.Store.Save(_files.PagesPath)) return new SessionResult(false, NotSaved("pages.json"));
        Pages = edit.Store;
        Changed?.Invoke(SettingsArea.Pages);
        return new SessionResult(true, null, edit.Warning);
    }

    /// <summary>The yes/no question for removing a page, with how many picks go with it; null for a built-in page.</summary>
    public DeleteQuestion? AskDeletePage(string id) => Pages.AskDelete(id, Picks);

    /// <summary>
    /// Removes the page, its picks and its keys, after the screen has asked and Dan said yes. All or nothing: the saves that can fail come
    /// first and a later failure puts the earlier file back, so "nothing was changed" is true; the keys are let go of last.
    /// </summary>
    public SessionResult DeletePage(string id)
    {
        var locked = Locked(_pagesLocked, "pages.json") ?? Locked(_picksLocked, "picks.json") ?? Locked(_settingsLocked, "settings.json");
        if (locked is not null) return locked;

        var deletion = Pages.Delete(id, Picks, confirmed: true);
        if (deletion.Refusal is { } refusal) return new SessionResult(false, refusal);

        // The page's own key first, because that is the save most likely to fail with the settings file unwritable: if it does, nothing
        // has been touched yet. A later failure puts everything back (the files, and the key through the editor).
        var oldKey = Settings.KeyFor(id);
        if (oldKey is not null)
        {
            var cleared = _keys.Clear(Settings, id, SaveSettings);
            if (cleared.Refusal is { } keyProblem) return new SessionResult(false, keyProblem.Message);
            Settings = cleared.Settings;
        }

        void PutKeyBack()
        {
            if (oldKey is not { } key) return;
            var again = _keys.Assign(Settings, id, key, SaveSettings);
            if (!again.Refused) Settings = again.Settings;
        }

        var oldPicks = Picks;
        // Picks first: a page that is left without its picks is harmless, picks without a page are not.
        if (!deletion.Picks.Save(_files.PicksPath))
        {
            PutKeyBack();
            return new SessionResult(false, NotSaved("picks.json"));
        }

        if (!deletion.Pages.Save(_files.PagesPath))
        {
            oldPicks.Save(_files.PicksPath);
            PutKeyBack();
            return new SessionResult(false, NotSaved("pages.json"));
        }

        Picks = deletion.Picks;
        Pages = deletion.Pages;

        // Now what is left to let go of: the page's entry in the settings, and the keys of the picks that went with it. A key that cannot
        // be let go of is said so, not hidden.
        DropPageKey(id);
        foreach (var gone in _switchedOff.Where(p => p.PageId == id).ToList()) _keysOfSwitchedOff.Remove(gone.Id); // a new page may get this page's id: nothing of this one is kept for it
        _switchedOff.RemoveAll(p => p.PageId == id);
        var picksReleased = ReleaseKeysOfMissingPicks();
        Changed?.Invoke(SettingsArea.Pages);
        if (deletion.RemovedPicks > 0) Changed?.Invoke(SettingsArea.Picks);
        return new SessionResult(true, null, picksReleased);
    }

    /// <summary>Lets go of a removed page's key and takes its entry out of the settings, so the file does not keep a line for a page that is gone.</summary>
    private void DropPageKey(string id)
    {
        Settings = _keys.Clear(Settings, id, SaveSettings).Settings;
        var without = Settings with { PageKeys = [.. Settings.PageKeys.Where(k => k.PageId != id)] };
        if (without.PageKeys.Count != Settings.PageKeys.Count && SaveSettings(without)) Settings = without;
    }

    // ---- On the island -----------------------------------------------------

    /// <summary>The picks a person switched off during this visit that are not on the starter list: the chip stays faint so that it can be switched on again (a starter pick always has one). Gone when the screen closes.</summary>
    private readonly List<Pick> _switchedOff = [];

    public IReadOnlyList<OnIslandRow> PickRows(string pageId)
    {
        var rows = PicksOnIsland.RowsFor(pageId, Picks, _installed());
        var kept = _switchedOff.Where(p => p.PageId == pageId && Picks.ById(p.Id) is null && rows.All(r => r.Pick.Id != p.Id)).Select(p => new OnIslandRow(p, false));
        return [.. rows, .. kept];
    }

    /// <summary>
    /// Moves a pick to another page (WORK-ORDER-5 §6: the right-click menu that did this is gone). The pick stays on exactly one
    /// page; nothing that is open is touched. Saved at once, like every change of this screen.
    /// </summary>
    public SessionResult MovePick(string pickId, string pageId)
    {
        if (Locked(_picksLocked, "picks.json") is { } locked) return locked;
        if (Pages.ById(pageId) is null) return new SessionResult(false, SettingsText.NoSuchPage);
        if (!PageIds.CanHoldPicks(pageId)) return new SessionResult(false, SettingsText.PageFillsItself);
        if (Picks.ById(pickId) is not { } pick) return new SessionResult(false, SettingsText.NoSuchPick);
        if (pick.PageId == pageId) return new SessionResult(false, null); // already there: nothing to save

        var moved = Picks.Move(pickId, pageId);
        return moved.ById(pickId)?.PageId == pageId ? ApplyPicks(moved) : new SessionResult(false, SettingsText.PickCannotMove);
    }

    /// <summary>The keys of the picks switched off during this visit, given back when the pick is switched on again (while it is off its key is let go).</summary>
    private readonly Dictionary<string, HotkeyCombo> _keysOfSwitchedOff = [];

    public SessionResult SetPick(OnIslandRow row, bool on)
    {
        var id = row.Pick.Id;
        var keyBefore = Settings.PickKeyFor(id);
        var result = Locked(_picksLocked, "picks.json") ?? ApplyPicks(PicksOnIsland.Switch(Picks, row, on));
        if (!result.Ok) return result;
        _switchedOff.RemoveAll(p => p.Id == id);
        if (on && Picks.ById(id) is null)
        {
            // The same program was added again meanwhile (another id): the chip cannot be switched on, and says so, and goes.
            _keysOfSwitchedOff.Remove(id);
            var there = Picks.Picks.FirstOrDefault(p => p.IsSameThing(row.Pick));
            return new SessionResult(false, SettingsText.AlreadyOnTheIsland(row.Pick.Name, there is null ? null : Pages.ById(there.PageId)?.Name, there?.Name));
        }

        if (!on)
        {
            _switchedOff.Add(row.Pick);
            if (keyBefore is { } kept) _keysOfSwitchedOff[id] = kept;
            return result;
        }

        if (!_keysOfSwitchedOff.Remove(id, out var key) || Settings.PickKeyFor(id) is not null) return result;
        var back = _keys.Assign(Settings, id, key, SaveSettings);
        if (back.Refused) return result with { Warning = back.Refusal!.Code == SettingsText.KeyNotSaved.Code ? SettingsText.KeyNotGivenBackNotSaved(key.ToString()) : SettingsText.KeyNotGivenBackBecause(key.ToString(), back.Refusal.Message) };
        if (back.Changed)
        {
            Settings = back.Settings;
            Changed?.Invoke(SettingsArea.Keys);
        }

        return result;
    }

    public RestoreQuestion? AskRestore(string pageId) =>
        Pages.ById(pageId) is { } page && PicksOnIsland.CanRestore(page) ? PicksOnIsland.AskRestore(page, Picks, _installed()) : null;

    /// <summary>Brings back the starter list of one page, after the screen has asked and Dan said yes.</summary>
    public SessionResult RestorePage(string pageId) =>
        Locked(_picksLocked, "picks.json")
        ?? (AskRestore(pageId) is null
            ? new SessionResult(false, SettingsText.NoSuchPage)
            : ApplyPicks(PicksOnIsland.Restore(Picks, pageId, _installed())));

    private SessionResult ApplyPicks(PickStore next)
    {
        next = Tokenised(next);
        if (!next.Save(_files.PicksPath)) return new SessionResult(false, NotSaved("picks.json"));
        var changed = !next.Picks.SequenceEqual(Picks.Picks);
        Picks = next;
        string? warning = null;
        if (changed)
        {
            warning = ReleaseKeysOfMissingPicks();
            Changed?.Invoke(SettingsArea.Picks);
        }

        return new SessionResult(changed, null, warning);
    }

    // ---- Adding by hand (WORK-ORDER-10 §3) --------------------------------------

    /// <summary>Windows' own windows for choosing a file or a folder; set by the screen that owns them. Null: "Browse…", "A folder" and "A file" answer that none is available.</summary>
    public IPlaceChooser? Chooser { get; set; }

    /// <summary>The profile folder and the known folders' real places, for the pick that stores a place; supplied by the app, in memory.</summary>
    public Func<HandContext>? HandContextSource { get; set; }

    private HandContext HandContext => HandContextSource?.Invoke() ?? new HandContext(null, []);

    /// <summary>
    /// The picks with a place that spells out the profile folder (a hand-edited file) written with %USERPROFILE% instead, so it is the same thing as the same place chosen now and the
    /// Windows account name never stays in the file. The profile folder is read in memory from the app's own source; without it the picks are returned as they are.
    /// </summary>
    private PickStore Tokenised(PickStore store)
    {
        var profile = HandContextSource?.Invoke().ProfileFolder;
        if (profile is null || !store.Picks.Any(p => PickPath.SpellsOutProfile(p.Location, profile))) return store;
        return new PickStore(store.Picks.Select(p => PickPath.SpellsOutProfile(p.Location, profile) && PickPath.Compress(p.Location, profile) is { } token ? p with { Location = token } : p));
    }

    /// <summary>The installed programs the "Add…" list shows, by name, narrowed by what the person typed (case ignored; empty shows all).</summary>
    public IReadOnlyList<InstalledProgram> ProgramsToChoose(string? filter)
    {
        var text = filter?.Trim() ?? string.Empty;
        return [.. _installed().Where(p => text.Length == 0 || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>What the address field understood, shown before the person confirms: the host, or the reason (NOT_A_SITE's words).</summary>
    public SiteUnderstanding UnderstandSite(string? typed) => TypedSite.Understand(typed);

    public HandAddResult AddProgramFromList(string pageId, InstalledProgram program) => AddByHand(pageId, p => HandPicks.FromInstalledProgram(program, p));

    public HandAddResult AddProgramByBrowsing(string pageId) =>
        AddChosen(pageId, c => c.ChooseFile(PlaceChoice.Program), (path, p) => HandPicks.BrowsedProgram(path, p, HandContext));

    public HandAddResult AddFolder(string pageId) =>
        AddChosen(pageId, c => c.ChooseFolder(), (path, p) => HandPicks.Folder(path, p, HandContext));

    public HandAddResult AddFile(string pageId) =>
        AddChosen(pageId, c => c.ChooseFile(PlaceChoice.AnyFile), (path, p) => HandPicks.File(path, p, HandContext));

    /// <summary>
    /// A thing chosen in Windows' own window. The window opens only when the add can go on (a locked picks file or a page that is gone is answered first), it never opens without the
    /// app's own profile folder to hand (a place under it could not be kept the right way), and a window that fails is answered like one that could not be opened.
    /// </summary>
    private HandAddResult AddChosen(string pageId, Func<IPlaceChooser, string?> ask, Func<string, string, HandPickResult> make)
    {
        if (Chooser is not { } chooser || HandContextSource is null) return new HandAddResult(false, SettingsText.NoChoosingWindow);
        if (Blocked(pageId) is { } blocked) return blocked;
        string? path;
        try
        {
            path = ask(chooser);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new HandAddResult(false, SettingsText.NoChoosingWindow);
        }

        return path is null ? HandAddResult.Cancel : AddByHand(pageId, p => make(path, p));
    }

    private HandAddResult? Blocked(string pageId)
    {
        if (Locked(_picksLocked, "picks.json") is { } locked) return new HandAddResult(false, locked.Refusal);
        if (!PageIds.CanHoldPicks(pageId)) return new HandAddResult(false, SettingsText.PageFillsItself);
        return Pages.ById(pageId) is null ? new HandAddResult(false, SettingsText.NoSuchPage) : null;
    }

    public HandAddResult AddSite(string pageId, string? typed) => AddByHand(pageId, p => HandPicks.Site(typed, p));

    /// <summary>Makes the pick, refuses what cannot be (with its reason), says where a thing that is already on the island is, and saves the rest at once.</summary>
    private HandAddResult AddByHand(string pageId, Func<string, HandPickResult> make)
    {
        if (Blocked(pageId) is { } blocked) return blocked;
        var made = make(pageId);
        if (made.Pick is not { } pick) return new HandAddResult(false, made.Message ?? SettingsText.NothingAdded);
        if (Tokenised(Picks).Find(pick) is { } there)
        {
            var where = Pages.ById(there.PageId)?.Name ?? there.PageId;
            return new HandAddResult(false, SettingsText.AlreadyOnIsland(there.Name, where), there, where);
        }

        var next = Picks.Add(pick, out var added);
        if (!added) return new HandAddResult(false, SettingsText.NothingAdded);
        var applied = ApplyPicks(next);
        return applied.Ok ? new HandAddResult(true, applied.Warning, pick) : new HandAddResult(false, applied.Refusal);
    }

    // ---- General -----------------------------------------------------------

    /// <summary>Whether the switch for starting with Windows can be used here at all.</summary>
    public bool StartupAvailable => _startup is not null;

    /// <summary>What the registry holds right now: on only when its value is there and names this program (not what the settings file remembers).</summary>
    public bool StartWithWindowsOn => _startup?.IsOn() == true;

    /// <summary>What the switch says about the state Windows reports (the person switched it off in Windows); null when nothing.</summary>
    public string? StartupExplanation => _startup?.Explanation;

    /// <summary>
    /// Switches the start with Windows on or off, by the person's own press. Writes (or deletes) the one value Windows keeps for it,
    /// through the one outside door; the settings file keeps only the yes or no as a memory of the last choice. A path too long for
    /// the start-up list is refused with its words and nothing is written.
    /// </summary>
    public SessionResult SetStartWithWindows(bool on)
    {
        if (_startup is null) return new SessionResult(false, SettingsText.StartupNotAvailable);
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;

        // The file first: when it cannot be saved the registry is never touched, so "nothing was changed" is true. When the registry
        // then refuses, the file goes back to what it was.
        var next = Settings with { StartWithWindows = on };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        var done = on ? _startup.TurnOn() : _startup.TurnOff();
        if (!done.Ok)
        {
            SaveSettings(Settings);
            return new SessionResult(false, done.Refusal?.Message ?? SettingsText.StartupNotTaken);
        }

        var changed = next != Settings;
        Settings = next;
        if (changed) Changed?.Invoke(SettingsArea.General);
        return new SessionResult(changed, null);
    }

    /// <summary>How long the notice stays, 3 to 30 whole seconds (WORK-ORDER-7 section 4). In force at once, saved at once; a number that is not a number is refused.</summary>
    public SessionResult SetNoticeSeconds(double seconds)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (!double.IsFinite(seconds)) return new SessionResult(false, SettingsText.IdleNotUsable);
        var kept = NoticeQueue.Clamp(Math.Round(seconds));
        if (kept == Settings.NoticeSeconds) return new SessionResult(false, null);
        var next = Settings with { NoticeSeconds = kept };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        Settings = next;
        Changed?.Invoke(SettingsArea.General);
        return new SessionResult(true, null);
    }

    /// <summary>"Show the pill while something plays" (WORK-ORDER-7 section 2). In force at once, saved at once.</summary>
    public SessionResult SetShowPill(bool on)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (on == Settings.ShowPill) return new SessionResult(false, null);
        var next = Settings with { ShowPill = on };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        Settings = next;
        Changed?.Invoke(SettingsArea.General);
        return new SessionResult(true, null);
    }

    /// <summary>
    /// How long the island stays without use, in seconds (WORK-ORDER-6 section 4): kept between two and sixty whole seconds, in force
    /// at once and saved at once. A number that is not a number is refused.
    /// </summary>
    public SessionResult SetIdleSeconds(double seconds)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (!double.IsFinite(seconds)) return new SessionResult(false, SettingsText.IdleNotUsable);

        var kept = Settings.ClampIdle(seconds);
        if (kept == Settings.IdleSeconds) return new SessionResult(false, null);
        var next = Settings with { IdleSeconds = kept };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        Settings = next;
        Changed?.Invoke(SettingsArea.General);
        return new SessionResult(true, null);
    }

    // ---- Mode ----------------------------------------------------------------

    /// <summary>One mode is always on. In force at once and saved at once; the island takes it over without a restart.</summary>
    public SessionResult SetMode(Mode mode)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (!Enum.IsDefined(mode)) return new SessionResult(false, SettingsText.ModeUnknown);
        if (mode == Settings.Mode) return new SessionResult(false, null);

        var next = Settings with { Mode = mode };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        Settings = next;
        Changed?.Invoke(SettingsArea.Mode);
        return new SessionResult(true, null);
    }

    /// <summary>Programs running now that are not on the "never over this" list yet, each once, at most a dozen. Read fresh at each call.</summary>
    public IReadOnlyList<RunningProgram> RunningNotOnList() =>
        [.. _running()
            .Where(p => NeverOverList.IsValid(new NeverOverEntry(p.Name, p.ExeFileName)) && !Settings.NeverOver.Contains(p.ExeFileName))
            .DistinctBy(p => p.ExeFileName, StringComparer.OrdinalIgnoreCase)
            .Take(12)];

    /// <summary>Adds a program to the "never over this" list: a name and an executable file name (never a path). A repeat changes nothing.</summary>
    public SessionResult AddNeverOver(NeverOverEntry entry)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (!NeverOverList.IsValid(entry)) return new SessionResult(false, SettingsText.NeverOverRefused);
        if (Settings.NeverOver.Contains(entry.ExeFileName)) return new SessionResult(false, null);
        if (Settings.NeverOver.Entries.Count >= ModeSettingsJson.MaxNeverOver) return new SessionResult(false, SettingsText.NeverOverFull);
        return ApplyNeverOver(Settings.NeverOver.With(entry));
    }

    public SessionResult RemoveNeverOver(string exeFileName)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        return Settings.NeverOver.Contains(exeFileName) ? ApplyNeverOver(Settings.NeverOver.Without(exeFileName)) : new SessionResult(false, null);
    }

    private SessionResult ApplyNeverOver(NeverOverList list)
    {
        var next = Settings with { NeverOver = list };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        Settings = next;
        Changed?.Invoke(SettingsArea.Mode);
        return new SessionResult(true, null);
    }

    // ---- Scenes ----------------------------------------------------------------

    /// <summary>The words for the section when there are no scenes (EVALS S1: none is made ready).</summary>
    public const string SceneExplanation = "A scene is a few things on your island, from any page, that open together with one key.";

    /// <summary>Every pick of every page, for ticking things into a scene.</summary>
    public IReadOnlyList<Pick> AllPicks => Picks.Picks;

    /// <summary>A new scene with a name. No scene is shipped ready made.</summary>
    public SessionResult CreateScene(string name)
    {
        if (Locked(_scenesLocked, "scenes.json") is { } locked) return locked;
        // A key left behind by a scene that is gone (it could not be given back then) stays with its id: a new scene never takes that id and so never owns it.
        return ApplySceneEdit(Scenes.Create(name, id => KeybindEditor.KeyOf(Settings, KeybindEditor.SceneActionId(id)) is not null));
    }

    public SessionResult RenameScene(string id, string name) =>
        Locked(_scenesLocked, "scenes.json") ?? ApplySceneEdit(Scenes.Rename(id, name));

    /// <summary>Ticks a pick into the scene (at the end of its list) or takes it out; the list's order is the order the scene opens things in.</summary>
    public SessionResult SetSceneThing(string sceneId, Pick pick, bool on)
    {
        if (Locked(_scenesLocked, "scenes.json") is { } locked) return locked;
        return ApplySceneEdit(on ? Scenes.AddThing(sceneId, pick) : Scenes.RemoveThing(sceneId, pick.Id));
    }

    /// <summary>The yes/no question for deleting a scene, or null when there is none such.</summary>
    public string? AskDeleteScene(string id) =>
        Scenes.ById(id) is { } scene ? $"Delete the scene \"{scene.Name}\"? Its key goes back to Windows. Nothing that is open is closed." : null;

    /// <summary>Deletes the scene and gives its key back to Windows at once. The file is saved first: when that fails nothing was changed.</summary>
    public SessionResult DeleteScene(string id)
    {
        if (Locked(_scenesLocked, "scenes.json") is { } locked) return locked;
        if (Scenes.ById(id) is null) return new SessionResult(false, SceneText.NoSuchScene);
        var after = Scenes.Delete(id);
        if (_files.ScenesPath is null || !after.Save(_files.ScenesPath)) return new SessionResult(false, NotSaved("scenes.json"));
        Scenes = after;
        var key = _settingsLocked ? new KeyChange(Settings, false, null) : _keys.Clear(Settings, KeybindEditor.SceneActionId(id), SaveSettings);
        if (key.Changed) Settings = key.Settings;
        Changed?.Invoke(SettingsArea.Scenes);
        return new SessionResult(true, key.Refused ? SettingsText.KeyStillHeld : null); // a key that could not be let go of is tried again at the next change
    }

    private SessionResult ApplySceneEdit(SceneEdit edit)
    {
        if (edit.Refusal is { } refusal) return new SessionResult(false, refusal);
        if (_files.ScenesPath is null || !edit.Store.Save(_files.ScenesPath)) return new SessionResult(false, NotSaved("scenes.json"));
        Scenes = edit.Store;
        Changed?.Invoke(SettingsArea.Scenes);
        return new SessionResult(true, null);
    }

    // ---- Browser add-on --------------------------------------------------------

    /// <summary>How many browsers are connected, and when that changes; null when this session has no listener (the self-test's own, or a start where it could not listen).</summary>
    public IAddonStatus? Addon { get; }

    // ---- Coding agents -------------------------------------------------------

    /// <summary>Whether there is a way to connect Claude Code at all in this session (the self-test has none: it never touches a real file).</summary>
    public bool AgentsAvailable => _agents is not null;

    /// <summary>The helpers that can be connected here, Claude Code first (none in the self-test).</summary>
    public IReadOnlyList<IAgentConnector> AgentConnectors => _agents is null ? [] : [_agents, .. _moreAgents];

    // The helper's own connector; Claude Code's when none is named.
    private IAgentConnector? ConnectorOf(string? helper) => helper is null ? _agents : AgentConnectors.FirstOrDefault(c => c.Helper == helper);

    /// <summary>The location, written for the person, never expanded.</summary>
    public string AgentLocation => AgentLocationOf(null);

    public string AgentLocationOf(string? helper) => ConnectorOf(helper)?.Location ?? string.Empty;

    public IReadOnlyList<string> AgentLines => AgentLinesOf(null);

    public IReadOnlyList<string> AgentLinesOf(string? helper) => ConnectorOf(helper)?.LinesToAdd ?? [];

    /// <summary>Why connecting is not offered, or null.</summary>
    public string? AgentNotOffered => AgentNotOfferedOf(null);

    public string? AgentNotOfferedOf(string? helper) => ConnectorOf(helper)?.NotOffered;

    /// <summary>Called when the person opens the section: reads the one settings file, only to say whether the helper is connected (and whether its folder is on this computer).</summary>
    public AgentConnection AgentState(string? helper = null) => ConnectorOf(helper)?.State() ?? AgentConnection.NotConnected;

    /// <summary>The confirming button was pressed: connects the helper. Nothing else ever calls this.</summary>
    public SessionResult ConnectAgent(string? helper = null)
    {
        if (ConnectorOf(helper) is not { } connector) return new SessionResult(false, AgentRefusals.NotAllowed.Message);
        var result = connector.Connect();
        return new SessionResult(result.Done, result.Refusal?.Message);
    }

    /// <summary>The confirming button of an update: the same, after a fresh copy of the file is saved under a second name.</summary>
    public SessionResult UpdateAgent(string? helper = null)
    {
        if (ConnectorOf(helper) is not { } connector) return new SessionResult(false, AgentRefusals.NotAllowed.Message);
        var result = connector.Update();
        return new SessionResult(result.Done, result.Refusal?.Message);
    }

    public SessionResult DisconnectAgent(string? helper = null)
    {
        if (ConnectorOf(helper) is not { } connector) return new SessionResult(false, AgentRefusals.NotAllowed.Message);
        var result = connector.Disconnect();
        return new SessionResult(result.Done, result.Refusal?.Message);
    }

    // ---- Glass -------------------------------------------------------------

    public bool BlurAvailable => _blurAvailable();

    public IReadOnlyList<GlassOption> GlassOptions => GlassChoice.Offered(BlurAvailable);

    /// <summary>The glass to draw with now: the saved one, or Approved when Blur is saved but not available.</summary>
    public GlassKind EffectiveGlass => GlassChoice.Effective(Settings.Glass, BlurAvailable);

    public SessionResult SetGlass(GlassKind kind)
    {
        if (Locked(_settingsLocked, "settings.json") is { } locked) return locked;
        if (!GlassChoice.IsOffered(kind, BlurAvailable)) return new SessionResult(false, SettingsText.BlurUnavailableFor(Settings.Glass));
        if (kind == Settings.Glass) return new SessionResult(false, null);

        var next = Settings with { Glass = kind };
        if (!SaveSettings(next)) return new SessionResult(false, NotSaved("settings.json"));
        Settings = next;
        Changed?.Invoke(SettingsArea.Glass);
        return new SessionResult(true, null);
    }

    /// <summary>Whether the graphics-card light can be had on this computer now; the app sets it. Null (a session of a test) means it cannot. There is no setting for the light any more (WORK-ORDER-13).</summary>
    public Func<bool>? GraphicsLightAvailable { get; set; }

    public bool GraphicsCardLightAvailable => GraphicsLightAvailable?.Invoke() ?? false;

    // ---- The add-on's folder (Dan's P11, WORK-ORDER-13) -------------------------

    /// <summary>Where the add-on's folder is, or null; the app sets it. Null (a test) finds nothing.</summary>
    public Func<string?>? FindAddonFolder { get; set; }

    /// <summary>Opens a folder in the file manager; the app sets it. Null (a test) opens nothing.</summary>
    public Func<string, bool>? OpenFolderAt { get; set; }

    /// <summary>The button "Open the add-on folder": the folder opens, or the screen says why it did not.</summary>
    public SessionResult OpenAddonFolder()
    {
        var folder = FindAddonFolder?.Invoke();
        return folder is not null && OpenFolderAt?.Invoke(folder) == true
            ? new SessionResult(true, null)
            : new SessionResult(false, AddonText.FolderNotOpened.Message);
    }

    // ---- Shared ------------------------------------------------------------

    private static SessionResult? Locked(bool locked, string fileName) =>
        locked
            ? new SessionResult(false, (Refusals.SettingsUnreadable with { WhatHappened = $"{fileName} could not be read." }).Message)
            : null;

    private static string NotSaved(string fileName) =>
        $"{fileName} could not be saved, so nothing was changed.";
}
