using System.Text.Json;

namespace Island.Core;

public enum SettingsStatus
{
    Loaded,
    Missing,
    Unreadable,
}

public sealed record SettingsLoad(Settings Settings, SettingsStatus Status, string? Detail);

/// <summary>The keybind of one page, by page id. A page may have none.</summary>
public sealed record PageKey(string PageId, HotkeyCombo? Combo);

/// <summary>Which glass the capsule is drawn with: the approved one, the darker tint, or the blurred one.</summary>
public enum GlassKind
{
    Approved,
    Darker,
    Blur,
}

/// <summary>
/// Keybinds and idle time. Loaded from a JSON file whose path the caller gives.
/// Loading never writes: a file that cannot be read is left exactly as it was.
/// Page keybinds are a list keyed by page id, not five fixed fields, because pages are data.
/// </summary>
public sealed record Settings(HotkeyCombo ShowHide, IReadOnlyList<PageKey> PageKeys, double IdleSeconds, GlassKind Glass = GlassKind.Approved, bool StartWithWindows = false)
{
    /// <summary>The idle time the settings screen and a real start keep it between, in seconds (WORK-ORDER-6 section 4).</summary>
    public const double MinIdleSeconds = 2;

    public const double MaxSetIdleSeconds = 60;

    private const double MaxIdleSeconds = 86400;
    private const string ShowHideKey = "showHide";

    /// <summary>The action id of the key that goes to the next mode; no page id can be this (page ids are made of lower-case letters, digits and hyphens, so a capital letter keeps them apart).</summary>
    public const string ModeNextKey = "modeNext";

    /// <summary>The defaults (Dan, 6 Oct 2026): the main key is Ctrl+Q and no page has a global keybind.</summary>
    public static Settings Defaults { get; } = new(
        HotkeyCombo.Parse("Ctrl+Q"),
        [.. Pages.BuiltIn.Select(p => new PageKey(p.Id, null))],
        LookConstants.IdleSeconds);

    // What the first build wrote on this laptop: three modifiers plus Space, Ctrl+Alt+Shift+1 to 5, 60 s.
    private const string OldShowHide = "Ctrl+Alt+Shift+Space";
    private const double OldIdleSeconds = 60;
    private static readonly string[] OldPageIds = [PageIds.Media, PageIds.Folders, PageIds.Apps, PageIds.Vibe, PageIds.Browser];

    /// <summary>The mode, always one of three: Vibe until the person chooses (Claude, WORK-ORDER-7 section 1).</summary>
    public Mode Mode { get; init; } = Mode.Vibe;

    /// <summary>How long the notice "Your agent is done" stays, in seconds: 3 to 30, 6 until the person changes it (WORK-ORDER-7 section 4).</summary>
    public double NoticeSeconds { get; init; } = NoticeQueue.DefaultSeconds;

    /// <summary>"Show the pill while something plays" (WORK-ORDER-7 section 2): on until the person switches it off.</summary>
    public bool ShowPill { get; init; } = true;

    /// <summary>Programs the island never appears over, a name and an executable file name each.</summary>
    public NeverOverList NeverOver { get; init; } = NeverOverList.Empty;

    /// <summary>The key that goes to the next mode (Focus, Vibe, DND, Focus ...); empty until the person sets it.</summary>
    public HotkeyCombo? ModeKey { get; init; }

    /// <summary>The keys of picks, by pick id; a pick with no key has no entry.</summary>
    public IReadOnlyList<PickKey> PickKeys { get; init; } = [];

    public HotkeyCombo? PickKeyFor(string pickId) => PickKeys.FirstOrDefault(k => k.PickId == pickId)?.Combo;

    /// <summary>Same settings with one pick's key replaced; null takes the entry away.</summary>
    public Settings WithPickKey(string pickId, HotkeyCombo? combo)
    {
        var keys = PickKeys.Where(k => k.PickId != pickId).ToList();
        if (combo is { } c) keys.Add(new PickKey(pickId, c));
        return this with { PickKeys = keys };
    }

    /// <summary>The idle time as the settings screen and a real start keep it: a whole second between the minimum and the maximum.</summary>
    public static double ClampIdle(double seconds) => double.IsNaN(seconds) ? LookConstants.IdleSeconds : Math.Clamp(Math.Round(seconds), MinIdleSeconds, MaxSetIdleSeconds);

    public HotkeyCombo? KeyFor(string pageId) => PageKeys.FirstOrDefault(k => k.PageId == pageId)?.Combo;

    /// <summary>Same settings with one page's keybind replaced (added when the page had none listed).</summary>
    public Settings WithPageKey(string pageId, HotkeyCombo? combo)
    {
        var keys = PageKeys.Where(k => k.PageId != pageId).ToList();
        var at = PageKeys.ToList().FindIndex(k => k.PageId == pageId);
        keys.Insert(at < 0 ? keys.Count : at, new PageKey(pageId, combo));
        return this with { PageKeys = keys };
    }

    // Records compare lists by reference; the keybinds must compare by content.
    public bool Equals(Settings? other) =>
        other is not null
        && ShowHide == other.ShowHide
        && IdleSeconds == other.IdleSeconds
        && Glass == other.Glass
        && StartWithWindows == other.StartWithWindows
        && Mode == other.Mode
        && ShowPill == other.ShowPill
        && NoticeSeconds == other.NoticeSeconds
        && ModeKey == other.ModeKey
        && NeverOver.Entries.SequenceEqual(other.NeverOver.Entries)
        && PageKeys.OrderBy(k => k.PageId, StringComparer.Ordinal).SequenceEqual(other.PageKeys.OrderBy(k => k.PageId, StringComparer.Ordinal))
        && PickKeys.OrderBy(k => k.PickId, StringComparer.Ordinal).SequenceEqual(other.PickKeys.OrderBy(k => k.PickId, StringComparer.Ordinal));

    public override int GetHashCode() =>
        HashCode.Combine(ShowHide, IdleSeconds, PageKeys.Count, Glass, StartWithWindows, PickKeys.Count, Mode);

    /// <summary>
    /// Loading writes in exactly one case: a file that holds the first build's defaults, untouched, is
    /// replaced by the new defaults. Anything else, a broken file included, is left exactly as it was.
    /// </summary>
    public static SettingsLoad Load(string path, IReadOnlyList<Page>? pages = null, bool bindIdleTime = true)
    {
        if (!File.Exists(path))
            return new SettingsLoad(Defaults, SettingsStatus.Missing, null);

        try
        {
            var text = File.ReadAllText(path);
            if (IsUntouchedOldDefaults(text))
            {
                File.WriteAllText(path, Defaults.ToJson());
                return new SettingsLoad(Defaults, SettingsStatus.Loaded, "The untouched settings of the first build were replaced by the new defaults.");
            }

            return Parse(text, pages, bindIdleTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fixed text on purpose: the system's own message names the file's full path, and a path
            // under the profile folder must never reach the log.
            return Unreadable("The file could not be opened.");
        }
    }

    /// <summary>True only for a file with exactly the first build's keys and values and nothing else (order and spacing do not matter).</summary>
    private static bool IsUntouchedOldDefaults(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2) return false;
            if (!root.TryGetProperty("idleSeconds", out var idle) || idle.ValueKind != JsonValueKind.Number
                || !idle.TryGetDouble(out var seconds) || seconds != OldIdleSeconds) return false;
            if (!root.TryGetProperty("hotkeys", out var hot) || hot.ValueKind != JsonValueKind.Object
                || hot.EnumerateObject().Count() != OldPageIds.Length + 1) return false;

            return IsKey(hot, ShowHideKey, OldShowHide)
                && OldPageIds.Select((id, i) => IsKey(hot, id, $"Ctrl+Alt+Shift+{i + 1}")).All(ok => ok);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsKey(JsonElement hot, string name, string expected) =>
        hot.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && HotkeyCombo.TryParse(v.GetString(), out var got, out _) && got == HotkeyCombo.Parse(expected);

    /// <summary>
    /// Reads the settings text. <paramref name="bindIdleTime"/> (true at a real start) keeps the idle time between two and sixty whole
    /// seconds; the temporary settings the self-test writes for itself are read with false.
    /// </summary>
    public static SettingsLoad Parse(string json, IReadOnlyList<Page>? pages = null, bool bindIdleTime = true)
    {
        pages ??= Pages.BuiltIn;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Unreadable("The file must hold one JSON object.");
            if (FileSchema.Problem(doc.RootElement) is { } schemaProblem) return Unreadable(schemaProblem);

            var result = Defaults;

            if (doc.RootElement.TryGetProperty("hotkeys", out var hot))
            {
                if (hot.ValueKind != JsonValueKind.Object)
                    return Unreadable("\"hotkeys\" must be an object.");

                if (ReadKey(hot, ShowHideKey, out var show, out var problem))
                {
                    if (problem is not null) return Unreadable(problem);
                    if (show is null) return Unreadable($"\"{ShowHideKey}\" cannot be empty: it is the keybind that brings the island back.");
                    result = result with { ShowHide = show.Value };
                }

                if (ReadKey(hot, ModeNextKey, out var modeCombo, out problem))
                {
                    if (problem is not null) return Unreadable(problem);
                    result = result with { ModeKey = modeCombo };
                }

                foreach (var page in pages)
                {
                    if (!ReadKey(hot, page.Id, out var combo, out problem)) continue;
                    if (problem is not null) return Unreadable(problem);
                    result = result.WithPageKey(page.Id, combo);
                }
            }

            if (doc.RootElement.TryGetProperty("idleSeconds", out var idle))
            {
                if (idle.ValueKind != JsonValueKind.Number || !idle.TryGetDouble(out var seconds)
                    || seconds <= 0 || seconds > MaxIdleSeconds)
                    return Unreadable("\"idleSeconds\" must be a number above 0 and at most 86400.");
                result = result with { IdleSeconds = bindIdleTime ? ClampIdle(seconds) : seconds };
            }

            if (doc.RootElement.TryGetProperty("glass", out var glass))
            {
                if (glass.ValueKind != JsonValueKind.String || !EnumNames.TryParse<GlassKind>(glass.GetString(), out var kind))
                    return Unreadable("\"glass\" must be \"approved\", \"darker\" or \"blur\".");
                result = result with { Glass = kind };
            }

            if (ModeSettingsJson.ReadMode(doc.RootElement, out var mode, out var modeProblem))
            {
                if (modeProblem is not null) return Unreadable(modeProblem);
                result = result with { Mode = mode };
            }

            if (doc.RootElement.TryGetProperty(ModeSettingsJson.ShowPillKey, out var showPill))
            {
                if (showPill.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Unreadable($"\"{ModeSettingsJson.ShowPillKey}\" must be true or false.");
                result = result with { ShowPill = showPill.GetBoolean() };
            }

            // "movingLight" (WORK-ORDER-12) is no longer a setting (WORK-ORDER-13): a file that has it loads, and the field is not written again.

            if (doc.RootElement.TryGetProperty(ModeSettingsJson.NoticeSecondsKey, out var noticeSeconds))
            {
                if (noticeSeconds.ValueKind != JsonValueKind.Number || !noticeSeconds.TryGetDouble(out var shown) || shown <= 0 || shown > MaxIdleSeconds)
                    return Unreadable($"\"{ModeSettingsJson.NoticeSecondsKey}\" must be a number of seconds, 3 to 30.");
                result = result with { NoticeSeconds = bindIdleTime ? NoticeQueue.Clamp(Math.Round(shown)) : shown };
            }

            if (ModeSettingsJson.ReadNeverOver(doc.RootElement, out var neverOver, out var neverProblem))
            {
                if (neverProblem is not null) return Unreadable(neverProblem);
                result = result with { NeverOver = neverOver };
            }

            if (PickKeysJson.Read(doc.RootElement, out var pickKeys, out var pickProblem))
            {
                if (pickProblem is not null) return Unreadable(pickProblem);
                if (pickKeys.Select(k => k.PickId).Distinct().Count() != pickKeys.Count) return Unreadable("A pick has two keys.");
                result = result with { PickKeys = pickKeys };
            }

            if (StartupSettingJson.Read(doc.RootElement, out var startWithWindows, out var startupProblem))
            {
                if (startupProblem is not null) return Unreadable(startupProblem);
                result = result with { StartWithWindows = startWithWindows };
            }

            var all = result.PageKeys.Select(k => k.Combo).Concat(result.PickKeys.Select(k => (HotkeyCombo?)k.Combo)).Append(result.ModeKey).Append(result.ShowHide).Where(c => c is not null).ToList();
            if (all.Distinct().Count() != all.Count)
                return Unreadable("Two keybinds are the same combination.");

            return new SettingsLoad(result, SettingsStatus.Loaded, null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            // Includes text that is not valid Unicode (a lone surrogate), which the JSON reader reports as
            // InvalidOperationException when asked for the string.
            return Unreadable(e is JsonException ? e.Message : "The file contains text that is not valid.");
        }
    }

    /// <summary>
    /// Reads one keybind from the "hotkeys" object. Returns false when the key is absent; otherwise
    /// true with either the combination (null for an explicit empty string) or a problem text.
    /// </summary>
    private static bool ReadKey(JsonElement hot, string key, out HotkeyCombo? combo, out string? problem)
    {
        combo = null;
        problem = null;
        if (!hot.TryGetProperty(key, out var value)) return false;

        if (value.ValueKind != JsonValueKind.String)
        {
            problem = $"\"{key}\" must be text such as \"Ctrl+Alt+Shift+1\".";
            return true;
        }

        var text = value.GetString();
        if (string.IsNullOrEmpty(text)) return true; // an empty string means no keybind

        if (!HotkeyCombo.TryParse(text, out var parsed, out var why))
        {
            problem = $"\"{key}\": {why}";
            return true;
        }

        combo = parsed;
        return true;
    }

    /// <summary>The file written on first run.</summary>
    public string ToJson()
    {
        var hotkeys = new Dictionary<string, string> { [ShowHideKey] = ShowHide.ToString() };
        foreach (var k in PageKeys) hotkeys[k.PageId] = k.Combo?.ToString() ?? string.Empty;
        hotkeys[ModeNextKey] = ModeKey?.ToString() ?? string.Empty;
        return JsonSerializer.Serialize(
            new Dictionary<string, object> { [FileSchema.Key] = FileSchema.Current, ["hotkeys"] = hotkeys, ["idleSeconds"] = IdleSeconds, ["glass"] = Glass.ToString().ToLowerInvariant(), [PickKeysJson.Key] = PickKeysJson.ToJson(PickKeys), [ModeSettingsJson.ModeKey] = ModeSettingsJson.ModeToJson(Mode), [ModeSettingsJson.ShowPillKey] = ShowPill, [ModeSettingsJson.NoticeSecondsKey] = NoticeSeconds, [ModeSettingsJson.NeverOverKey] = ModeSettingsJson.NeverOverToJson(NeverOver), [StartupSettingJson.Key] = StartWithWindows },
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Writes these settings, replacing the file. False when the path cannot be written; never throws. The caller must not use it on a file that could not be read.</summary>
    public bool Save(string path)
    {
        // Never write what the next start would call unreadable.
        if (!(IdleSeconds > 0 && IdleSeconds <= MaxIdleSeconds)) return false;
        if (!(NoticeSeconds > 0 && NoticeSeconds <= MaxIdleSeconds) || !Enum.IsDefined(Mode)) return false;
        try
        {
            AtomicFile.Write(path, ToJson()); // never in place: a cut settings file would be unreadable and every key would fall back to its default
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the defaults if there is no file. Returns true when it wrote. A path that cannot be written
    /// (a folder in the way, a read-only profile) leaves the app running on its defaults: false, no crash.
    /// </summary>
    public static bool EnsureExists(string path)
    {
        try
        {
            if (File.Exists(path)) return false;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Defaults.ToJson());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static SettingsLoad Unreadable(string detail) =>
        new(Defaults, SettingsStatus.Unreadable, detail);
}
