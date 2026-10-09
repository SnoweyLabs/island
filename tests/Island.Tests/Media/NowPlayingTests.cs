using Island.Core;

namespace Island.Tests;

public class NowPlayingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly NowPlayingPolicy Picked = new(["youtube.com", "twitch.tv"], DesktopPlayersCount: true, BrowserSessionCounts: false);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static MediaSessionInfo Session(string id, string title, PlaybackState state, bool browser = false, double? pos = null, double? len = null) =>
        new(id, browser ? "chrome.exe" : $"{id}.exe", browser, title, "Artist", state, pos, len);

    private static TabInfo Tab(string key, string host, string title, PlaybackState state, long order = 1, double? pos = null, double? len = null) =>
        new(key, 1, 1, "page", host, state == PlaybackState.Playing, false, order, new TabMedia(title, null, state, pos, len));

    private static NowPlayingView? Feed(NowPlaying np, double t, MediaSessionInfo[]? sessions = null, TabInfo[]? tabs = null,
        bool connected = true, NowPlayingPolicy? policy = null) =>
        np.Update(At(t), sessions ?? [], tabs ?? [], connected, policy ?? Picked);

    [Fact]
    public void Most_Recent_Start_Or_Resume_Wins()
    {
        var np = new NowPlaying();

        Assert.Equal("One", Feed(np, 0, tabs: [Tab("a", "youtube.com", "One", PlaybackState.Playing)])!.Title);

        // A second tab starts: it takes the line.
        Assert.Equal("Two", Feed(np, 1, tabs: [Tab("a", "youtube.com", "One", PlaybackState.Playing), Tab("b", "youtube.com", "Two", PlaybackState.Playing, 2)])!.Title);

        // The first is paused and played again: back to the first, although the second still plays.
        Feed(np, 2, tabs: [Tab("a", "youtube.com", "One", PlaybackState.Paused), Tab("b", "youtube.com", "Two", PlaybackState.Playing, 2)]);
        var back = Feed(np, 3, tabs: [Tab("a", "youtube.com", "One", PlaybackState.Playing), Tab("b", "youtube.com", "Two", PlaybackState.Playing, 2)]);
        Assert.Equal("One", back!.Title);

        // The same between a browser tab and a desktop player.
        var spotify = Session("spotify", "Song", PlaybackState.Playing);
        Assert.Equal("Song", Feed(np, 4, [spotify], [Tab("a", "youtube.com", "One", PlaybackState.Playing)])!.Title);
        var paused = Session("spotify", "Song", PlaybackState.Paused);
        Feed(np, 5, [paused], [Tab("a", "youtube.com", "One", PlaybackState.Paused)]);
        Assert.Equal("One", Feed(np, 6, [paused], [Tab("a", "youtube.com", "One", PlaybackState.Playing)])!.Title);
        Feed(np, 7, [paused], [Tab("a", "youtube.com", "One", PlaybackState.Paused)]);
        Assert.Equal("Song", Feed(np, 8, [spotify], [Tab("a", "youtube.com", "One", PlaybackState.Paused)])!.Title);
    }

    [Fact]
    public void Paused_Keeps_The_Last_Item()
    {
        var np = new NowPlaying();
        Feed(np, 0, [Session("alpha", "Song", PlaybackState.Playing)]);

        var view = Feed(np, 1, [Session("alpha", "Song", PlaybackState.Paused)]);

        Assert.NotNull(view);
        Assert.Equal("Song", view.Title);
        Assert.True(view.IsPaused);
        Assert.Equal("Alpha - paused", view.SecondLine);
        Assert.Equal("Song", np.Current(At(300))!.Title); // nothing but time has passed: it stays
        Assert.Equal(MediaCommand.PlayPause, np.PlanFor(MediaCommand.PlayPause)!.Command); // play can resume it
    }

    [Fact]
    public void Nothing_Played_Shows_Nothing()
    {
        var np = new NowPlaying();
        Assert.Null(np.Current(At(0)));
        Assert.Null(np.PlanFor(MediaCommand.Next));

        var view = Feed(np, 0, [Session("alpha", "Song", PlaybackState.Paused), Session("beta", "Other", PlaybackState.Stopped)],
            [Tab("a", "youtube.com", "One", PlaybackState.Paused)]);

        Assert.Null(view);
        Assert.Null(np.PlanFor(MediaCommand.PlayPause));
    }

    [Fact]
    public void Controls_Target_The_Now_Playing_Item()
    {
        var np = new NowPlaying();
        var spotify = Session("alpha", "Song", PlaybackState.Playing);
        Feed(np, 0, [spotify], [Tab("a", "youtube.com", "One", PlaybackState.Paused)]);

        // The player is the item: all three commands go to its session id and to nothing else.
        foreach (var command in Enum.GetValues<MediaCommand>())
            Assert.Equal(new MediaCommandPlan(new MediaTarget(MediaTargetKind.Session, "alpha"), command), np.PlanFor(command));

        // The tab starts: the commands follow it, addressed by tab key.
        Feed(np, 1, [spotify], [Tab("a", "youtube.com", "One", PlaybackState.Playing)]);
        foreach (var command in Enum.GetValues<MediaCommand>())
            Assert.Equal(new MediaCommandPlan(new MediaTarget(MediaTargetKind.Tab, "a"), command), np.PlanFor(command));

        // Sending it reaches the tab door only.
        var world = new PretendWorld { Tabs = [Tab("a", "youtube.com", "One", PlaybackState.Playing)], Sessions = [spotify] };
        Assert.True(np.PlanFor(MediaCommand.Next)!.Send(world, world));
        Assert.Equal(["tab-media:a:Next"], world.Sent);
    }

    [Fact]
    public void Title_Change_Event_Updates_The_Line()
    {
        var np = new NowPlaying();
        Feed(np, 0, [Session("alpha", "First", PlaybackState.Playing)]);

        // One report, no time passing between them: the title is what the source reported.
        var view = Feed(np, 0.1, [Session("alpha", "Second", PlaybackState.Playing)]);
        Assert.Equal("Second", view!.Title);

        // Also on a tab, and also while paused.
        Feed(np, 1, tabs: [Tab("a", "youtube.com", "Video", PlaybackState.Playing)]);
        Assert.Equal("Next video", Feed(np, 1.1, tabs: [Tab("a", "youtube.com", "Next video", PlaybackState.Playing)])!.Title);
        Assert.Equal("Third", Feed(np, 1.2, tabs: [Tab("a", "youtube.com", "Third", PlaybackState.Paused)])!.Title);
    }

    [Fact]
    public void Unpicked_Site_Does_Not_Take_Over()
    {
        var np = new NowPlaying();
        var other = Tab("x", "example.org", "Autoplay", PlaybackState.Playing, 5);

        // Nothing picked-site has played: an unpicked site leaves the line empty.
        Assert.Null(Feed(np, 0, tabs: [other]));

        // A picked tab plays; an unpicked one starting later does not take it.
        Feed(np, 1, tabs: [Tab("a", "youtube.com", "One", PlaybackState.Playing), other]);
        var view = Feed(np, 2, tabs: [Tab("a", "youtube.com", "One", PlaybackState.Playing), other, Tab("y", "example.net", "More", PlaybackState.Playing, 9)]);
        Assert.Equal("One", view!.Title);

        // "www." and case do not matter, and a subdomain is another site (music.youtube.com is not youtube.com).
        Assert.Equal("Music", Feed(np, 3, tabs: [Tab("m", "WWW.Twitch.tv", "Music", PlaybackState.Playing, 6)])!.Title);
        Assert.Equal("Music", Feed(np, 4, tabs: [Tab("m", "twitch.tv", "Music", PlaybackState.Playing, 6), Tab("n", "music.youtube.com", "Nope", PlaybackState.Playing, 7)])!.Title);
    }

    [Fact]
    public void Desktop_Players_Only_Count_When_The_Policy_Says_So()
    {
        var np = new NowPlaying();
        var noDesktop = Picked with { DesktopPlayersCount = false };
        Assert.Null(Feed(np, 0, [Session("alpha", "Song", PlaybackState.Playing)], policy: noDesktop));
    }

    [Fact]
    public void Browser_Session_Counts_Only_Without_The_Addon_And_With_A_Site_Pick()
    {
        var browser = Session("chrome", "Some tab", PlaybackState.Playing, browser: true);
        var live = new NowPlayingPolicy(["youtube.com"], true, BrowserSessionCounts: true);

        // No add-on: the session is the browser as a whole and counts.
        var np = new NowPlaying();
        var view = Feed(np, 0, [browser], connected: false, policy: live);
        Assert.True(view!.IsBrowserSession);
        Assert.Equal("Chrome", view.Where);
        Assert.Equal(MediaTargetKind.Session, np.PlanFor(MediaCommand.Next)!.Target.Kind);

        // Add-on connected: the tabs speak for the browser, its session is ignored.
        Assert.Null(Feed(new NowPlaying(), 0, [browser], connected: true, policy: live));

        // No site picked on the Media page: the browser session does not count.
        var noSites = NowPlayingPolicy.ForMediaPagePicks([Pick.ForProgram("Alpha", PageIds.Media, "alpha.exe", null)]);
        Assert.False(noSites.BrowserSessionCounts);
        Assert.Null(Feed(new NowPlaying(), 0, [browser], connected: false, policy: noSites));

        var withSite = NowPlayingPolicy.ForMediaPagePicks([Pick.ForSite("Beta", "example.org", PageIds.Media)]);
        Assert.True(withSite.BrowserSessionCounts);
        Assert.Equal(["example.org"], withSite.PickedHosts);
    }

    [Fact]
    public void A_Session_That_Vanishes_For_A_Moment_Does_Not_Blank_The_Line()
    {
        var np = new NowPlaying();
        Feed(np, 0, [Session("alpha", "Song", PlaybackState.Playing)]);

        var gap = Feed(np, 1);
        Assert.Equal("Song", gap!.Title);
        Assert.False(gap.CanControl);
        Assert.Null(np.PlanFor(MediaCommand.Next)); // nothing to send to while it is gone

        // Back with the next track: the same item, now with its new title, controllable again, not a "new start" elsewhere.
        var back = Feed(np, 2, [Session("alpha", "Next song", PlaybackState.Playing)]);
        Assert.Equal("Next song", back!.Title);
        Assert.True(back.CanControl);
    }

    [Fact]
    public void A_Session_That_Disappears_For_Good_Clears_The_Line_After_The_Grace()
    {
        var np = new NowPlaying();
        Feed(np, 0, [Session("alpha", "Song", PlaybackState.Paused)]);
        Feed(np, 1, [Session("alpha", "Song", PlaybackState.Playing)]);

        Assert.NotNull(Feed(np, 2));
        Assert.NotNull(np.Current(At(1) + NowPlaying.VanishGrace));
        Assert.Null(np.Current(At(1) + NowPlaying.VanishGrace + TimeSpan.FromSeconds(1)));
        Assert.Null(Feed(np, 10));
        Assert.Null(np.PlanFor(MediaCommand.PlayPause));
    }

    [Fact]
    public void A_Vanished_Item_Hands_The_Line_To_Whatever_Else_Is_Playing()
    {
        var np = new NowPlaying();
        var beta = Session("beta", "Other", PlaybackState.Playing);
        Feed(np, 0, [beta]);
        Feed(np, 1, [Session("alpha", "Song", PlaybackState.Playing), beta]);

        Assert.Equal("Song", Feed(np, 2, [beta])!.Title);        // alpha gone, within the grace
        Assert.Equal("Other", Feed(np, 10, [beta])!.Title);      // grace over: beta, which is still playing
    }

    [Fact]
    public void When_The_Item_Pauses_The_Line_Follows_Another_That_Still_Plays()
    {
        var np = new NowPlaying();
        var beta = Session("beta", "Other", PlaybackState.Playing);
        Feed(np, 0, [beta]);
        Feed(np, 1, [Session("alpha", "Song", PlaybackState.Playing), beta]);

        Assert.Equal("Other", Feed(np, 2, [Session("alpha", "Song", PlaybackState.Paused), beta])!.Title);
        // Both paused: the last item stays.
        Assert.Equal("Other", Feed(np, 3, [Session("alpha", "Song", PlaybackState.Paused), Session("beta", "Other", PlaybackState.Paused)])!.Title);
    }

    [Fact]
    public void Unknown_Length_Shows_No_Progress()
    {
        var np = new NowPlaying();

        var live = Feed(np, 0, tabs: [Tab("a", "twitch.tv", "Live", PlaybackState.Playing, pos: 30, len: null)]);
        Assert.Null(live!.Progress);
        Assert.Null(live.LengthSeconds);

        Assert.Null(Feed(new NowPlaying(), 0, [Session("alpha", "Stream", PlaybackState.Playing, pos: 10, len: 0)])!.Progress);       // length 0 is "unknown"
        Assert.Null(Feed(new NowPlaying(), 0, [Session("alpha", "Song", PlaybackState.Playing, pos: null, len: 200)])!.Progress);       // no position reported
        Assert.Null(new NowPlaying().Current(At(0)));
    }

    [Fact]
    public void Known_Length_Gives_Progress_That_Moves_While_Playing_And_Stays_When_Paused()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [Session("alpha", "Song", PlaybackState.Playing, pos: 50, len: 200)]);
        Assert.Equal(0.25, view!.Progress);

        Assert.Equal(0.5, np.Current(At(50))!.Progress);   // 50 s later, still playing
        Assert.Equal(1.0, np.Current(At(900))!.Progress);  // never past the end

        var paused = Feed(np, 100, [Session("alpha", "Song", PlaybackState.Paused, pos: 100, len: 200)]);
        Assert.Equal(0.5, paused!.Progress);
        Assert.Equal(0.5, np.Current(At(500))!.Progress);
    }

    [Fact]
    public void A_Missing_Title_Falls_Back_To_Where_It_Plays()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [new MediaSessionInfo("alpha", "alpha.exe", false, null, null, PlaybackState.Playing, null, null)]);
        Assert.Equal("Alpha", view!.Title);
    }
}
