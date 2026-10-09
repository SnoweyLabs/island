using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island.Core;

/// <summary>
/// The fields of the hook's JSON that matter. Field names are on the Claude Code hooks page (and Codex's, which are the same):
/// the event, the notification's kind, the working folder, and, from WORK-ORDER-11 on, the session's id and a tool's name (both "" when absent).
/// </summary>
public sealed record HookInput(string Event, string NotificationType, string Folder, string SessionId = "", string ToolName = "");

/// <summary>Reads the hook's JSON. Anything that is not the expected shape gives null; it never throws.</summary>
public static partial class HookInputReader
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <param name="json">The hook's input.</param>
    /// <param name="eventWhenMissing">The event named on the command line (<c>--event</c>), for a helper whose input does not name its own; used only when the input names none.</param>
    public static HookInput? Read(string? json, string? eventWhenMissing = null)
    {
        // More characters than the limit can never fit in the limit's bytes, so skip the encoding.
        if (string.IsNullOrWhiteSpace(json) || json.Length > AgentPipe.StdinLimitBytes) return null;
        return Read(Encoding.UTF8.GetBytes(json), eventWhenMissing);
    }

    public static HookInput? Read(ReadOnlySpan<byte> utf8, string? eventWhenMissing = null)
    {
        if (utf8.IsEmpty || utf8.Length > AgentPipe.StdinLimitBytes) return null;
        if (utf8.StartsWith(Bom)) utf8 = utf8[Bom.Length..]; // the JSON reader does not skip it
        try
        {
            using var doc = JsonDocument.Parse(utf8.ToArray(),new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!TryString(root, "hook_event_name", required: string.IsNullOrEmpty(eventWhenMissing), out var ev)
                || !TryString(root, "notification_type", required: false, out var kind)
                || !TryString(root, "cwd", required: false, out var cwd))
                return null;
            if (ev.Length == 0) ev = eventWhenMissing ?? "";
            return new HookInput(ev, kind, cwd, Optional(root, "session_id"), Optional(root, "tool_name"));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            // What was read may be the first 64 KB of a longer text (the agent's whole last answer can be that long): the fields that matter come first.
            return utf8.Length >= AgentPipe.StdinLimitBytes ? ReadPrefix(utf8, eventWhenMissing) : null;
        }
    }

    /// <summary>The three fields from the start of a text that was cut: top-level strings only, read until the cut.</summary>
    private static HookInput? ReadPrefix(ReadOnlySpan<byte> utf8, string? eventWhenMissing)
    {
        string? ev = null, kind = null, cwd = null, session = null, tool = null;
        try
        {
            var reader = new Utf8JsonReader(utf8, isFinalBlock: false, default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                if (!reader.Read()) break;
                if (reader.TokenType == JsonTokenType.String)
                {
                    if (name == "hook_event_name") ev = reader.GetString();
                    else if (name == "notification_type") kind = reader.GetString();
                    else if (name == "cwd") cwd = reader.GetString();
                    else if (name == "session_id") session = reader.GetString();
                    else if (name == "tool_name") tool = reader.GetString();
                }
                else if ((reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) && !reader.TrySkip()) break;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            // the cut fell inside something unreadable: what came before it counts
        }

        if (string.IsNullOrEmpty(ev)) ev = eventWhenMissing;
        return string.IsNullOrEmpty(ev) ? null : new HookInput(ev, kind ?? "", cwd ?? "", session ?? "", tool ?? "");
    }

    /// <summary>A text field that may be missing or of another type: then "" (a field the island does not need must never refuse the whole input).</summary>
    private static string Optional(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? ReadString(e) : "";

    private static bool TryString(JsonElement root, string name, bool required, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var e)) return !required;
        if (e.ValueKind != JsonValueKind.String) return false;
        value = ReadString(e);
        return !required || value.Length > 0;
    }

    // A path may hold an unpaired surrogate, which JSON writes as an escape and which GetString refuses.
    // The escape is dropped and the rest of the string is read; if that fails too, the string is empty.
    private static string ReadString(JsonElement e)
    {
        try
        {
            return e.GetString() ?? "";
        }
        catch (InvalidOperationException)
        {
            try
            {
                using var again = JsonDocument.Parse(LoneSurrogateEscape().Replace(e.GetRawText(), ""));
                return again.RootElement.GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return "";
            }
        }
    }

    // A high-surrogate escape not followed by a low one, or a low one not preceded by a high one.
    [GeneratedRegex(@"\x5Cu[dD][89abAB][0-9a-fA-F]{2}(?!\x5Cu[dD][c-fC-F][0-9a-fA-F]{2})|(?<!\x5Cu[dD][89abAB][0-9a-fA-F]{2})\x5Cu[dD][c-fC-F][0-9a-fA-F]{2}")]
    private static partial Regex LoneSurrogateEscape();
}
