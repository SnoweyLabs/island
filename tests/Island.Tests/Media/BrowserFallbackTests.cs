using Island.Core;

namespace Island.Tests;

/// <summary>
/// WORK-ORDER-9 section 3: with the add-on connected, Windows' own report about the browser fills the Now-playing line in exactly one case: a tab on a
/// picked site is making sound, no tab on a picked site has said it is playing, and Windows says the browser is playing. Invented names only.
/// </summary>
public class BrowserFallbackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<Pick> Picks =
    [
        Pick.ForSite("Alpha Video", "youtube.com", PageIds.Media),
        Pick.ForSite("Beta Stream", "twitch.tv", PageIds.Media),
    ];

    private static readonly NowPlayingPolicy Policy = NowPlayingPolicy.ForMediaPagePicks(Picks);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static MediaSessionInfo Browser(PlaybackState state, string title = "Windows title", double? position = 30, double? length = 200) =>
        new("browser-1", "chrome.exe", true, title, "Windows artist", state, position, length);

    /// <summary>A tab that makes sound and has said nothing (a page the add-on cannot read).</summary>
    private static TabInfo Silent(string key, string host, long order = 1, bool audible = true) => new(key, 1, 1, "page", host, audible, false, order, null);

    /// <summary>A tab whose page reported what it plays.</summary>
    private static TabInfo Reporting(string key, string host, PlaybackState state, string title, long order = 1) =>
        new(key, 1, 1, "page", host, state == PlaybackState.Playing, false, order, new TabMedia(title, "Tab artist", state, 5, 100));

    private static NowPlayingView? Feed(NowPlaying np, double t, IReadOnlyList<MediaSessionInfo> sessions, IReadOnlyList<TabInfo> tabs, bool connected = true) =>
        np.Update(At(t), sessions, tabs, connected, Policy);

    [Fact]
    public void Counts_When_A_Picked_Site_Tab_Is_Audible_And_Silent()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")]);

        Assert.NotNull(view);
        Assert.Equal("Windows title", view.Title);
        Assert.Equal("Windows artist", view.Artist);
        Assert.Equal("YouTube", view.Where);
        Assert.Equal("YouTube · tab", PlayingTile.SecondLine(view));
        Assert.False(view.IsPaused);
        Assert.Equal(0.15, view.Progress!.Value, 3); // from Windows' session, as for a desktop player
        Assert.Equal(Picks[0].Id, PlayingTile.PickIdFor(view, Picks)); // the tile of that pick dances
        Assert.Equal("t1", view.FallbackTabKey); // a click on the line goes to the tab
    }

    [Fact]
    public void Ignored_When_A_Tab_Reports_Playing()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [Browser(PlaybackState.Playing)], [Reporting("t2", "twitch.tv", PlaybackState.Playing, "Tab title", 2)]);

        Assert.Equal("Tab title", view!.Title); // the tab's report is better: it knows which tab
        Assert.Equal(MediaTargetKind.Tab, view.Target.Kind);
        Assert.Null(view.FallbackTabKey);

        // A tab that has reported, in any state, speaks for itself and is never a reason for the fallback.
        foreach (var state in new[] { PlaybackState.Paused, PlaybackState.Stopped })
        {
            var paused = new TabInfo("t3", 1, 1, "page", "youtube.com", true, false, 1, new TabMedia("Reported", null, state, null, null));
            var seen = Feed(new NowPlaying(), 0, [Browser(PlaybackState.Playing)], [paused]);
            Assert.True(seen is null || seen.FallbackTabKey is null, $"an audible tab that reported {state} is not silent");
            Assert.True(seen is null || seen.Title != "Windows title");
        }
    }

    [Fact]
    public void Ignored_When_Only_Unpicked_Sites_Are_Audible()
    {
        // EVALS N7: a site nobody picked never takes the line.
        Assert.Null(Feed(new NowPlaying(), 0, [Browser(PlaybackState.Playing)], [Silent("t1", "example.org")]));
        Assert.Null(Feed(new NowPlaying(), 0, [Browser(PlaybackState.Playing)], [Silent("t1", "music.youtube.com")])); // another site than the one picked
    }

    [Fact]
    public void Ignored_When_Windows_Says_Paused_And_No_Tab_Is_Audible()
    {
        Assert.Null(Feed(new NowPlaying(), 0, [Browser(PlaybackState.Paused)], [Silent("t1", "youtube.com")]));    // a tab makes sound, Windows says paused
        Assert.Null(Feed(new NowPlaying(), 0, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com", audible: false)])); // Windows says playing, no tab makes sound
        Assert.Null(Feed(new NowPlaying(), 0, [Browser(PlaybackState.Playing)], []));                                  // no tab at all
    }

    [Fact]
    public void Where_Is_The_Most_Recent_Audible_Picked_Tab()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [Browser(PlaybackState.Playing)], [Silent("old", "youtube.com", 3), Silent("new", "twitch.tv", 9), Silent("quiet", "youtube.com", 20, audible: false), Silent("other", "example.org", 30)]);

        Assert.Equal("Twitch", view!.Where);
        Assert.Equal("new", view.FallbackTabKey);
        Assert.Equal(Picks[1].Id, PlayingTile.PickIdFor(view, Picks));
    }

    [Fact]
    public void A_Tab_Report_Takes_Over_Without_A_New_Item()
    {
        var np = new NowPlaying();
        var first = Feed(np, 0, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")]);
        Assert.Equal("Windows title", first!.Title);

        // The tab's own report arrives; Windows still says playing. The line is never empty and keeps the same item.
        var tabs = new[] { Reporting("t1", "youtube.com", PlaybackState.Playing, "Tab title") };
        var second = Feed(np, 0.5, [Browser(PlaybackState.Playing)], tabs);
        Assert.Equal("Tab title", second!.Title);
        Assert.Equal(MediaTargetKind.Tab, second.Target.Kind);
        Assert.Equal("t1", second.Target.Id);
        Assert.True(second.CanControl, "no grace period, no blink: the item was present the whole time");
        Assert.False(second.IsPaused);
        Assert.Equal(MediaTargetKind.Tab, np.PlanFor(MediaCommand.Next)!.Target.Kind);

        // And nothing between the two was empty or was a second item: a start would have made another item.
        Assert.Equal(second.Title, np.Current(At(0.6))!.Title);

        // Back the other way: the tab stops reporting (the page changed), the fallback speaks again, still without a gap.
        var third = Feed(np, 1, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")]);
        Assert.Equal("Windows title", third!.Title);
        Assert.True(third.CanControl);
    }

    [Fact]
    public void Controls_Go_To_The_Windows_Session()
    {
        var np = new NowPlaying();
        Feed(np, 0, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")]);

        foreach (var command in new[] { MediaCommand.Previous, MediaCommand.PlayPause, MediaCommand.Next })
            Assert.Equal(new MediaCommandPlan(new MediaTarget(MediaTargetKind.Session, "browser-1"), command), np.PlanFor(command));
    }

    [Fact]
    public void Without_The_Addon_It_Is_As_Before()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [Browser(PlaybackState.Playing)], [], connected: false);
        Assert.True(view!.IsBrowserSession);
        Assert.Equal("Chrome", view.Where);
        Assert.Null(view.FallbackTabKey);
        Assert.Null(PlayingTile.PickIdFor(view, Picks)); // the whole browser belongs to no pick
    }

    [Fact]
    public void Exactly_One_Browser_Session_Must_Say_It_Is_Playing()
    {
        // A tab does not say which browser it belongs to, and a session is never matched to a browser by a program's name: with two playing, Windows' words are ignored,
        // in whichever order Windows lists them.
        var other = new MediaSessionInfo("browser-2", "edge.exe", true, "Other title", null, PlaybackState.Playing, null, null);
        Assert.Null(Feed(new NowPlaying(), 0, [Browser(PlaybackState.Playing), other], [Silent("t1", "youtube.com")]));
        Assert.Null(Feed(new NowPlaying(), 0, [other, Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")]));

        // One playing and one paused is one: the playing one speaks.
        var paused = other with { State = PlaybackState.Paused };
        Assert.Equal("Windows title", Feed(new NowPlaying(), 0, [paused, Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")])!.Title);
    }

    [Fact]
    public void A_Vanishing_Session_Holds_For_The_Grace_Period_And_Then_The_Line_Goes()
    {
        var np = new NowPlaying();
        Assert.Equal("Windows title", Feed(np, 0, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com")])!.Title);
        Assert.Equal("Windows title", Feed(np, 1, [], [Silent("t1", "youtube.com")])!.Title);
        Assert.Null(Feed(np, 10, [], [Silent("t1", "youtube.com")]));
    }
}
