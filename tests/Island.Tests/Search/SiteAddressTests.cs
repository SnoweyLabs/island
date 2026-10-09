using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-7 section 3: the one place a search address is built.</summary>
public class SiteAddressTests
{
    // A text with a space, an ampersand, a slash and a letter with an accent.
    private const string Text = "café a&b/c";

    [Theory]
    [InlineData(SearchService.YouTube, "https://www.youtube.com/results?search_query=caf%C3%A9%20a%26b%2Fc", "www.youtube.com")]
    [InlineData(SearchService.YouTubeMusic, "https://music.youtube.com/search?q=caf%C3%A9%20a%26b%2Fc", "music.youtube.com")]
    [InlineData(SearchService.Twitch, "https://www.twitch.tv/search?term=caf%C3%A9%20a%26b%2Fc", "www.twitch.tv")]
    [InlineData(SearchService.Spotify, "https://open.spotify.com/search/caf%C3%A9%20a%26b%2Fc", "open.spotify.com")]
    public void Search_Address_Per_Service(SearchService service, string expected, string host)
    {
        var address = SiteAddress.ForSearch(service, Text);

        Assert.Equal(expected, address);
        Assert.True(Uri.TryCreate(address, UriKind.Absolute, out var uri));
        Assert.Equal(host, uri!.Host);
        Assert.Equal("https", uri.Scheme);
        // The slash of the text is part of the text, never a new step of the path (it matters for Spotify, whose text is a path segment): "/", "search/", text.
        if (service == SearchService.Spotify) Assert.Equal(3, uri.Segments.Length);
    }

    [Fact]
    public void Blank_Text_Has_No_Address()
    {
        foreach (var service in Enum.GetValues<SearchService>())
        {
            Assert.Null(SiteAddress.ForSearch(service, null));
            Assert.Null(SiteAddress.ForSearch(service, "   "));
        }
    }

    [Fact]
    public void The_Address_Of_A_Host_Is_As_It_Was()
    {
        Assert.Equal("https://example.org/", SiteAddress.For("www.example.org"));
    }
}
