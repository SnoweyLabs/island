using Island.Core;

namespace Island.Tests;

public class SearchAddressTests
{
    // A space, an ampersand, a slash and a letter with an accent.
    private const string Typed = "café a&b/c";
    private const string Encoded = "caf%C3%A9%20a%26b%2Fc";

    [Theory]
    [InlineData(SearchService.YouTube, "www.youtube.com/results?search_query=" + Encoded)]
    [InlineData(SearchService.YouTubeMusic, "music.youtube.com/search?q=" + Encoded)]
    [InlineData(SearchService.Twitch, "www.twitch.tv/search?term=" + Encoded)]
    [InlineData(SearchService.Spotify, "open.spotify.com/search/" + Encoded)] // the text is a path segment: the slash is encoded too
    [InlineData(SearchService.Google, "www.google.com/search?q=" + Encoded)]
    public void Pattern_And_Encoding_Per_Service(SearchService service, string expected)
    {
        Assert.Equal(expected, SearchServices.Address(service, Typed));
        Assert.DoesNotContain("://", SearchServices.Address(service, Typed));
    }

    [Theory]
    [InlineData("a b", "a%20b")]
    [InlineData("a&b", "a%26b")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a?b", "a%3Fb")]
    [InlineData("a#b", "a%23b")]
    [InlineData("100%", "100%25")]
    [InlineData("a+b", "a%2Bb")]
    [InlineData("a=b;c", "a%3Db%3Bc")]
    [InlineData("ș", "%C8%99")]
    [InlineData("A-z_0.9~", "A-z_0.9~")]
    [InlineData("\U0001F3B5", "%F0%9F%8E%B5")]
    [InlineData("ا", "%D8%A7")]
    public void Encode_Is_Strict_Utf8_Percent_Encoding(string text, string expected) =>
        Assert.Equal(expected, SearchServices.Encode(text));

    [Fact]
    public void Text_Cannot_Change_The_Shape_Of_The_Address()
    {
        // Text that looks like a path, an address, a query, a fragment: it stays one value.
        foreach (var service in Enum.GetValues<SearchService>())
        {
            var address = SearchServices.Address(service, "x/../y?z=1&w=2#frag https:\\\\evil.example/")!;

            // This computer's address ends with the "&" that closes the protocol's one parameter (search-ms syntax); the value is what stands before it.
            var value = service switch
            {
                SearchService.Spotify => address["open.spotify.com/search/".Length..],
                SearchService.ThisComputer => address.EndsWith('&') ? address[(address.IndexOf('=') + 1)..^1] : address,
                _ => address[(address.IndexOf('=') + 1)..],
            };
            Assert.DoesNotContain("/", value);
            Assert.DoesNotContain("&", value);
            Assert.DoesNotContain("?", value);
            Assert.DoesNotContain("#", address);
            Assert.DoesNotContain(" ", address);
            Assert.DoesNotContain("://", address);
        }
    }

    [Fact]
    public void Blank_Text_Gives_No_Address()
    {
        Assert.Null(SearchServices.Address(SearchService.YouTube, ""));
        Assert.Null(SearchServices.Address(SearchService.YouTube, "  "));
        Assert.Null(SearchServices.Address(SearchService.YouTube, null));
    }

    [Fact]
    public void The_Text_Is_Trimmed_Before_It_Is_Encoded()
    {
        Assert.Equal("www.twitch.tv/search?term=tu", SearchServices.Address(SearchService.Twitch, "  tu "));
    }

    [Fact]
    public void A_Lone_Surrogate_Becomes_The_Replacement_Character()
    {
        Assert.Equal("a%EF%BF%BDb", SearchServices.Encode("a\ud800b"));
        Assert.Equal("%EF%BF%BD", SearchServices.Encode("\udc00"));
    }

    [Fact]
    public void Very_Long_Text_Is_Cut_Without_Splitting_A_Pair()
    {
        var typed = new string('a', SearchServices.MaxAddressTextChars - 1) + "\U0001F3B5" + new string('b', 5000);

        var address = SearchServices.Address(SearchService.Spotify, typed)!;

        Assert.EndsWith(new string('a', SearchServices.MaxAddressTextChars - 1), address);   // the pair was dropped whole
        Assert.True(address.Length < 400);
        Assert.DoesNotContain("%EF%BF%BD", address);
    }

    [Fact]
    public void Text_Of_Only_Combining_Marks_Still_Encodes()
    {
        Assert.Equal("%CC%81%CC%81", SearchServices.Encode("́́"));
    }

    [Fact]
    public void Google_Has_Its_Own_Tile_Whenever_Something_Is_Typed_And_Never_Counts_As_A_Played_Service()
    {
        Assert.Null(SearchServices.GoogleTile("  "));
        Assert.Equal("Search tu on Google", SearchServices.GoogleTile(" tu ")!.Name);
        Assert.Equal("www.google.com/search?q=tu", SearchServices.GoogleTile("tu")!.Address);

        // Changed by Dan's decision of 2026-10-09: the tiles after the matches are always YouTube, Google and this computer, whatever played last.
        Assert.Equal([SearchService.YouTube, SearchService.Google, SearchService.ThisComputer], SearchServices.Tiles("tu").Select(t => t.Service));
        Assert.Empty(SearchServices.Tiles(" "));
    }

    [Fact]
    public void The_Three_Tiles_Say_Where_They_Search_And_This_Computer_Opens_File_Explorers_Search()
    {
        var tiles = SearchServices.Tiles(" tu ");
        Assert.Equal(["Search tu on YouTube", "Search tu on Google", "Search tu on this computer"], tiles.Select(t => t.Name));
        Assert.Equal("www.youtube.com/results?search_query=tu", tiles[0].Address);

        // search-ms:query=<URL-encoded text>& (Microsoft Learn, "Getting started with parameter-value arguments"): no scheme of the web, nothing of the text can add a parameter.
        Assert.Equal("search-ms:query=tu&", SiteAddress.ForSearch(SearchService.ThisComputer, "tu"));
        Assert.Equal("search-ms:query=a%20b%26crumb%3Dfolder%3AC%3A&", SiteAddress.ForSearch(SearchService.ThisComputer, @"a b&crumb=folder:C:"));
        Assert.Equal("https://www.google.com/search?q=tu", SiteAddress.ForSearch(SearchService.Google, "tu"));
        Assert.Null(SiteAddress.ForSearch(SearchService.ThisComputer, "  "));
        Assert.Equal("this computer", SearchServices.DisplayName(SearchService.ThisComputer));

        // "this computer" is never taken for a media service that played.
        Assert.Null(SearchServices.Parse("this computer"));
    }
}
