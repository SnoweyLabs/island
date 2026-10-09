namespace Island.Core;

/// <summary>
/// The one place that builds a web address, from a host, to be handed to the user's default browser. The app
/// itself never contacts the internet (GuardTests.App_Has_No_Internet_Client): this text is only given away.
/// </summary>
public static class SiteAddress
{
    public static string For(string host) => "https://" + SiteMatch.Normalize(host) + "/";

    /// <summary>
    /// The address of a service's own results page for the text (WORK-ORDER-7 section 3): the pattern of <see cref="SearchServices"/> with the
    /// scheme put in front. The text is percent-encoded there. Null when the text is blank. Only given away, to the person's browser.
    /// </summary>
    public static string? ForSearch(SearchService service, string? text) =>
        SearchServices.Address(service, text) is { } address ? "https://" + address : null;

    /// <summary>The host of an address, with or without its scheme ("www.youtube.com/watch" and "https://www.youtube.com/watch" give the same); null when there is none.</summary>
    public static string? HostOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : null;
    }
}
