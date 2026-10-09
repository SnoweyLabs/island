namespace Island.Core;

/// <summary>
/// Plain-language names for where something plays, and the rule that tells "the whole browser" from a desktop player.
/// Ids come from Windows' media sessions (SourceAppUserModelId) or from a tab's host; Research/windows-apis.md section 6.
/// </summary>
public static class MediaNames
{
    // Compared lower case. Chrome and Edge ids and Firefox's hashed id are from the research file; Brave, Opera and
    // Vivaldi are guesses (UNVERIFIED), kept because a missed browser would count as a desktop player.
    private static readonly string[] BrowserNeedles = ["chrome", "msedge", "firefox", "308046b0af4a39cb", "brave", "opera", "vivaldi"];

    private static readonly (string Needle, string Name)[] Apps =
    [
        ("spotify", "Spotify"), ("msedge", "Edge"), ("chrome", "Chrome"), ("firefox", "Firefox"), ("308046b0af4a39cb", "Firefox"),
        ("brave", "Brave"), ("opera", "Opera"), ("vivaldi", "Vivaldi"), ("vlc", "VLC"),
    ];

    private static readonly Dictionary<string, string> Sites = new()
    {
        ["youtube.com"] = "YouTube",
        ["music.youtube.com"] = "YouTube Music",
        ["twitch.tv"] = "Twitch",
        ["soundcloud.com"] = "SoundCloud",
        ["open.spotify.com"] = "Spotify",
    };

    /// <summary>True when the session's app is a web browser: Windows keeps one session for all of its tabs.</summary>
    public static bool IsBrowserApp(string? sourceApp) =>
        !string.IsNullOrEmpty(sourceApp) && BrowserNeedles.Any(n => sourceApp.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Spotify", "Chrome", ... for a session's app id; the readable part of the id when the app is not known.</summary>
    public static string App(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return "Player";
        foreach (var (needle, name) in Apps)
            if (sourceApp.Contains(needle, StringComparison.OrdinalIgnoreCase)) return name;

        var id = sourceApp;
        var bang = id.IndexOf('!');
        if (bang >= 0) id = id[..bang];
        id = id[(id.LastIndexOfAny(['\\', '/']) + 1)..];
        var underscore = id.IndexOf('_');
        if (underscore > 0) id = id[..underscore];
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        id = id[(id.LastIndexOf('.') + 1)..];
        return id.Length == 0 ? "Player" : char.ToUpperInvariant(id[0]) + id[1..];
    }

    /// <summary>"YouTube", "Twitch", ... for a tab's host; the host itself when it is not a known media site.</summary>
    public static string Site(string host)
    {
        var normalized = SiteMatch.Normalize(host);
        return Sites.GetValueOrDefault(normalized, normalized);
    }
}
