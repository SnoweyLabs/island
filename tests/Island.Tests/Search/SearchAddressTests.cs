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

            var value = service == SearchService.Spotify ? address["open.spotify.com/search/".Length..] : address[(address.IndexOf('=') + 1)..];
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

        // Nothing has played: only Google's tile. Something has played: its tile first, Google's last. "Google" in the played list is not a player.
        Assert.Equal([SearchService.Google], SearchServices.Tiles([], "tu").Select(t => t.Service));
        Assert.Equal([SearchService.Twitch, SearchService.Google], SearchServices.Tiles(["Twitch", "Google"], "tu").Select(t => t.Service));
        Assert.Empty(SearchServices.Tiles(["Twitch"], " "));
    }
}
