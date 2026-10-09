using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Island.Core.Agents.Connect;

/// <summary>Where a helper's connection stands, read from the text of its hooks file.</summary>
public enum ConnectState
{
    NotConnected,
    Connected,

    /// <summary>Entries that run Island.Notify are there, but not all of today's.</summary>
    ConnectedOlder,

    /// <summary>The text cannot be read as the helper's hooks file: the island leaves it alone.</summary>
    Unreadable,
}

/// <summary>
/// Adds and removes the island's entries in the text of Codex's hooks.json. Text in, text out: no file, no place on disk, nothing expanded.
/// Codex's hooks page (https://learn.chatgpt.com/docs/hooks, read 2026-10-07) gives a command hook only as one <c>command</c> string, with <c>async</c> to run it in the
/// background and no <c>args</c>: so the entry is the shell form, the program's path in double quotes, then <c>--agent codex</c>. Which shell Codex uses on Windows is
/// not on the page (UNVERIFIED). The page says nothing of what Codex does with an event it does not know, but every event written here is listed on it with no version note.
/// </summary>
public static class CodexHooks
{
    /// <summary>Said once on the question screen: Codex skips a hook it has not been told to trust.</summary>
    public const string TrustSentence = "Codex asks you once to trust the new hooks (open /hooks in Codex and say yes); until you do, it skips them.";

    /// <summary>The place of the file, as documentation text for the question only: never expanded, never opened.</summary>
    public const string PlaceText = @"%USERPROFILE%\.codex\hooks.json";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    /// <summary>The events an entry is written for, in the table's order.</summary>
    public static IReadOnlyList<string> WrittenEvents { get; } = CodexSignals.Rows.Where(r => r.Written).Select(r => r.Event).ToArray();

    /// <summary>Our entries are added next to what is there. Nothing is added twice. Unreadable text comes back unchanged with the reason.</summary>
    public static HookEdit Connect(string? settingsText, string notifyCommand, bool viaAlias = false)
    {
        var text = settingsText ?? "";
        if (!IsAcceptableCommand(notifyCommand, viaAlias)) return new HookEdit(text, false, HookInstaller.NotifyPathInvalid);

        JsonObject root;
        JsonObject hooks;
        if (IsBlank(text))
        {
            root = new JsonObject();
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        else if (!TryOpen(text, out root, out var found)) return new HookEdit(text, false, HookInstaller.FileUnreadable);
        else if (found is null)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        else hooks = found;

        // Check every event before changing any, so a refusal leaves nothing half done.
        if (WrittenEvents.Any(e => hooks[e] is { } n && n is not JsonArray)) return new HookEdit(text, false, HookInstaller.FileUnreadable);

        var added = false;
        foreach (var name in WrittenEvents)
        {
            var list = hooks[name] as JsonArray;
            if (list is not null && list.Any(HasOurHandler)) continue;
            if (list is null)
            {
                list = new JsonArray();
                hooks[name] = list;
            }

            list.Add(Group(notifyCommand));
            added = true;
        }

        if (!added) return new HookEdit(text, false, null);
        return TryRender(root, text, out var written) ? new HookEdit(written, true, null) : new HookEdit(text, false, HookInstaller.FileUnreadable);
    }

    /// <summary>Removes exactly the entries that run a program named Island.Notify, in whatever form they are written, and nothing else.</summary>
    public static HookEdit Disconnect(string? settingsText)
    {
        var text = settingsText ?? "";
        if (IsBlank(text)) return new HookEdit(text, false, null);
        if (!TryOpen(text, out var root, out var hooks)) return new HookEdit(text, false, HookInstaller.FileUnreadable);
        if (hooks is null) return new HookEdit(text, false, null);

        var removed = false;
        foreach (var name in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[name] is not JsonArray groups) continue;
            var removedHere = false;
            foreach (var group in groups.OfType<JsonObject>().ToList())
            {
                if (group["hooks"] is not JsonArray handlers || !handlers.Any(IsOurHandler)) continue;
                foreach (var h in handlers.Where(IsOurHandler).ToList()) handlers.Remove(h);
                removed = removedHere = true;
                if (handlers.Count == 0) groups.Remove(group);
            }

            // A list is taken out only when taking our entry out emptied it: a list the person left empty stays.
            if (removedHere && groups.Count == 0) hooks.Remove(name);
        }

        if (!removed) return new HookEdit(text, false, null);
        if (hooks.Count == 0) root.Remove("hooks"); // the object that connecting made, with nothing left in it
        return TryRender(root, text, out var written) ? new HookEdit(written, true, null) : new HookEdit(text, false, HookInstaller.FileUnreadable);
    }

    /// <summary>Not connected (no entry runs Island.Notify), connected (every event of today's), connected, older (some, not all), or unreadable.</summary>
    public static ConnectState State(string? settingsText)
    {
        if (IsBlank(settingsText)) return ConnectState.NotConnected;
        if (!TryOpen(settingsText!, out _, out var hooks)) return ConnectState.Unreadable;
        if (hooks is null) return ConnectState.NotConnected;
        var anywhere = hooks.Any(p => p.Value is JsonArray groups && groups.Any(HasOurHandler));
        if (!anywhere) return ConnectState.NotConnected;
        var all = WrittenEvents.All(e => hooks[e] is JsonArray groups && groups.Any(HasOurHandler));
        return all ? ConnectState.Connected : ConnectState.ConnectedOlder;
    }

    /// <summary>The exact lines that connecting would add, for the question asked before anything is written. Empty for a command that connecting would refuse.</summary>
    public static IReadOnlyList<string> LinesToAdd(string notifyCommand, bool viaAlias = false)
    {
        if (!IsAcceptableCommand(notifyCommand, viaAlias)) return [];
        var hooks = new JsonObject();
        foreach (var name in WrittenEvents) hooks[name] = new JsonArray(Group(notifyCommand));
        var shown = new JsonObject { ["hooks"] = hooks };
        return shown.ToJsonString(Pretty).Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>A byte-order mark in front of nothing is an empty file.</summary>
    private static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text?.TrimStart((char)0xFEFF));

    private static JsonObject Group(string notifyCommand)
    {
        var handler = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"\"{notifyCommand}\" --agent {CodexSignals.Helper}",
            ["async"] = true,
        };
        return new JsonObject { ["hooks"] = new JsonArray(handler) }; // no matcher: Stop, UserPromptSubmit and Interrupt ignore one, the rest then match every tool
    }

    private static bool TryOpen(string text, out JsonObject root, out JsonObject? hooks)
    {
        root = new JsonObject();
        hooks = null;
        try
        {
            if (JsonNode.Parse(text.TrimStart((char)0xFEFF), NodeOptions) is not JsonObject obj) return false;
            root = obj;
            if (!obj.TryGetPropertyValue("hooks", out var h)) return true;
            if (h is not JsonObject ho) return false;
            hooks = ho;
            return true;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return false; // includes a repeated property name
        }
    }

    /// <summary>The new text. False when a value in the file cannot be written back (a lone surrogate escape): the file is then left as it is.</summary>
    private static bool TryRender(JsonObject root, string original, out string text)
    {
        try
        {
            var json = root.ToJsonString(Pretty).Replace("\r\n", "\n");
            var crlf = original.Contains("\r\n");
            if (crlf) json = json.Replace("\n", "\r\n");
            text = json + (crlf ? "\r\n" : "\n");
            return true;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            text = "";
            return false;
        }
    }

    private static bool HasOurHandler(JsonNode? group) =>
        group is JsonObject g && g["hooks"] is JsonArray handlers && handlers.Any(IsOurHandler);

    private static bool IsOurHandler(JsonNode? handler)
    {
        if (handler is not JsonObject h || h["command"] is not JsonValue v || !v.TryGetValue<string>(out var command)) return false;
        // With args, command is the program as it stands; without, it is a shell line and the program is its first word.
        return IsOurProgram(h["args"] is JsonArray ? command : FirstWord(command));
    }

    /// <summary>The first word of a shell line: quoted or not, and after a leading call operator (&amp;) a PowerShell line may carry.</summary>
    private static string FirstWord(string line)
    {
        var s = line.Trim();
        if (s.StartsWith('&')) s = s[1..].TrimStart();
        if (s.StartsWith('"'))
        {
            var close = s.IndexOf('"', 1);
            return close < 0 ? s[1..] : s[1..close];
        }

        var space = s.IndexOfAny([' ', '\t']);
        return space < 0 ? s : s[..space];
    }

    /// <summary>
    /// What connecting writes inside double quotes: an absolute local path (unpackaged) or, when packaged, the execution alias and nothing else. A path that holds a
    /// character a shell would still act on inside quotes (a quote, %, $, a backtick) or a control character is refused, so the quoting can never be broken out of.
    /// </summary>
    private static bool IsAcceptableCommand(string? command, bool viaAlias)
    {
        if (command is null || command != command.Trim() || command.Any(c => char.IsControl(c) || c is '"' or '%' or '$' or '`')) return false;
        return viaAlias ? command == PackageNames.NotifyAlias : IsAcceptablePath(command);
    }

    private static bool IsAcceptablePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' && IsOurProgram(path);

    private static bool IsOurProgram(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = path.Trim().Split('\\', '/')[^1];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Equals(HookInstaller.ProgramName, StringComparison.OrdinalIgnoreCase);
    }
}
