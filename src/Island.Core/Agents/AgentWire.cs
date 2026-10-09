using System.Text.Json;

namespace Island.Core;

/// <summary>What Island.Notify tells the island: the event, the notification kind, the folder, the chain of process ids.</summary>
public sealed record AgentMessage(string Event, string Kind, string Folder, IReadOnlyList<int> Chain);

/// <summary>
/// The message on the pipe: one compact JSON object of a fixed shape, <c>{"v":1,"e":..,"k":..,"f":..,"c":[..]}</c>,
/// ended by a newline and never longer than <see cref="AgentPipe.MaxMessageBytes"/>. The parser takes exactly that
/// shape and nothing else (no other property, no repeated property, no wrong type, no longer text), and never throws.
/// </summary>
public static class AgentWire
{
    private const int Version = 1;

    public static byte[] Encode(string? hookEvent, string? kind, string? folder, IReadOnlyList<int> chain)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteString("e", AgentText.Head(AgentText.Clean(hookEvent), AgentPipe.MaxEventChars));
            w.WriteString("k", AgentText.Head(AgentText.Clean(kind), AgentPipe.MaxKindChars));
            w.WriteString("f", AgentText.Tail(AgentText.Clean(folder), AgentPipe.MaxFolderChars));
            w.WriteStartArray("c");
            foreach (var pid in chain.Where(p => p > 0).Take(AgentPipe.MaxChain)) w.WriteNumberValue(pid);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    public static AgentMessage? TryParse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty || utf8.Length > AgentPipe.MaxMessageBytes) return null;
        try
        {
            using var doc = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 3 });
            return FromRoot(doc.RootElement);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static AgentMessage? TryParse(string? text) =>
        string.IsNullOrEmpty(text) || text.Length > AgentPipe.MaxMessageBytes ? null : TryParse(System.Text.Encoding.UTF8.GetBytes(text));

    private static AgentMessage? FromRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        string? e = null, k = null, f = null;
        List<int>? chain = null;
        var seen = 0;
        var versionOk = false;
        foreach (var p in root.EnumerateObject())
        {
            seen++;
            switch (p.Name)
            {
                case "v" when !versionOk:
                    versionOk = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v) && v == Version;
                    if (!versionOk) return null;
                    break;
                case "e" when e is null: e = Str(p.Value, AgentPipe.MaxEventChars); break;
                case "k" when k is null: k = Str(p.Value, AgentPipe.MaxKindChars); break;
                case "f" when f is null: f = Str(p.Value, AgentPipe.MaxFolderChars); break;
                case "c" when chain is null: chain = Pids(p.Value); break;
                default: return null; // unknown or repeated property
            }
        }

        return seen == 5 && versionOk && e is not null && k is not null && f is not null && chain is not null
            ? new AgentMessage(e, k, f, chain)
            : null;
    }

    private static string? Str(JsonElement el, int max)
    {
        if (el.ValueKind != JsonValueKind.String) return null;
        var s = el.GetString();
        return s is not null && s.Length <= max ? s : null;
    }

    private static List<int>? Pids(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<int>();
        foreach (var item in el.EnumerateArray())
        {
            if (list.Count == AgentPipe.MaxChain || item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var pid) || pid <= 0)
                return null;
            list.Add(pid);
        }

        return list;
    }
}
