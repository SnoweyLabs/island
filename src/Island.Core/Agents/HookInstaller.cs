using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Island.Core;

/// <summary>The new text of the file, whether it differs, and why it was left alone (null when it was not).</summary>
public sealed record HookEdit(string Text, bool Changed, string? Reason);

/// <summary>
/// Adds or removes the island's two hook entries in the text of an agent's settings file. Text in, text out: it
/// touches no file and knows no location. The entries are in the form the Claude Code hooks page gives for starting a
/// program directly, without a shell (<c>command</c> is the program, <c>args</c> is present), in the background
/// (<c>async</c>): the event Stop (which takes no matcher) and the event Notification with the matcher
/// <c>permission_prompt</c>. Everything else in the text is kept, in its order.
/// </summary>
public static class HookInstaller
{
    public const string FileUnreadable = "HOOKS_FILE_UNREADABLE";
    public const string NotifyPathInvalid = "NOTIFY_PATH_INVALID";

    /// <summary>The file name (without .exe) of the program our entries run, and the only program removal touches.</summary>
    public const string ProgramName = "Island.Notify";

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    /// <summary>Our entries are added next to what is there. Nothing is added twice. Unreadable text comes back unchanged.</summary>
    public static HookEdit Connect(string? settingsText, string notifyPath, bool viaAlias = false)
    {
        var text = settingsText ?? "";
        if (!IsAcceptableCommand(notifyPath, viaAlias)) return new HookEdit(text, false, NotifyPathInvalid);

        JsonObject root;
        JsonObject hooks;
        if (IsBlank(text))
        {
            root = new JsonObject();
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        else if (!TryOpen(text, out root, out var found)) return new HookEdit(text, false, FileUnreadable);
        else if (found is null)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        else hooks = found;

        // Check every event before changing any, so a refusal leaves nothing half done.
        var events = Entries;
        if (events.Any(e => hooks[e.Item1] is { } n && n is not JsonArray)) return new HookEdit(text, false, FileUnreadable);

        var added = false;
        foreach (var (name, matcher) in events)
        {
            var list = hooks[name] as JsonArray;
            if (list is not null && list.Any(g => HasOurHandler(g, matcher))) continue; // our entry for this event and matcher is already there
            if (list is null)
            {
                list = new JsonArray();
                hooks[name] = list;
            }

            list.Add(Group(matcher, notifyPath));
            added = true;
        }

        if (!added) return new HookEdit(text, false, null);
        return TryRender(root, text, out var written) ? new HookEdit(written, true, null) : new HookEdit(text, false, FileUnreadable);
    }

    /// <summary>Removes exactly the entries that run a program named Island.Notify and nothing else.</summary>
    public static HookEdit Disconnect(string? settingsText)
    {
        var text = settingsText ?? "";
        if (IsBlank(text)) return new HookEdit(text, false, null);
        if (!TryOpen(text, out var root, out var hooks)) return new HookEdit(text, false, FileUnreadable);
        if (hooks is null) return new HookEdit(text, false, null);

        var removed = false;
        foreach (var name in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[name] is not JsonArray groups) continue;
            var removedHere = false;
            foreach (var group in groups.OfType<JsonObject>().ToList())
            {
                if (group["hooks"] is not JsonArray handlers) continue;
                var hadOurs = handlers.Any(IsOurHandler);
                if (!hadOurs) continue;
                foreach (var h in handlers.Where(IsOurHandler).ToList()) handlers.Remove(h);
                removed = removedHere = true;
                if (handlers.Count == 0) groups.Remove(group);
            }

            // A list is taken out only when taking our entry out emptied it: a list the person left empty stays.
            if (removedHere && groups.Count == 0) hooks.Remove(name);
        }

        if (!removed) return new HookEdit(text, false, null);
        if (hooks.Count == 0) root.Remove("hooks"); // the object that connecting made, with nothing left in it
        return TryRender(root, text, out var written) ? new HookEdit(written, true, null) : new HookEdit(text, false, FileUnreadable);
    }

    /// <summary>True when every entry of today's set runs Island.Notify. Unreadable text, and a file with only some of them, are not "connected" (see <see cref="StateOf"/>).</summary>
    public static bool IsConnected(string? settingsText) => StateOf(settingsText) == AgentConnection.Connected;

    /// <summary>
    /// Where the file stands: unreadable; not connected (no entry runs Island.Notify); connected (every entry of today's set is there); or connected, older (some entry
    /// that runs Island.Notify is there, but not all of today's: the connection made before the sixth page told the island only when Claude Code had finished or needed
    /// an answer, and Update brings the rest).
    /// </summary>
    public static AgentConnection StateOf(string? settingsText)
    {
        if (IsBlank(settingsText)) return AgentConnection.NotConnected;
        if (!TryOpen(settingsText!, out _, out var hooks)) return AgentConnection.Unreadable;
        if (hooks is null) return AgentConnection.NotConnected;
        if (Entries.All(e => hooks[e.Event] is JsonArray groups && groups.Any(g => HasOurHandler(g, e.Matcher)))) return AgentConnection.Connected;
        var any = hooks.Any(p => p.Value is JsonArray groups && groups.Any(g => HasOurHandler(g)));
        return any ? AgentConnection.ConnectedOlder : AgentConnection.NotConnected;
    }

    /// <summary>The exact lines that connecting would add, for the screen that asks before anything is written.</summary>
    public static IReadOnlyList<string> LinesToAdd(string notifyPath, bool viaAlias = false)
    {
        if (!IsAcceptableCommand(notifyPath, viaAlias)) return []; // the lines of a program that connecting would refuse are not shown
        var hooks = new JsonObject();
        foreach (var (name, matcher) in Entries)
        {
            if (hooks[name] is not JsonArray list) hooks[name] = list = new JsonArray();
            list.Add(Group(matcher, notifyPath));
        }

        var shown = new JsonObject { ["hooks"] = hooks };
        return shown.ToJsonString(Pretty).Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>
    /// Today's entries, one per event of Claude Code that the table of WORK-ORDER-11 section 3 reads and that has long been on its hooks page: the session starting and
    /// ending, a prompt, a tool finishing, Stop, and the two kinds of Notification (permission_prompt and idle_prompt, in the way the page gives for narrowing an entry to a
    /// kind). The events PermissionRequest, PostToolUseFailure and StopFailure are in the table but NOT written: what Claude Code does with a settings file that names an
    /// event it does not know is not on its page (UNVERIFIED), and a file it refuses would break Dan's own helper. Without them "needs you" shows after the notification
    /// that a permission has waited about six seconds, and a failed tool or an error ends nothing until Stop or the next prompt (STATE.md, OWNER DECISIONS).
    /// </summary>
    private static readonly (string Event, string? Matcher)[] Entries =
    [
        ("SessionStart", null), ("UserPromptSubmit", null), ("PostToolUse", null), ("Stop", null), ("SessionEnd", null),
        ("Notification", "permission_prompt"), ("Notification", "idle_prompt"),
    ];

    /// <summary>The events (without repeats) of today's entries, in the order they are written.</summary>
    public static IReadOnlyList<string> WrittenEvents { get; } = [.. Entries.Select(e => e.Event).Distinct()];

    /// <summary>A byte-order mark in front of nothing is an empty file.</summary>
    private static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text?.TrimStart((char)0xFEFF));

    private static JsonObject Group(string? matcher, string notifyPath)
    {
        var handler = new JsonObject
        {
            ["type"] = "command",
            ["command"] = notifyPath,
            ["args"] = new JsonArray("--agent", "claude"),
            ["async"] = true,
        };
        var group = new JsonObject();
        if (matcher is not null) group["matcher"] = matcher;
        group["hooks"] = new JsonArray(handler);
        return group;
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
            if (original.Contains("\r\n")) json = json.Replace("\n", "\r\n");
            text = json + (original.Contains("\r\n") ? "\r\n" : "\n");
            return true;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            text = "";
            return false;
        }
    }

    /// <summary>Our handler in the group; with a matcher, only in a group that has that matcher.</summary>
    private static bool HasOurHandler(JsonNode? group, string? matcher = null) =>
        group is JsonObject g && g["hooks"] is JsonArray handlers && handlers.Any(IsOurHandler)
        && (matcher is null || g["matcher"] is JsonValue m && m.TryGetValue<string>(out var found) && found == matcher);

    private static bool IsOurHandler(JsonNode? handler)
    {
        if (handler is not JsonObject h || h["command"] is not JsonValue v || !v.TryGetValue<string>(out var command)) return false;
        // With args, command is the program as it stands (it may hold spaces); without, it is a shell line and the program is its first word.
        return IsOurProgram(h["args"] is JsonArray ? command : FirstWord(command));
    }

    private static string FirstWord(string line)
    {
        var s = line.Trim();
        if (s.StartsWith('"'))
        {
            var close = s.IndexOf('"', 1);
            return close < 0 ? s[1..] : s[1..close];
        }

        var space = s.IndexOfAny([' ', '\t']);
        return space < 0 ? s : s[..space];
    }

    /// <summary>
    /// The path connecting writes: the program's own name, and an absolute path on a local drive with nothing around it (never a relative or network
    /// path, which the hook would resolve from wherever it is run, and never one with blanks round it, which would name a file that is not there).
    /// </summary>
    /// <summary>
    /// What connecting writes as the hook's command: an absolute local path (unpackaged), or, when packaged, the package's execution alias and nothing else
    /// (the alias is the stable command name; the package's own folder changes with every version).
    /// </summary>
    private static bool IsAcceptableCommand(string? command, bool viaAlias) =>
        viaAlias ? command == PackageNames.NotifyAlias : IsAcceptablePath(command);

    private static bool IsAcceptablePath(string? path) =>
        path is not null && path == path.Trim() && path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/' && IsOurProgram(path);

    private static bool IsOurProgram(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = path.Trim().Split('\\', '/')[^1];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Equals(ProgramName, StringComparison.OrdinalIgnoreCase);
    }
}
