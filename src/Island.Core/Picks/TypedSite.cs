using System.Globalization;

namespace Island.Core;

/// <summary>What the island understood of a typed address: the host, or the refusal NOT_A_SITE.</summary>
public sealed record SiteUnderstanding(string? Host, Refusal? Refusal)
{
    public bool Ok => Host is not null;
}

/// <summary>
/// WORK-ORDER-10 section 3: whatever is typed for a website ("https://www.Example.org:8080/page?x#y", "user@example.org", " example.org. ")
/// is cut down to its host, and the screen shows that host before the person confirms. Written by hand, not with <see cref="Uri"/>, which
/// forgives too much. Something that is not a site's name is refused: nothing, a single word with no dot, an address with other than a web scheme,
/// a bad port, an IP address (the + list leaves those out too), a name with an empty or bad part. A name with letters outside ASCII is kept in its
/// ASCII form, the way a browser reports it to the add-on.
/// </summary>
public static class TypedSite
{
    /// <summary>(Claude) Text longer than this is refused unread.</summary>
    public const int MaxTypedChars = 2048;

    // A pick's id is "site:" and the host, and an id holds 200 characters at most (PickKeysJson.MaxIdLength): a longer name is refused here, with the register's words, not later with words about an id.
    private const int MaxHostChars = 195;
    private const int MaxLabelChars = 63;

    public static SiteUnderstanding Understand(string? text) =>
        HostOf(text) is { } host ? new SiteUnderstanding(host, null) : new SiteUnderstanding(null, HandPickRefusals.NotASite);

    private static string? HostOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTypedChars || text.Any(char.IsControl)) return null;
        var rest = text.Trim();

        var scheme = rest.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0 && rest[..scheme].IndexOfAny(['/', '\\', '?', '#', '@', ':']) >= 0) scheme = -1; // a "://" inside a path or a query is not a scheme
        if (scheme >= 0)
        {
            var name = rest[..scheme];
            if (!name.Equals("http", StringComparison.OrdinalIgnoreCase) && !name.Equals("https", StringComparison.OrdinalIgnoreCase)) return null;
            rest = rest[(scheme + 3)..];
        }
        else if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest[2..];
        }

        var end = rest.IndexOfAny(['/', '\\', '?', '#']);
        var authority = (end < 0 ? rest : rest[..end]).Trim();

        var at = authority.LastIndexOf('@');
        if (at >= 0) authority = authority[(at + 1)..];
        if (authority.StartsWith('[')) return null; // an IPv6 address

        var colon = authority.LastIndexOf(':');
        if (colon >= 0)
        {
            var port = authority[(colon + 1)..];
            if (port.Length > 5 || !port.All(char.IsAsciiDigit) || port.Length > 0 && int.Parse(port, CultureInfo.InvariantCulture) > 65535) return null;
            authority = authority[..colon];
        }

        var host = SiteMatch.Normalize(authority);
        if (host.Any(c => c > 127))
        {
            try
            {
                host = new IdnMapping().GetAscii(host);
            }
            catch (ArgumentException)
            {
                return null;
            }

            host = SiteMatch.Normalize(host);
        }

        while (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..]; // what is shown is what is kept: the pick drops every leading www.
        return IsHostName(host) ? host : null;
    }

    private static bool IsHostName(string host)
    {
        if (host.Length is 0 or > MaxHostChars || !host.Contains('.')) return false;
        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > MaxLabelChars || label[0] == '-' || label[^1] == '-') return false;
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }

        // 192.168.0.1, 1.2.3 and 127.0.0.0x1 are numbers a browser reads as addresses, not a site's name: a real last label starts with a letter (xn-- included).
        return char.IsAsciiLetter(labels[^1][0]);
    }
}
