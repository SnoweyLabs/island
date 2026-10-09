using System.Text;

namespace Island.Core;

/// <summary>The media services whose own results page search knows. Exactly the four the sites document themselves.</summary>
public enum SearchService
{
    YouTube,
    YouTubeMusic,
    Twitch,
    Spotify,

    /// <summary>Not a media service: its tile is always there when something is typed, after the media service's own.</summary>
    Google,
}

/// <summary>What the last tile of search says and where it goes.</summary>
/// <param name="Service">Which service.</param>
/// <param name="Name">The tile's name, "Search &lt;text&gt; on &lt;service&gt;".</param>
/// <param name="Address">Host and path/query WITHOUT a scheme; SiteAddress puts the scheme on.</param>
public sealed record SearchServiceTile(SearchService Service, string Name, string Address);

/// <summary>
/// The address patterns and the service tile (EVALS F2). Each pattern was read from the site's own OpenSearch description
/// on 2026-10-06 (the Url template of each file):
///   YouTube        www.youtube.com/results?search_query={searchTerms}   (the file adds &amp;page and a utm tag: not used)
///   YouTube Music  music.youtube.com/search?q={searchTerms}             (the file adds a utm tag: not used)
///   Twitch         www.twitch.tv/search?term={searchTerms}
///   Spotify web    open.spotify.com/search/{searchTerms}                (the text is a path segment)
///   Google         www.google.com/search?q={searchTerms}                (added at Dan's request 2026-10-07; the pattern is Google's long-known one, not re-read from its description)
/// SoundCloud (observed only) and the spotify: form (undocumented) are deliberately not here.
/// </summary>
public static class SearchServices
{
    /// <summary>Longest text that goes into an address; a web address has a practical limit, and no search needs more.</summary>
    public const int MaxAddressTextChars = 200;

    private const string TextMark = "{text}";

    private static readonly Dictionary<SearchService, (string Name, string Pattern)> Table = new()
    {
        [SearchService.YouTube] = ("YouTube", "www.youtube.com/results?search_query=" + TextMark),
        [SearchService.YouTubeMusic] = ("YouTube Music", "music.youtube.com/search?q=" + TextMark),
        [SearchService.Twitch] = ("Twitch", "www.twitch.tv/search?term=" + TextMark),
        [SearchService.Spotify] = ("Spotify", "open.spotify.com/search/" + TextMark),
        [SearchService.Google] = ("Google", "www.google.com/search?q=" + TextMark),
    };

    private static readonly Dictionary<string, SearchService> ByHost = new(StringComparer.Ordinal)
    {
        ["youtube.com"] = SearchService.YouTube,
        ["m.youtube.com"] = SearchService.YouTube,
        ["music.youtube.com"] = SearchService.YouTubeMusic,
        ["twitch.tv"] = SearchService.Twitch,
        ["open.spotify.com"] = SearchService.Spotify,
    };

    public static string DisplayName(SearchService service) => Table[service].Name;

    /// <summary>
    /// The service for a name as MediaNames gives it ("YouTube", "Spotify") or for a host ("music.youtube.com"); null for
    /// anything else, SoundCloud included. The desktop Spotify program reads as "Spotify" and gets the web address.
    /// </summary>
    public static SearchService? Parse(string? nameOrHost)
    {
        if (string.IsNullOrWhiteSpace(nameOrHost)) return null;
        foreach (var (service, (name, _)) in Table)
            if (string.Equals(name, nameOrHost.Trim(), StringComparison.OrdinalIgnoreCase)) return service;
        return ByHost.TryGetValue(SiteMatch.Normalize(nameOrHost), out var found) ? found : null;
    }

    /// <summary>
    /// The one "Search &lt;text&gt; on &lt;service&gt;" tile, or null. <paramref name="playedOldestFirst"/> is every media
    /// service that has played since the app started, in the order they played, the latest last; entries search does not
    /// know are skipped, so the tile is for the latest KNOWN one. Blank text gives nothing.
    /// </summary>
    public static SearchServiceTile? Tile(IEnumerable<string?> playedOldestFirst, string? text)
    {
        var typed = text?.Trim();
        if (string.IsNullOrEmpty(typed)) return null;
        SearchService? latest = null;
        foreach (var played in playedOldestFirst)
            if (Parse(played) is { } known and not SearchService.Google) latest = known; // Google has no player: it never is "the latest played"
        if (latest is not { } service) return null;
        // The name is drawn: direction characters and the like are taken out of it (the address is encoded from the text as typed).
        return new SearchServiceTile(service, $"Search {AgentText.Clean(typed)} on {DisplayName(service)}", Address(service, typed)!);
    }

    /// <summary>The "Search &lt;text&gt; on Google" tile, or null for blank text.</summary>
    public static SearchServiceTile? GoogleTile(string? text)
    {
        var typed = text?.Trim();
        if (string.IsNullOrEmpty(typed)) return null;
        return new SearchServiceTile(SearchService.Google, $"Search {AgentText.Clean(typed)} on Google", Address(SearchService.Google, typed)!);
    }

    /// <summary>The tiles that come after the matches: the latest played media service's (if any), then Google's.</summary>
    public static IReadOnlyList<SearchServiceTile> Tiles(IEnumerable<string?> playedOldestFirst, string? text) =>
        [.. new[] { Tile(playedOldestFirst, text), GoogleTile(text) }.OfType<SearchServiceTile>()];

    /// <summary>
    /// Host and path/query for the service's results page, no scheme; null for blank text. Where the text is a query value
    /// it is encoded as a query value; where it is a path segment (Spotify) it is encoded as a path segment: in both cases
    /// by the strictest rule below, so a slash, a question mark, a hash, an ampersand, a plus and a percent sign in the text
    /// can never change the shape of the address.
    /// </summary>
    public static string? Address(SearchService service, string? text)
    {
        var typed = text?.Trim();
        if (string.IsNullOrEmpty(typed)) return null;
        if (!Table.TryGetValue(service, out var row)) return null;
        return row.Pattern.Replace(TextMark, Encode(Truncate(typed)), StringComparison.Ordinal);
    }

    /// <summary>
    /// UTF-8 percent-encoding that leaves only A-Z a-z 0-9 - . _ ~ as they are, so a space is %20 (never +) and any
    /// slash is %2F. A lone surrogate becomes U+FFFD (%EF%BF%BD). Every one of the four sites reads %20 in a query value.
    /// </summary>
    public static string Encode(string text)
    {
        var sb = new StringBuilder(text.Length * 3);
        foreach (var b in Encoding.UTF8.GetBytes(SearchMatch.ReplaceLoneSurrogates(text)))
        {
            if (IsUnreserved(b)) sb.Append((char)b);
            else sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static bool IsUnreserved(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
            or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~';

    private static string Truncate(string text)
    {
        if (text.Length <= MaxAddressTextChars) return text;
        var cut = MaxAddressTextChars;
        if (char.IsHighSurrogate(text[cut - 1])) cut--; // never split a pair
        return text[..cut];
    }
}
