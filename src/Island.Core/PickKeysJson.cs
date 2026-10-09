using System.Text.Json;

namespace Island.Core;

/// <summary>The key of one pick, by pick id (the id always has a colon in it: <c>program:spotify</c>; a page id never has one).</summary>
public sealed record PickKey(string PickId, HotkeyCombo Combo);

/// <summary>
/// How the settings file carries the keys of picks (WORK-ORDER-6 section 4): an object "pickKeys" of pick id to combination. A pick with
/// no key has no entry. An empty text is read as no key. Whether the pick still exists is not known here; the app drops the entry of a
/// pick that is gone.
/// </summary>
public static class PickKeysJson
{
    public const string Key = "pickKeys";
    private const int MaxIdLength = 200;

    /// <summary>The id of an action is a pick's when it has a colon in it.</summary>
    public static bool IsPickId(string actionId) => actionId.Contains(':');

    /// <summary>False when the object is absent; otherwise true with the keys or a problem text.</summary>
    public static bool Read(JsonElement root, out List<PickKey> keys, out string? problem)
    {
        keys = [];
        problem = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(Key, out var obj)) return false;
        if (obj.ValueKind != JsonValueKind.Object)
        {
            problem = $"\"{Key}\" must be an object.";
            return true;
        }

        foreach (var entry in obj.EnumerateObject())
        {
            if (!IsUsableId(entry.Name))
            {
                problem = $"\"{Key}\" holds a name that is not a pick id.";
                return true;
            }

            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                problem = $"\"{Key}\" must hold text such as \"Ctrl+Alt+S\" for each pick.";
                return true;
            }

            var text = entry.Value.GetString();
            if (string.IsNullOrEmpty(text)) continue;
            if (!HotkeyCombo.TryParse(text, out var combo, out var why))
            {
                problem = $"\"{Key}\": {why}";
                return true;
            }

            keys.Add(new PickKey(entry.Name, combo));
        }

        return true;
    }

    public static Dictionary<string, string> ToJson(IEnumerable<PickKey> keys) =>
        keys.ToDictionary(k => k.PickId, k => k.Combo.ToString());

    /// <summary>The one rule for a pick's id: what a key can be given to and what the settings file can hold as a name.</summary>
    public static bool IsUsableId(string id) =>
        id.Length is > 2 and <= MaxIdLength && id.Contains(':') && !id.Contains('\\') && id.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c));
}
