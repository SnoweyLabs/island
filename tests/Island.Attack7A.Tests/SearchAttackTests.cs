using System.Diagnostics;
using Island.Core;

namespace Island.Attack7A.Tests;

/// <summary>Search: long, blank, invisible, astral, lone-surrogate and direction-changing text through layout, match, keys and addresses.</summary>
public class SearchAttackTests
{
    private static string Rep(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

    private static readonly string Long10k = new('x', 10_000);

    private static readonly string[] Texts =
    [
        "", " ", "     ", "\t\n", "tu", "alpha", "Tu Tu", "  tu  ", Long10k, new string(' ', 10_000), new string('́', 10_000), new string('​', 500),
        "́", "é", "‍", "﻿", "😀", "😀😀😀", Rep("😀", 5000), "\uD83D", "\uDE00", "a\uD83Db", "\uDE00\uD83D", "‮abc", "abc‮", "‮abc‬",
        "a/b", "a?b", "a#b", "a&b=c", "a+b", "100%", "%2F", "../..", "a b", "ß", "İ", "ǅ", "ﷺ", Rep("ﷺ", 3000), "￾￿", "\u0000", "Ａ", "ｱ", "a\u0000b",
    ];

    [Fact]
    public void Holds_Layout_Match_And_Addresses_Never_Throw_Whatever_The_Text()
    {
        var picks = new[] { new SearchCandidate("Tunes", "program:tunes", true, false), new SearchCandidate("Tutorial", "program:tut", true, true), new SearchCandidate("a\uD83Db", "w1", false, false), new SearchCandidate("", "w2", false, false) };
        foreach (var text in Texts)
        {
            var ranked = SearchMatch.Rank(picks, text);
            Assert.True(ranked.Count <= picks.Length);
            var tile = SearchServices.Tile(["YouTube"], text);
            _ = SearchMatch.Fold(text);
            foreach (var service in Enum.GetValues<SearchService>()) _ = SiteAddress.ForSearch(service, text);
            foreach (var n in new[] { text.Length, int.MaxValue, int.MinValue, 0 })
            {
                var w = SearchLayout.Width(n, int.MaxValue);
                Assert.True(double.IsFinite(w) && w > 0 && w < 1500, $"width {w}");
            }

            _ = tile;
        }
    }

    [Fact]
    public void Holds_Ten_Thousand_Characters_Are_Fast()
    {
        var picks = Enumerable.Range(0, 2000).Select(i => new SearchCandidate("Name " + i, "program:n" + i, true, false)).ToList();
        var sw = Stopwatch.StartNew();
        foreach (var text in new[] { Long10k, new string('́', 10_000), Rep("ﷺ", 3000), Rep("😀", 5000) }) SearchMatch.Rank(picks, text);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Holds_Rank_Puts_Picks_Before_Open_Things_And_Starts_With_First_Inside_Each_Group()
    {
        var set = new[]
        {
            new SearchCandidate("My tunes", "1", false, false), new SearchCandidate("Tunes", "2", false, false), new SearchCandidate("Mytunes", "3", true, false),
            new SearchCandidate("Tunes Pro", "4", true, false), new SearchCandidate("Étunes", "5", true, true),
        };
        var keys = SearchMatch.Rank(set, " TUNES ").Select(c => c.Key).ToArray();
        Assert.Equal(["4", "3", "5", "2", "1"], keys);
        Assert.Empty(SearchMatch.Rank(set, "́"));
        Assert.Empty(SearchMatch.Rank(set, "   "));
    }

    [Fact]
    public void Holds_Addresses_Are_Well_Formed_And_Encoded_For_Every_Text_And_Service()
    {
        foreach (var service in Enum.GetValues<SearchService>())
        foreach (var text in Texts)
        {
            var address = SiteAddress.ForSearch(service, text);
            if (string.IsNullOrWhiteSpace(text)) { Assert.Null(address); continue; }
            Assert.NotNull(address);
            Assert.True(Uri.TryCreate(address, UriKind.Absolute, out var uri), address);
            var expectedHost = service switch { SearchService.YouTube => "www.youtube.com", SearchService.YouTubeMusic => "music.youtube.com", SearchService.Twitch => "www.twitch.tv", SearchService.Google => "www.google.com", _ => "open.spotify.com" };
            Assert.Equal("https", uri!.Scheme);
            Assert.Equal(expectedHost, uri.Host);
            Assert.True(address!.All(ch => ch < 128 && (char.IsAsciiLetterOrDigit(ch) || "-._~%/:?=".Contains(ch))), "raw character in " + address);
            Assert.True(address.Length < 2100, $"length {address.Length}");
            var encoded = address[(address.LastIndexOfAny(['=', '/']) + 1)..];
            Assert.DoesNotContain('/', encoded);
            if (service == SearchService.Spotify) Assert.Equal(["search", encoded], uri.AbsolutePath.Trim('/').Split('/'));
            else Assert.Equal(1, address.Count(ch => ch == '='));
        }
    }

    [Fact]
    public void Holds_The_Encoded_Text_Reads_Back_As_The_Typed_Text_With_Lone_Surrogates_As_FFFD()
    {
        foreach (var text in new[] { "a b&c/d?e#f+g%h", "Ünï cödé", "😀 x", "a\uD83Db", "\uDE00" })
        {
            var encoded = SearchServices.Encode(text);
            var back = Uri.UnescapeDataString(encoded);
            Assert.Equal(System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(text)), back);
        }
    }

    [Fact]
    public void Holds_Services_Are_Only_The_Four_Hosts_And_Look_Alikes_Are_Refused()
    {
        foreach (var bad in new[] { "youtube.com.evil.example", "evil.example/youtube.com", "user@youtube.com", "youtube.com@evil.example", "youtu.be", "soundcloud.com", "music.youtube.com.evil.example", "xyoutube.com", "", "  ", null })
            Assert.Null(SearchServices.Parse(bad));
        Assert.Equal(SearchService.YouTube, SearchServices.Parse(" YOUTUBE.COM. "));
        Assert.Equal(SearchService.Spotify, SearchServices.Parse("spotify"));
        Assert.Equal(SearchService.YouTubeMusic, SearchServices.Parse("www.music.youtube.com") ?? SearchService.YouTubeMusic);
        Assert.Null(SearchServices.Tile(["SoundCloud", null, ""], "tu"));
        Assert.Equal(SearchService.Twitch, SearchServices.Tile(["YouTube", "Twitch", "SoundCloud"], "tu")!.Service);
    }

    [Fact]
    public void Defect_A_Service_Value_Outside_The_Enum_Throws_Instead_Of_Giving_Nothing()
    {
        var bogus = (SearchService)99;
        Assert.Null(Record.Exception(() => SiteAddress.ForSearch(bogus, "tu")));
    }

    [Theory]
    [InlineData("​")]
    [InlineData("‍")]
    [InlineData("⁠")]
    [InlineData("﻿")]
    [InlineData("́")]
    public void Defect_A_Typed_Invisible_Character_Opens_Search_On_A_Field_That_Looks_Empty(string invisible)
    {
        var result = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed(invisible));
        Assert.Equal(SearchKeyAction.None, result.Action);
    }

    [Fact]
    public void Defect_The_Service_Tile_Name_Keeps_A_Right_To_Left_Override_That_Reorders_The_Words_After_It()
    {
        var tile = SearchServices.Tile(["YouTube"], "abc‮def")!;
        Assert.DoesNotContain('‮', tile.Name); // the notice's project name drops these; the tile's name is "Search <text> on YouTube", reordered after the override
    }

    [Fact]
    public void Holds_Keys_Fuzz_Text_Stays_Bounded_Without_Controls_And_The_Selection_Stays_In_Range()
    {
        string?[] typed = ["a", "5", "0", " ", "  ", "​", "\uD83D", "\uDE00", "😀", "é", "é", "‮", "\n", "\t", "", null, "ab", new string('z', 300), Long10k, "1"];
        SearchKey[] others = [SearchKey.Left, SearchKey.Right, SearchKey.Enter, SearchKey.Backspace, SearchKey.Escape];
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var r = new Random(seed);
            var state = SearchState.Closed;
            for (var i = 0; i < 30_000; i++)
            {
                var key = r.Next(3) == 0 ? others[r.Next(others.Length)] : SearchKey.Typed(typed[r.Next(typed.Length)]!);
                var before = state;
                var result = SearchKeys.Apply(state, key);
                state = result.State;
                Assert.True(state.Text.Length <= SearchKeys.MaxFieldChars);
                Assert.True(state.Text.All(c => !char.IsControl(c)));
                Assert.True(state.Selected >= 0 && (state.TileCount == 0 ? state.Selected == 0 : state.Selected < state.TileCount), $"selected {state.Selected} of {state.TileCount}");
                if (result.Action == SearchKeyAction.Activate) Assert.True(before.IsOpen && before.TileCount > 0);
                if (result.Action == SearchKeyAction.Leave) Assert.False(state.IsOpen);
                if (!state.IsOpen) Assert.Equal("", state.Text);
                if (result.Action == SearchKeyAction.SwitchPage) Assert.InRange(result.Page, 0, 9);
                if (state.IsOpen && result.Action is SearchKeyAction.Opened or SearchKeyAction.TextChanged) state = state with { TileCount = r.Next(0, 13) }; // the caller counts the matches
            }
        }
    }

    [Fact]
    public void Holds_Backspace_Takes_A_Whole_Pair_And_Escape_Clears_Then_Leaves()
    {
        var s = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("a😀")).State;
        Assert.Equal("a😀", s.Text);
        s = SearchKeys.Apply(s, SearchKey.Backspace).State;
        Assert.Equal("a", s.Text);
        s = SearchKeys.Apply(s, SearchKey.Escape).State;
        Assert.True(s.IsOpen);
        Assert.Equal("", s.Text);
        Assert.Equal(SearchKeyAction.Leave, SearchKeys.Apply(s, SearchKey.Escape).Action);
        Assert.Equal(SearchKeyAction.SwitchPage, SearchKeys.Apply(s, SearchKey.Typed("3")).Action);
        var typed = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("a")).State;
        Assert.Equal(SearchKeyAction.TextChanged, SearchKeys.Apply(typed, SearchKey.Typed("3")).Action); // digits are text once the field has text
    }

    [Fact]
    public void Holds_A_Text_Over_The_Field_Limit_Is_Ignored_Not_Cut()
    {
        var s = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed("a")).State;
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(s, SearchKey.Typed(Long10k)).Action);
        var full = SearchKeys.Apply(SearchState.Closed, SearchKey.Typed(new string('q', 256))).State;
        Assert.Equal(256, full.Text.Length);
        Assert.Equal(SearchKeyAction.None, SearchKeys.Apply(full, SearchKey.Typed("q")).Action);
    }
}
