using System.Text.Json;

namespace Island.Core.Agents.Sessions;

/// <summary>
/// A message as the session book takes it. <paramref name="Time"/> is the sender's reading of the steady counter
/// (milliseconds), or null for a version-1 message (the island then uses its own reading at arrival).
/// <paramref name="SessionId"/> is "" when the hook gave none.
/// </summary>
public sealed record SessionMessage(string Helper, string Event, string Kind, string SessionId, long? Time, string Folder, IReadOnlyList<int> Chain);

/// <summary>
/// The version-2 message: <c>{"v":2,"a":helper,"e":event,"k":kind,"s":session id,"t":time,"f":end of folder,"c":[chain]}</c>
/// and a newline, never longer than <see cref="SessionLimits.MaxMessageBytes"/>. It sits beside <see cref="AgentWire"/>
/// (which is not changed and still takes version 1 only). The parser takes exactly this shape and nothing else: no
/// other property, no repeated property, no wrong type, no longer text, and never throws.
/// </summary>
public static class SessionWire
{
    private const int PropertyCount = 8;

    public static byte[] Encode(string? helper, string? hookEvent, string? kind, string? sessionId, long time, string? folder, IReadOnlyList<int>? chain)
    {
        var bytes = Write(helper, hookEvent, kind, sessionId, time, folder, chain);
        // Cannot happen with the limits (every character escapes to at most six bytes); the folder is the cheapest field to lose.
        return bytes.Length <= SessionLimits.MaxMessageBytes ? bytes : Write(helper, hookEvent, kind, sessionId, time, "", chain);
    }

    /// <summary>A version-2 message only. Anything else, including version 1, is null.</summary>
    public static SessionMessage? TryParse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty || utf8.Length > SessionLimits.MaxMessageBytes) return null;
        try
        {
            using var doc = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 3 });
            return FromRoot(doc.RootElement);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null; // a refusal, never an exception: the bytes come from outside
        }
    }

    public static SessionMessage? TryParse(string? text) =>
        string.IsNullOrEmpty(text) || text.Length > SessionLimits.MaxMessageBytes ? null : TryParse(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// Version 2 as above, else version 1 through <see cref="AgentWire"/> as exactly what it always was: Claude Code, no
    /// session id, no time. Anything else is null.
    /// </summary>
    public static SessionMessage? TryParseAny(ReadOnlySpan<byte> utf8)
    {
        if (TryParse(utf8) is { } two) return two;
        return AgentWire.TryParse(utf8) is { } one ? FromVersionOne(one) : null;
    }

    public static SessionMessage FromVersionOne(AgentMessage m) =>
        new(HelperNames.ClaudeCode, m.Event, m.Kind, "", null, m.Folder, m.Chain);

    private static byte[] Write(string? helper, string? hookEvent, string? kind, string? sessionId, long time, string? folder, IReadOnlyList<int>? chain)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", SessionLimits.WireVersion);
            w.WriteString("a", Head(helper, SessionLimits.MaxHelperChars));
            w.WriteString("e", Head(hookEvent, SessionLimits.MaxEventChars));
            w.WriteString("k", Head(kind, SessionLimits.MaxKindChars));
            w.WriteString("s", Head(sessionId, SessionLimits.MaxSessionIdChars));
            w.WriteNumber("t", Math.Max(time, 0));
            w.WriteString("f", AgentText.Tail(AgentText.Clean(folder), SessionLimits.MaxFolderChars));
            w.WriteStartArray("c");
            foreach (var pid in (chain ?? []).Where(p => p > 0).Take(SessionLimits.MaxChain)) w.WriteNumberValue(pid);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string Head(string? text, int max) => AgentText.Head(AgentText.Clean(text).Trim(), max);

    private static SessionMessage? FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? a = null, e = null, k = null, s = null, f = null;
        long? t = null;
        List<int>? chain = null;
        var seen = 0;
        var versionOk = false;
        foreach (var p in root.EnumerateObject())
        {
            seen++;
            switch (p.Name)
            {
                case "v" when !versionOk:
                    versionOk = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v) && v == SessionLimits.WireVersion;
                    if (!versionOk) return null;
                    break;
                case "a" when a is null: a = Str(p.Value, SessionLimits.MaxHelperChars); break;
                case "e" when e is null: e = Str(p.Value, SessionLimits.MaxEventChars); break;
                case "k" when k is null: k = Str(p.Value, SessionLimits.MaxKindChars); break;
                case "s" when s is null: s = Str(p.Value, SessionLimits.MaxSessionIdChars); break;
                case "t" when t is null: t = Time(p.Value); break;
                case "f" when f is null: f = Str(p.Value, SessionLimits.MaxFolderChars, clean: false); break;
                case "c" when chain is null: chain = Pids(p.Value); break;
                default: return null; // unknown or repeated property
            }
        }

        return seen == PropertyCount && versionOk && a is not null && e is not null && k is not null && s is not null && t is not null && f is not null && chain is not null
            ? new SessionMessage(a, e, k, s, t, f, chain)
            : null;
    }

    // A text longer than its limit is refused, not cut: the sender cuts before it writes. Control characters that came
    // in as JSON escapes are removed, so nothing downstream sees them (the folder is cleaned later by ProjectName).
    private static string? Str(JsonElement el, int max, bool clean = true)
    {
        if (el.ValueKind != JsonValueKind.String) return null;
        var text = el.GetString();
        if (text is null || text.Length > max) return null;
        return clean ? AgentText.Clean(text).Trim() : text;
    }

    private static long? Time(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var t) && t >= 0 ? t : null;

    private static List<int>? Pids(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<int>();
        foreach (var item in el.EnumerateArray())
        {
            if (list.Count == SessionLimits.MaxChain || item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var pid) || pid <= 0)
                return null;
            list.Add(pid);
        }

        return list;
    }
}
