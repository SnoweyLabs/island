using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island.Core;

/// <summary>A tab as the add-on describes it (PROTOCOL.md "tab object"). The host is already normalized.</summary>
public sealed record TabObject(int Id, int WindowId, string Title, string Host, bool Audible, bool Active, bool Pinned, bool Incognito);

/// <summary>A frame from the add-on, validated.</summary>
public abstract record AddonMessage;

public sealed record HelloMessage(string Browser, string Profile, string Version) : AddonMessage;

public sealed record SnapshotMessage(IReadOnlyList<TabObject> Tabs) : AddonMessage;

public sealed record TabMessage(TabObject Tab) : AddonMessage;

public sealed record TabRemovedMessage(int Id) : AddonMessage;

public sealed record TabActivatedMessage(int Id, int WindowId) : AddonMessage;

public sealed record IconMessage(int Id, byte[] Png) : AddonMessage;

public sealed record MediaMessage(int Id, TabMedia Media) : AddonMessage;

public sealed record ResultMessage(string Command, int Id, bool Ok) : AddonMessage;

public sealed record PingMessage : AddonMessage;

/// <summary>A frame from the island, as the pretend add-on reads it.</summary>
public abstract record IslandMessage;

public sealed record WelcomeMessage(int Version) : IslandMessage;

public sealed record ActivateMessage(int Id, int WindowId) : IslandMessage;

public sealed record MediaCommandMessage(int Id, MediaCommand Command) : IslandMessage;

public sealed record CloseTabMessage(int Id) : IslandMessage;

public sealed record ResyncMessage : IslandMessage;

public sealed record PongMessage : IslandMessage;

/// <summary>Either a message or the reason the frame was rejected. Never both, never neither.</summary>
public readonly record struct Parsed<T>(T? Message, string? Reject) where T : class
{
    public bool Ok => Message is not null;

    public static Parsed<T> Bad(string reason) => new(null, reason);
}

/// <summary>
/// Strict reading of every frame in extension/PROTOCOL.md, and building of the island's own. A bad frame
/// gives a reason and never throws: the frames come from outside the app. Reasons name the rule broken,
/// never the frame's content.
/// </summary>
public static partial class TabMessages
{
    // A snapshot is object > array > object: three levels. Anything much deeper is not ours.
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 8 };

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int MaxShortText = 64;

    /// <summary>
    /// A title cut in the middle of an emoji leaves half a character (a lone surrogate) in the text. The JSON reader refuses
    /// such text; the island replaces the half with U+FFFD so one odd title never loses the whole list of tabs.
    /// </summary>
    private static string? Clean(string? frame) => frame is null ? null : LoneSurrogates().Replace(frame, "\\uFFFD");

    [GeneratedRegex(@"\x5Cu[dD][89abAB][0-9a-fA-F]{2}(?!\x5Cu[dD][c-fC-F][0-9a-fA-F]{2})|(?<!\x5Cu[dD][89abAB][0-9a-fA-F]{2})\x5Cu[dD][c-fC-F][0-9a-fA-F]{2}")]
    private static partial Regex LoneSurrogates();

    public static Parsed<AddonMessage> ParseAddon(string? frame)
    {
        try
        {
            return ParseAddonCore(Clean(frame));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return Parsed<AddonMessage>.Bad("unreadable text");
        }
    }

    private static Parsed<AddonMessage> ParseAddonCore(string? frame)
    {
        if (!TryOpen(frame, out var doc, out var type, out var reject)) return Parsed<AddonMessage>.Bad(reject);
        using (doc)
        {
            var o = doc.RootElement;
            AddonMessage? m = type switch
            {
                "hello" => Hello(o),
                "snapshot" => Snapshot(o),
                "tab" => o.TryGetProperty("tab", out var t) && Tab(t) is { } tab ? new TabMessage(tab) : null,
                "tab-removed" => Int(o, "id", 0) is { } id ? new TabRemovedMessage(id) : null,
                "tab-activated" => Int(o, "id", 0) is { } a && Int(o, "windowId", -1) is { } w ? new TabActivatedMessage(a, w) : null,
                "icon" => Icon(o),
                "media" => Media(o),
                "result" => Result(o),
                "ping" => new PingMessage(),
                _ => null,
            };
            if (m is not null) return new(m, null);
            return Parsed<AddonMessage>.Bad(AddonTypes.Contains(type) ? "bad field in " + type : "unknown type");
        }
    }

    private static readonly string[] AddonTypes = ["hello", "snapshot", "tab", "tab-removed", "tab-activated", "icon", "media", "result", "ping"];

    public static Parsed<IslandMessage> ParseIsland(string? frame)
    {
        try
        {
            return ParseIslandCore(Clean(frame));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            return Parsed<IslandMessage>.Bad("unreadable text");
        }
    }

    private static Parsed<IslandMessage> ParseIslandCore(string? frame)
    {
        if (!TryOpen(frame, out var doc, out var type, out var reject)) return Parsed<IslandMessage>.Bad(reject);
        using (doc)
        {
            var o = doc.RootElement;
            IslandMessage? m = type switch
            {
                "welcome" => Int(o, "v", TabProtocol.Version, TabProtocol.Version) is { } v ? new WelcomeMessage(v) : null,
                "activate" => Int(o, "id", 0) is { } a && Int(o, "windowId", -1) is { } w ? new ActivateMessage(a, w) : null,
                "media-command" => Int(o, "id", 0) is { } id && Command(Str(o, "command", MaxShortText)) is { } c ? new MediaCommandMessage(id, c) : null,
                "close" => Int(o, "id", 0) is { } closing ? new CloseTabMessage(closing) : null,
                "resync" => new ResyncMessage(),
                "pong" => new PongMessage(),
                _ => null,
            };
            return m is null ? Parsed<IslandMessage>.Bad("bad island frame") : new(m, null);
        }
    }

    public static string Welcome() => $$"""{"type":"welcome","v":{{TabProtocol.Version}}}""";

    public static string Activate(int id, int windowId) => $$"""{"type":"activate","id":{{id}},"windowId":{{windowId}}}""";

    /// <summary>The frame that closes a tab. A negative id is no tab (the protocol knows 0 and up): refused, never written.</summary>
    public static string CloseTab(int id) =>
        id >= 0 ? $$"""{"type":"close","id":{{id}}}""" : throw new ArgumentOutOfRangeException(nameof(id), "a tab id is 0 or more");

    public static string MediaCommandFrame(int id, MediaCommand command) =>
        $$"""{"type":"media-command","id":{{id}},"command":"{{CommandName(command)}}"}""";

    public static string Resync() => """{"type":"resync"}""";

    public static string Pong() => """{"type":"pong"}""";

    public static string CommandName(MediaCommand command) => command switch
    {
        MediaCommand.PlayPause => "playpause",
        MediaCommand.Next => "next",
        _ => "previous",
    };

    private static MediaCommand? Command(string? name) => name switch
    {
        "playpause" => MediaCommand.PlayPause,
        "next" => MediaCommand.Next,
        "previous" => MediaCommand.Previous,
        _ => null,
    };

    private static bool TryOpen(string? frame, out JsonDocument doc, out string type, out string reject)
    {
        doc = null!;
        type = "";
        reject = "";
        if (frame is null || frame.Length == 0) { reject = "empty"; return false; }
        if (frame.Length > TabProtocol.MaxFrameBytes || Encoding.UTF8.GetByteCount(frame) > TabProtocol.MaxFrameBytes)
        {
            reject = "oversize";
            return false;
        }

        try
        {
            doc = JsonDocument.Parse(frame, Options);
        }
        catch (JsonException)
        {
            reject = "not json";
            return false;
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Object) { doc.Dispose(); reject = "not an object"; return false; }
        if (!doc.RootElement.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String)
        {
            doc.Dispose();
            reject = "no type";
            return false;
        }

        type = t.GetString()!;
        return true;
    }

    private static HelloMessage? Hello(JsonElement o)
    {
        if (Int(o, "v", TabProtocol.Version, TabProtocol.Version) is null) return null;
        if (Str(o, "client", MaxShortText) != TabProtocol.ClientName) return null;
        var browser = Str(o, "browser", MaxShortText);
        var profile = Str(o, "profile", MaxShortText);
        var version = Str(o, "version", MaxShortText);
        if (string.IsNullOrEmpty(browser) || profile is null || !ProfileRule().IsMatch(profile) || version is null) return null;
        return new HelloMessage(browser, profile, version);
    }

    private static SnapshotMessage? Snapshot(JsonElement o)
    {
        if (!o.TryGetProperty("tabs", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        if (list.GetArrayLength() > TabProtocol.MaxTabsPerConnection) return null;
        var tabs = new List<TabObject>();
        foreach (var e in list.EnumerateArray())
        {
            if (Tab(e) is not { } tab) return null; // one bad tab spoils the frame: half a snapshot would lie
            tabs.Add(tab);
        }

        return new SnapshotMessage(tabs);
    }

    private static TabObject? Tab(JsonElement t)
    {
        if (t.ValueKind != JsonValueKind.Object) return null;
        var id = Int(t, "id", 0);
        var windowId = Int(t, "windowId", -1);
        var title = Str(t, "title", TabProtocol.MaxTitleChars);
        var host = Str(t, "host", TabProtocol.MaxHostChars);
        var audible = Bool(t, "audible");
        var active = Bool(t, "active");
        if (id is null || windowId is null || title is null || host is null || audible is null || active is null) return null;
        if (!OptionalBool(t, "pinned", out var pinned) || !OptionalBool(t, "incognito", out var incognito)) return null;
        return new TabObject(id.Value, windowId.Value, title, host.Length == 0 ? "" : SiteMatch.Normalize(host), audible.Value, active.Value, pinned, incognito);
    }

    private static IconMessage? Icon(JsonElement o)
    {
        var id = Int(o, "id", 0);
        // base64 of 64 KB is about 87.4 KB of text; anything longer is over the limit before decoding.
        var text = Str(o, "png", (TabProtocol.MaxIconBytes + 2) / 3 * 4);
        if (id is null || string.IsNullOrEmpty(text)) return null;
        var bytes = new byte[TabProtocol.MaxIconBytes];
        if (!Convert.TryFromBase64String(text, bytes, out var n)) return null;
        if (n < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)) return null;
        return new IconMessage(id.Value, bytes[..n]);
    }

    private static MediaMessage? Media(JsonElement o)
    {
        var id = Int(o, "id", 0);
        if (id is null) return null;
        if (!NullableStr(o, "title", out var title) || !NullableStr(o, "artist", out var artist)) return null;
        PlaybackState? state = Str(o, "state", MaxShortText) switch
        {
            "playing" => PlaybackState.Playing,
            "paused" => PlaybackState.Paused,
            "stopped" => PlaybackState.Stopped,
            _ => null,
        };
        if (state is null || !Seconds(o, "position", out var position) || !Seconds(o, "length", out var length)) return null;
        if (!Optional(o, "rate", 0, MaxRate, out var rate) || !Optional(o, "readAt", 1, MaxUnixMs, out var readAt)) return null;
        return new MediaMessage(id.Value, new TabMedia(title, artist, state.Value, position, length, rate, readAt));
    }

    /// <summary>The playing speed the add-on may report (a browser offers up to 16).</summary>
    private const double MaxRate = 16;

    /// <summary>The last millisecond of year 9999, as a Unix time: a reading time beyond it is no time.</summary>
    private const double MaxUnixMs = 253402300799000d;

    /// <summary>An optional number: absent or null is fine (null result); present it must be a finite number within the range.</summary>
    private static bool Optional(JsonElement o, string name, double min, double max, out double? value)
    {
        value = null;
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d) || d < min || d > max) return false;
        value = d;
        return true;
    }

    private static ResultMessage? Result(JsonElement o)
    {
        var cmd = Str(o, "cmd", MaxShortText);
        var id = Int(o, "id", 0);
        var ok = Bool(o, "ok");
        if (cmd is not ("activate" or "media-command" or "close") || id is null || ok is null) return null;
        return new ResultMessage(cmd, id.Value, ok.Value);
    }

    private static int? Int(JsonElement o, string name, int min, int max = int.MaxValue) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i >= min && i <= max ? i : null;

    private static bool? Bool(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static string? Str(JsonElement o, string name, int maxChars) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && FitsChars(s, maxChars) ? s : null;

    /// <summary>The add-on cuts text to a number of Unicode characters (code points), not UTF-16 units: an emoji counts one here and two in <c>string.Length</c>.</summary>
    private static bool FitsChars(string s, int maxChars) =>
        s.Length <= maxChars || (s.Length <= maxChars * 2 && s.EnumerateRunes().Count() <= maxChars);

    /// <summary>Absent or false gives false; a value that is not a boolean is a bad frame.</summary>
    private static bool OptionalBool(JsonElement o, string name, out bool value)
    {
        value = false;
        if (!o.TryGetProperty(name, out var v)) return true;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = v.GetBoolean();
        return true;
    }

    /// <summary>Absent or null gives null; text longer than a title is a bad frame.</summary>
    private static bool NullableStr(JsonElement o, string name, out string? value)
    {
        value = null;
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return FitsChars(value!, TabProtocol.MaxTitleChars);
    }

    /// <summary>Seconds: absent or null gives null; otherwise a number from 0 to about 31 years.</summary>
    private static bool Seconds(JsonElement o, string name, out double? value)
    {
        value = null;
        if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d) || d < 0 || d > 1e9) return false;
        value = d;
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z")]
    private static partial Regex ProfileRule();
}
