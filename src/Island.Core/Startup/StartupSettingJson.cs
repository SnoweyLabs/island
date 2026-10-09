using System.Text.Json;

namespace Island.Core;

/// <summary>
/// How the settings file carries "startWithWindows": a plain yes or no, off when absent. It is only a memory of the
/// person's last choice; the switch never reads it to decide anything and nothing is written because of it.
/// </summary>
public static class StartupSettingJson
{
    public const string Key = "startWithWindows";

    /// <summary>False with no problem when the key is absent; a problem text when it is there and is not true or false.</summary>
    public static bool Read(JsonElement root, out bool value, out string? problem)
    {
        value = false;
        problem = null;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(Key, out var v)) return false;
        if (v.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = v.GetBoolean();
            return true;
        }

        problem = $"\"{Key}\" must be true or false.";
        return true;
    }
}
