using System.Text.Json;

namespace Island.Core;

/// <summary>
/// How the settings file carries the mode ("mode": "focus", "vibe" or "dnd") and the "never over this" list ("neverOver": a list of a name
/// and an executable file name, never a path). The key that goes to the next mode is read like every other key, in "hotkeys".
/// </summary>
public static class ModeSettingsJson
{
    public const string ModeKey = "mode";
    public const string NeverOverKey = "neverOver";
    public const string ShowPillKey = "showPill";
    public const string NoticeSecondsKey = "noticeSeconds";
    public const int MaxNeverOver = 200;

    /// <summary>False when the key is absent; otherwise true with the value or a problem text.</summary>
    public static bool ReadMode(JsonElement root, out Mode mode, out string? problem)
    {
        mode = default;
        problem = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(ModeKey, out var v)) return false;
        if (v.ValueKind != JsonValueKind.String || !EnumNames.TryParse(v.GetString(), out mode))
            problem = "\"mode\" must be \"focus\", \"vibe\" or \"dnd\".";
        return true;
    }

    public static bool ReadNeverOver(JsonElement root, out NeverOverList list, out string? problem)
    {
        list = NeverOverList.Empty;
        problem = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(NeverOverKey, out var v)) return false;
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() > MaxNeverOver)
        {
            problem = $"\"{NeverOverKey}\" must be a list of at most {MaxNeverOver} entries.";
            return true;
        }

        var entries = new List<NeverOverEntry>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("exe", out var exe) || exe.ValueKind != JsonValueKind.String
                || !NeverOverList.IsValid(new NeverOverEntry(name.GetString()!, exe.GetString()!)))
            {
                problem = $"\"{NeverOverKey}\" holds an entry that is not a name and an executable file name such as \"game.exe\".";
                return true;
            }

            entries.Add(new NeverOverEntry(name.GetString()!, exe.GetString()!));
        }

        list = NeverOverList.From(entries);
        return true;
    }

    public static List<Dictionary<string, string>> NeverOverToJson(NeverOverList list) =>
        [.. list.Entries.Select(e => new Dictionary<string, string> { ["name"] = e.Name, ["exe"] = e.ExeFileName })];

    public static string ModeToJson(Mode mode) => mode.ToString().ToLowerInvariant();
}
