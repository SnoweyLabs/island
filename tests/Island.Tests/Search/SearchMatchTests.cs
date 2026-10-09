using Island.Core;

namespace Island.Tests;

public class SearchMatchTests
{
    private static SearchCandidate Pick(string name, bool closed = false) => new(name, "pick:" + name, true, closed);
    private static SearchCandidate Open(string name) => new(name, "open:" + name, false, false);
    private static string[] Names(IEnumerable<SearchCandidate> items) => [.. items.Select(i => i.Name)];

    [Theory]
    [InlineData("spo", "Spotify")]
    [InlineData("SPO", "Spotify")]
    [InlineData("tify", "Spotify")]
    [InlineData("cafe", "Cafe\u0301 Alpha")]          // text without an accent finds a name with a combining accent
    [InlineData("caf\u00e9", "Cafe Alpha")]           // text with an accent finds a name without one
    [InlineData("\u0219", "Stiri")]                   // Romanian s with comma below
    [InlineData("tara", "\u021aar\u0103 Beta")]       // Romanian T with comma, a with breve
    [InlineData("fi", "Of\ufb01ce")]                  // a ligature is read as its letters
    [InlineData("strasse", "Stra\u00dfe Gamma")]      // sharp s
    [InlineData("\uff21lpha", "Alpha")]               // full-width A
    [InlineData("\u0627\u0644", "\u0627\u0644\u0639\u0631\u0628\u064a\u0629")] // RTL text finds itself
    public void Partial_Words_Match_Ignoring_Case_And_Accents(string typed, string name)
    {
        var result = SearchMatch.Rank([Pick(name), Pick("Zzz")], typed);

        Assert.Equal([name], Names(result));
    }

    [Fact]
    public void Non_Matching_Text_Gives_Nothing()
    {
        Assert.Empty(SearchMatch.Rank([Pick("Alpha"), Open("Beta")], "gamma"));
    }

    [Fact]
    public void Starts_With_Comes_First()
    {
        var items = new[] { Pick("My Tunes"), Pick("Tutorial"), Pick("Autumn"), Pick("Tunes") };

        var result = SearchMatch.Rank(items, "tu");

        // Starts-with first, each part in the order given (stable); contains after.
        Assert.Equal(["Tutorial", "Tunes", "My Tunes", "Autumn"], Names(result));
    }

    [Fact]
    public void Starts_With_Ignores_Accents_Too()
    {
        var result = SearchMatch.Rank([Pick("Alpha \u00c9t\u00e9"), Pick("\u00e9t\u00e9 Beta")], "ete");

        Assert.Equal(["\u00e9t\u00e9 Beta", "Alpha \u00c9t\u00e9"], Names(result));
    }

    [Fact]
    public void Picks_Come_Before_Other_Open_Things()
    {
        var items = new[] { Open("Tutorial Window"), Pick("Autumn Tunes"), Open("Tunes Player"), Pick("Tunes", closed: true) };

        var result = SearchMatch.Rank(items, "tu");

        // Picks (starts-with, then contains), then open things (starts-with, then contains): a pick that only contains
        // the text still beats an open thing that starts with it.
        Assert.Equal(["Tunes", "Autumn Tunes", "Tutorial Window", "Tunes Player"], Names(result));
        Assert.True(result[0].IsClosed);
    }

    [Fact]
    public void The_Candidate_Is_Returned_Unchanged_With_Its_Key()
    {
        var item = new SearchCandidate("Alpha", "k-1", true, true);

        var result = SearchMatch.Rank([item], "alp");

        Assert.Same(item, Assert.Single(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("\u0301\u0301")]   // only combining marks fold to nothing
    public void Empty_Or_Blank_Text_Matches_Nothing(string? typed)
    {
        Assert.Empty(SearchMatch.Rank([Pick("Alpha"), Open("")], typed));
    }

    [Fact]
    public void A_Space_After_A_Word_Does_Not_Hide_It()
    {
        Assert.Equal(["Alpha Beta"], Names(SearchMatch.Rank([Pick("Alpha Beta")], "  alpha ")));
        Assert.Equal(["Alpha Beta"], Names(SearchMatch.Rank([Pick("Alpha Beta")], "alpha b")));
    }

    [Fact]
    public void Service_Tile_Is_Last_And_Names_The_Service()
    {
        var tile = SearchServices.Tile(["Spotify", "YouTube Music"], "tu");

        Assert.NotNull(tile);
        Assert.Equal(SearchService.YouTubeMusic, tile.Service);   // the latest known service, the last in the list
        Assert.Equal("Search tu on YouTube Music", tile.Name);
        Assert.Equal("music.youtube.com/search?q=tu", tile.Address);
    }

    [Fact]
    public void Service_Tile_Reads_Names_And_Hosts_Alike()
    {
        Assert.Equal(SearchService.YouTube, SearchServices.Tile(["www.youtube.com"], "x")!.Service);
        Assert.Equal(SearchService.YouTube, SearchServices.Tile(["YOUTUBE"], "x")!.Service);
        Assert.Equal(SearchService.Twitch, SearchServices.Tile(["twitch.tv"], "x")!.Service);
        Assert.Equal(SearchService.Spotify, SearchServices.Tile(["open.spotify.com"], "x")!.Service);
        Assert.Equal(SearchService.Spotify, SearchServices.Tile(["Spotify.exe".Replace(".exe", "")], "x")!.Service);
    }

    [Fact]
    public void Service_Tile_Uses_The_Latest_Known_Service_Even_After_An_Unknown_One()
    {
        var tile = SearchServices.Tile(["Twitch", "SoundCloud"], "tu");

        Assert.Equal(SearchService.Twitch, tile!.Service);
    }

    [Theory]
    [InlineData("SoundCloud")]
    [InlineData("soundcloud.com")]
    [InlineData("example.org")]
    [InlineData("Alpha")]
    [InlineData("music.example.org")]
    [InlineData("")]
    [InlineData(null)]
    public void An_Unknown_Service_Gets_No_Tile(string? service)
    {
        Assert.Null(SearchServices.Tile([service], "tu"));
        Assert.Null(SearchServices.Parse(service));
    }

    [Fact]
    public void No_Service_Played_Gets_No_Tile()
    {
        Assert.Null(SearchServices.Tile([], "tu"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_Text_Gets_No_Tile(string? typed)
    {
        Assert.Null(SearchServices.Tile(["YouTube"], typed));
    }

    // ---- adversarial ----

    [Fact]
    public void Very_Long_Text_Neither_Throws_Nor_Matches()
    {
        var typed = new string('a', 100_000);

        Assert.Empty(SearchMatch.Rank([Pick("Alpha")], typed));
        Assert.Equal(["Alpha"], Names(SearchMatch.Rank([Pick("Alpha")], "alp")));
    }

    [Fact]
    public void A_Very_Long_Name_Still_Matches()
    {
        var name = new string('x', 50_000) + "needle";

        Assert.Single(SearchMatch.Rank([Pick(name)], "needle"));
    }

    [Fact]
    public void A_Lone_Surrogate_Never_Throws()
    {
        var items = new[] { Pick("Alpha\ud800"), Pick("Be\udc00ta"), Pick("Gamma \U0001F3B5") };

        // A lone half reads as the replacement character on both sides, so it finds the other names that have one.
        Assert.Equal(["Alpha\ud800", "Be\udc00ta"], Names(SearchMatch.Rank(items, "\ud83d")));
        Assert.Equal(["Gamma \U0001F3B5"], Names(SearchMatch.Rank(items, "\U0001F3B5")));
    }

    [Fact]
    public void Text_That_Looks_Like_A_Path_Or_Address_Is_Plain_Text()
    {
        var items = new[] { Pick("C:\\Apps\\Alpha"), Pick("example.org/a?b=c#d"), Pick("100%") };

        Assert.Equal(["C:\\Apps\\Alpha"], Names(SearchMatch.Rank(items, "c:\\apps")));
        Assert.Equal(["example.org/a?b=c#d"], Names(SearchMatch.Rank(items, "org/a?b=c#")));
        Assert.Equal(["100%"], Names(SearchMatch.Rank(items, "100%")));
        Assert.Empty(SearchMatch.Rank(items, ".*"));
    }

    [Fact]
    public void Empty_Names_And_Null_Like_Input_Do_Not_Throw()
    {
        var result = SearchMatch.Rank([Pick(""), Open("Alpha")], "alpha");

        Assert.Equal(["Alpha"], Names(result));
    }
}
