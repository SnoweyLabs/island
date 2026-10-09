using Island.Core;

namespace Island.Tests;

public class MediaNamesTests
{
    [Theory]
    [InlineData("chrome.exe", true)]
    [InlineData("Chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("308046B0AF4A39CB", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", false)]
    [InlineData("alpha.exe", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Browsers_Are_Told_From_Desktop_Players(string? id, bool browser) =>
        Assert.Equal(browser, MediaNames.IsBrowserApp(id));

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("msedge.exe", "Edge")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("alpha.exe", "Alpha")]
    [InlineData("C:\\Apps\\beta.exe", "Beta")]
    [InlineData("Example.Gamma_abc123!App", "Gamma")]
    [InlineData("", "Player")]
    public void App_Names_Are_Readable(string id, string expected) => Assert.Equal(expected, MediaNames.App(id));

    [Theory]
    [InlineData("youtube.com", "YouTube")]
    [InlineData("www.youtube.com", "YouTube")]
    [InlineData("music.youtube.com", "YouTube Music")]
    [InlineData("open.spotify.com", "Spotify")]
    [InlineData("example.org", "example.org")]
    public void Site_Names_Are_Readable(string host, string expected) => Assert.Equal(expected, MediaNames.Site(host));
}
