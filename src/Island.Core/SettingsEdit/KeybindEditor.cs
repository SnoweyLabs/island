namespace Island.Core.SettingsEdit;

/// <summary>What a pressed key means while the screen waits for a new combination.</summary>
/// <param name="Combo">The combination, when the press made a usable one.</param>
/// <param name="Waiting">True while only modifiers are down: keep waiting, say nothing.</param>
/// <param name="Refusal">Why the press was refused, in plain words.</param>
public sealed record KeyCapture(HotkeyCombo? Combo, bool Waiting, Refusal? Refusal);

/// <summary>The result of one keybind edit: the settings now in force, whether anything changed, and the reason when it was refused.</summary>
public sealed record KeyChange(Settings Settings, bool Changed, Refusal? Refusal)
{
    public bool Refused => Refusal is not null;
}

/// <summary>
/// Changes keybinds: the main key, the page keys and the pick keys (an action id with a colon in it is a pick's). It never writes a file and
/// never forgets the rule of safety: the new combination is registered first, the old one is released only
/// after the new one is held and saved, so a refusal always leaves the old key in force.
/// </summary>
public sealed class KeybindEditor(IHotkeyRegistrar registrar, Func<string, string> actionName)
{
    /// <summary>The action id of the main key; a page's action id is its page id.</summary>
    public const string MainId = "showHide";

    // winerror.h ERROR_HOTKEY_ALREADY_REGISTERED. UNVERIFIED against Microsoft Learn (no network tonight):
    // with any other code the refusal is still shown, only in the general words.
    private const int HotkeyAlreadyRegistered = 1409;

    private const HotkeyModifiers AllModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    /// <summary>Turns one key press into a combination, keeps waiting while only modifiers are down, or says why not.</summary>
    public static KeyCapture Capture(KeyPress press)
    {
        if (press.IsWindowsKey) return Refuse(SettingsText.KeyHasWindowsKey);
        if (press.IsModifierOnly) return new KeyCapture(null, true, null);

        // Settings can only keep what HotkeyCombo writes and reads back, so test the key itself with modifiers
        // that are never unsafe, then let HotkeyCombo judge the real combination.
        var probe = new HotkeyCombo(AllModifiers, press.VirtualKey);
        if (!HotkeyCombo.TryParse(probe.ToString(), out var stored, out _) || stored != probe)
            return Refuse(SettingsText.KeyUnknown);

        var combo = new HotkeyCombo(press.Modifiers & AllModifiers, press.VirtualKey); // only what the text of a combination can carry
        return HotkeyCombo.TryParse(combo.ToString(), out _, out var why)
            ? new KeyCapture(combo, false, null)
            : Refuse(SettingsText.KeyUnsafe(why));
    }

    private static KeyCapture Refuse(Refusal refusal) => new(null, false, refusal);

    /// <summary>The combination an action has now; null when it has none.</summary>
    /// <summary>
    /// A scene's key (WORK-ORDER-7 section 5) is kept with the pick keys, under the action id "scene:" and the scene's id: it has a colon, so every rule of
    /// a pick's key (K2 to K9, the duplicate check, restoring the defaults, the settings file) already holds for it, and no page id (letters, digits and
    /// hyphens) and no pick id (a kind, a colon, a name) can be the same text.
    /// </summary>
    public const string SceneIdPrefix = "scene:";

    public static string SceneActionId(string sceneId) => SceneIdPrefix + sceneId;

    public static bool IsSceneAction(string actionId) => actionId.StartsWith(SceneIdPrefix, StringComparison.Ordinal);

    public static string SceneIdOf(string actionId) => actionId[SceneIdPrefix.Length..];

    /// <summary>The action id of the key that goes to the next mode.</summary>
    public const string ModeNextId = Settings.ModeNextKey;

    public static HotkeyCombo? KeyOf(Settings settings, string actionId) =>
        actionId == MainId ? settings.ShowHide
        : actionId == ModeNextId ? settings.ModeKey
        : PickKeysJson.IsPickId(actionId) ? settings.PickKeyFor(actionId)
        : settings.KeyFor(actionId);

    /// <summary>The action that already holds <paramref name="combo"/>, or null.</summary>
    public static string? OwnerOf(Settings settings, HotkeyCombo combo, string exceptAction)
    {
        if (exceptAction != MainId && settings.ShowHide == combo) return MainId;
        if (exceptAction != ModeNextId && settings.ModeKey == combo) return ModeNextId;
        return settings.PageKeys.FirstOrDefault(k => k.PageId != exceptAction && k.Combo == combo)?.PageId
            ?? settings.PickKeys.FirstOrDefault(k => k.PickId != exceptAction && k.Combo == combo)?.PickId;
    }

    /// <summary>What a pressed combination means now: <see cref="MainId"/>, a page id, or null. The app's key listener asks this, so a changed key needs no other bookkeeping.</summary>
    public static string? ActionFor(Settings settings, HotkeyCombo combo) => OwnerOf(settings, combo, exceptAction: string.Empty);

    /// <summary>A press, end to end: capture it, then assign it. A press that only waits changes nothing and refuses nothing.</summary>
    public KeyChange AssignPress(Settings current, string actionId, KeyPress press, Func<Settings, bool>? save = null)
    {
        var capture = Capture(press);
        if (capture.Refusal is { } refusal) return new KeyChange(current, false, refusal);
        return capture.Combo is { } combo ? Assign(current, actionId, combo, save) : new KeyChange(current, false, null);
    }

    /// <summary>
    /// Gives an action a combination. Order: refuse a duplicate inside the app; register the new one; save
    /// (when a save is given); only then release the old one. Any refusal leaves the old key held.
    /// </summary>
    public KeyChange Assign(Settings current, string actionId, HotkeyCombo combo, Func<Settings, bool>? save = null)
    {
        var old = KeyOf(current, actionId);
        if (old != combo && OwnerOf(current, combo, actionId) is { } owner)
            return Refused(current, SettingsText.KeyTakenInside(combo, actionName(owner), actionName(actionId)));

        // Also for the key the action already has: if Windows refused it at the start (another program held it) it may be free now,
        // and the registrar answers true for a combination it already holds.
        if (!registrar.TryRegister(combo, out var error))
            return Refused(current, error == HotkeyAlreadyRegistered
                ? SettingsText.KeyTakenByAnotherProgram(combo, old is not null)
                : SettingsText.KeyRefusedByWindows(combo, error, old is not null));

        if (old == combo) return new KeyChange(current, false, null);

        var next = With(current, actionId, combo);
        if (save is not null && !save(next))
        {
            registrar.Release(combo);
            return Refused(current, SettingsText.KeyNotSavedFor(old is not null));
        }

        if (old is { } released) registrar.Release(released);
        return new KeyChange(next, true, null);
    }

    /// <summary>Removes a page's or a pick's key. The main key cannot be removed.</summary>
    public KeyChange Clear(Settings current, string actionId, Func<Settings, bool>? save = null)
    {
        if (actionId == MainId) return Refused(current, SettingsText.MainKeyCannotBeEmpty);
        if (KeyOf(current, actionId) is not { } old) return new KeyChange(current, false, null);

        var next = With(current, actionId, null);
        if (save is not null && !save(next)) return Refused(current, SettingsText.KeyNotSaved);

        registrar.Release(old);
        return new KeyChange(next, true, null);
    }

    /// <summary>
    /// A pick that is gone gives its key back to Windows at once (WORK-ORDER-6 section 4): every pick key whose pick <paramref name="pickExists"/>
    /// does not know is cleared. A key that cannot be cleared (its save failed) stays; the next call tries again.
    /// </summary>
    public KeyChange ReleaseKeysOfMissingPicks(Settings current, Func<string, bool> pickExists, Func<Settings, bool>? save = null)
    {
        var result = new KeyChange(current, false, null);
        foreach (var key in current.PickKeys.Where(k => !pickExists(k.PickId)))
        {
            var step = Clear(result.Settings, key.PickId, save);
            if (step.Refused) return new KeyChange(result.Settings, result.Changed, step.Refusal);
            result = new KeyChange(step.Settings, result.Changed || step.Changed, null);
        }

        return result;
    }

    /// <summary>The default for one action: Ctrl+Q for the main key, no key for a page or a pick.</summary>
    public KeyChange RestoreDefault(Settings current, string actionId, Func<Settings, bool>? save = null) =>
        actionId == MainId ? Assign(current, MainId, Settings.Defaults.ShowHide, save) : Clear(current, actionId, save);

    /// <summary>
    /// EVALS K8: the original keys come back, the main key first (a failure there stops everything), then
    /// every page key and pick key is cleared. Idle time and glass are not touched.
    /// </summary>
    public KeyChange RestoreDefaults(Settings current, Func<Settings, bool>? save = null)
    {
        // The default main key may be held by a page or a pick at this moment: that one is cleared first, or the editor would refuse.
        var result = new KeyChange(current, false, null);
        if (OwnerOf(current, Settings.Defaults.ShowHide, MainId) is { } holder)
        {
            result = Clear(current, holder, save);
            if (result.Refused) return result;
        }

        var main = RestoreDefault(result.Settings, MainId, save);
        if (main.Refused) return new KeyChange(result.Settings, result.Changed, main.Refusal);
        result = new KeyChange(main.Settings, result.Changed || main.Changed, null);

        var changed = result.Changed;
        var held = result.Settings.PageKeys.Where(k => k.Combo is not null).Select(k => k.PageId).Concat(result.Settings.PickKeys.Select(k => k.PickId)).Concat(result.Settings.ModeKey is null ? Array.Empty<string>() : new[] { ModeNextId }).ToList();
        foreach (var id in held)
        {
            var step = Clear(result.Settings, id, save);
            if (step.Refused) return new KeyChange(result.Settings, changed, step.Refusal);
            result = step;
            changed |= step.Changed;
        }

        return new KeyChange(result.Settings, changed, null);
    }

    private static KeyChange Refused(Settings current, Refusal refusal) => new(current, false, refusal);

    private static Settings With(Settings settings, string actionId, HotkeyCombo? combo) =>
        actionId == MainId ? settings with { ShowHide = combo!.Value }
        : actionId == ModeNextId ? settings with { ModeKey = combo }
        : PickKeysJson.IsPickId(actionId) ? settings.WithPickKey(actionId, combo)
        : settings.WithPageKey(actionId, combo);
}
