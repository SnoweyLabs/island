using Island.Core;

namespace Island.Attack9.Tests;

/// <summary>
/// ATTACK9 on WORK-ORDER-9 section 3 (the browser fallback of NowPlaying.Candidates). Defect_ tests fail because of the defect they name; Holds_ tests pass.
/// Invented names only: "Alpha Video" is a pick of youtube.com, "Beta Stream" of twitch.tv, example.org is a site nobody picked.
/// </summary>
public class FallbackAttackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<Pick> Picks =
    [
        Pick.ForSite("Alpha Video", "youtube.com", PageIds.Media),
        Pick.ForSite("Beta Stream", "twitch.tv", PageIds.Media),
    ];

    private static readonly NowPlayingPolicy Policy = NowPlayingPolicy.ForMediaPagePicks(Picks);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static MediaSessionInfo Browser(PlaybackState state, string id = "browser-1", string title = "Windows title", double? position = 30, double? length = 200) =>
        new(id, "chrome.exe", true, title, "Windows artist", state, position, length);

    private static TabInfo Silent(string key, string host, long order = 1, bool audible = true) => new(key, 1, 1, "page", host, audible, false, order, null);

    private static TabInfo Reporting(string key, string host, PlaybackState state, bool audible, long order = 1, string title = "Tab title") =>
        new(key, 1, 1, "page", host, audible, false, order, new TabMedia(title, "Tab artist", state, 5, 100));

    private static NowPlayingView? Feed(NowPlaying np, double t, IReadOnlyList<MediaSessionInfo> sessions, IReadOnlyList<TabInfo> tabs, bool connected = true) =>
        np.Update(At(t), sessions, tabs, connected, Policy);

    // ---------------------------------------------------------------------------------------------------------------- defects

    [Theory]
    [InlineData(PlaybackState.Paused)]
    [InlineData(PlaybackState.Stopped)]
    public void Defect_An_Audible_Tab_That_Has_Reported_Paused_Is_Still_Taken_For_Silent(PlaybackState reported)
    {
        // WORK-ORDER-9 section 3: "A tab that has reported - playing, paused or stopped - speaks for itself and is never a reason for the fallback."
        // SilentAudibleTab keeps every audible picked tab, whatever its report says. Chrome keeps "audible" on for a moment after a pause, and Windows
        // names the browser as playing while any other tab of it plays: then Windows' words (another tab's title) are shown as this picked site, playing,
        // and they win over the tab's own paused report (the group by key prefers the playing one).
        var np = new NowPlaying();
        var view = Feed(np, 0, [Browser(PlaybackState.Playing)], [Reporting("t1", "youtube.com", reported, audible: true)]);

        Assert.True(view is null || view.FallbackTabKey is null, $"the line shows Windows' words ('{view?.Title}') for a tab that reported {reported}");
    }

    [Fact]
    public void Defect_Going_Back_To_A_Tab_Used_A_Moment_Ago_Leaves_The_Line_On_The_Other_One_For_The_Grace_Period()
    {
        // Two audible picked tabs that have said nothing; the most recently used one is the one the fallback speaks for. The key of the item is "tab:<key>", so
        // switching to the other tab changes the item. The first switch is a new item and takes the line at once; the switch back finds the old item's memory
        // still saying "playing" (it is only forgotten after the 4 s grace), so it is not a start, and the line stays on the tab that is no longer the one:
        // wrong tab on click, buttons dead (CanControl false), for up to four seconds.
        var np = new NowPlaying();
        var browser = new[] { Browser(PlaybackState.Playing) };

        Assert.Equal("b", Feed(np, 0, browser, [Silent("a", "youtube.com", 5), Silent("b", "twitch.tv", 9)])!.FallbackTabKey);
        Assert.Equal("a", Feed(np, 1, browser, [Silent("a", "youtube.com", 12), Silent("b", "twitch.tv", 9)])!.FallbackTabKey);

        var back = Feed(np, 2, browser, [Silent("a", "youtube.com", 12), Silent("b", "twitch.tv", 15)]);
        Assert.Equal("b", back!.FallbackTabKey);
        Assert.True(back.CanControl);
        Assert.NotNull(np.PlanFor(MediaCommand.PlayPause));
    }

    [Fact]
    public void Defect_Two_Playing_Browser_Sessions_Are_Chosen_By_The_Order_Windows_Lists_Them()
    {
        // WORK-ORDER-9 section 3 asks for exactly one playing browser session ("a session is never matched to a browser ... that is why the rule asks for
        // exactly one"). The code takes the first playing one, so the reading order of the sessions decides whose title is shown and where the buttons go.
        // (The existing test BrowserFallbackTests.Two_Browsers_And_A_Vanishing_Session_Never_Make_Two_Items_Or_A_Crash pins "the first playing session is taken"
        // for one order; it stays as it is: this test only asks that the result does not depend on the order.)
        var one = Browser(PlaybackState.Playing, "browser-1", "First title");
        var two = Browser(PlaybackState.Playing, "browser-2", "Second title");
        var tabs = new[] { Silent("t1", "youtube.com") };

        var forward = Feed(new NowPlaying(), 0, [one, two], tabs);
        var backward = Feed(new NowPlaying(), 0, [two, one], tabs);

        Assert.Equal(forward?.Title, backward?.Title);
        Assert.Equal(forward?.Target, backward?.Target);
    }

    // ---------------------------------------------------------------------------------------------------------------- what held

    [Fact]
    public void Holds_Two_Playing_Sessions_Flipping_Their_Order_Between_Readings_Are_Ignored_And_One_Alone_Speaks_Without_A_Gap()
    {
        // Version 2 of the work order: exactly one playing browser session. With two the line stays empty at every reading, whatever the order; when one of
        // them pauses, the other speaks, and keeps the line while the order of the list flips.
        var np = new NowPlaying();
        var tabs = new[] { Silent("t1", "youtube.com") };
        var one = Browser(PlaybackState.Playing, "browser-1", "First title");
        var two = Browser(PlaybackState.Playing, "browser-2", "Second title");
        for (var i = 0; i < 20; i++) Assert.Null(Feed(np, i * 0.5, i % 2 == 0 ? [one, two] : [two, one], tabs));

        var paused = two with { State = PlaybackState.Paused };
        for (var i = 0; i < 20; i++)
        {
            var view = Feed(np, 10 + i * 0.5, i % 2 == 0 ? [one, paused] : [paused, one], tabs);
            Assert.NotNull(view);
            Assert.Equal("First title", view.Title);
            Assert.Equal("t1", view.FallbackTabKey);
            Assert.True(view.CanControl);
        }
    }

    [Fact]
    public void Holds_N7_A_Site_Nobody_Picked_Never_Takes_The_Line_In_Any_Combination()
    {
        string[] hosts = ["youtube.com", "twitch.tv", "example.org", "music.youtube.com", "youtube.com.example.org", ""];
        PlaybackState?[] reports = [null, PlaybackState.Playing, PlaybackState.Paused, PlaybackState.Stopped];
        var oneTab = from host in hosts from audible in new[] { true, false } from report in reports select (host, audible, report);
        var tabChoices = oneTab.ToList();

        var sessionPictures = new (string Name, MediaSessionInfo[] Sessions)[]
        {
            ("none", []),
            ("one browser playing", [Browser(PlaybackState.Playing)]),
            ("one browser paused", [Browser(PlaybackState.Paused)]),
            ("two browsers playing", [Browser(PlaybackState.Playing, "b1", "One"), Browser(PlaybackState.Playing, "b2", "Two")]),
            ("a desktop player playing", [new("app-1", "alpha.exe", false, "Alpha title", null, PlaybackState.Playing, 1, 10)]),
        };

        var checkedPictures = 0;
        foreach (var first in tabChoices)
        {
            foreach (var second in tabChoices)
            {
                var tabs = new List<TabInfo>
                {
                    Make("t1", first, 3),
                    Make("t2", second, 4),
                };
                foreach (var (name, sessions) in sessionPictures)
                {
                    foreach (var connected in new[] { true, false })
                    {
                        var view = Feed(new NowPlaying(), 0, sessions, tabs, connected);
                        checkedPictures++;
                        if (view is null) continue;
                        AssertAllowed(view, tabs, connected, $"{name}, connected {connected}, t1 {first}, t2 {second}");
                    }
                }
            }
        }

        Assert.True(checkedPictures > 20000);

        static TabInfo Make(string key, (string host, bool audible, PlaybackState? report) t, long order) =>
            new(key, 1, 1, "page", t.host, t.audible, false, order, t.report is { } r ? new TabMedia("Tab title", "Tab artist", r, 5, 100) : null);
    }

    /// <summary>What may be on the line: a picked tab's own report, a desktop player, the whole browser only without the add-on, or Windows' words for a picked, audible tab.</summary>
    private static void AssertAllowed(NowPlayingView view, IReadOnlyList<TabInfo> tabs, bool connected, string picture)
    {
        bool Picked(string host) => Policy.PickedHosts.Any(h => SiteMatch.Matches(h, host));

        if (view.FallbackTabKey is { } key)
        {
            var tab = tabs.Single(t => t.Key == key);
            Assert.True(connected && Picked(tab.Host) && tab.Audible, "the fallback speaks for a picked, audible tab with the add-on connected: " + picture);
            Assert.True(view.IsBrowserSession && view.Target.Kind == MediaTargetKind.Session, picture);
            Assert.Equal(MediaNames.Site(tab.Host), view.Where);
            return;
        }

        if (view.Target.Kind == MediaTargetKind.Tab)
        {
            // (With no add-on connected the tab list is empty in the app; a picture with tabs and no connection is not one the app makes.)
            var tab = tabs.Single(t => t.Key == view.Target.Id);
            Assert.True(Picked(tab.Host), "a tab of a site nobody picked took the line: " + picture);
            return;
        }

        if (view.IsBrowserSession) Assert.False(connected, "the whole browser took the line although the add-on is connected: " + picture);
    }

    [Fact]
    public void Holds_The_Tabs_Report_Arriving_And_Leaving_Every_Step_Keeps_One_Item_And_Never_Blinks()
    {
        var np = new NowPlaying();
        var rng = new Random(9);
        for (var i = 0; i < 400; i++)
        {
            var reported = rng.Next(2) == 0;
            var tab = reported ? Reporting("t1", "youtube.com", PlaybackState.Playing, audible: true, order: 3) : Silent("t1", "youtube.com", 3);
            var view = Feed(np, i * 0.5, [Browser(PlaybackState.Playing)], [tab]);

            Assert.NotNull(view);
            Assert.True(view.CanControl, $"step {i}: present the whole time");
            Assert.False(view.IsPaused);
            Assert.Equal(reported ? "Tab title" : "Windows title", view.Title);
            Assert.Equal(reported ? MediaTargetKind.Tab : MediaTargetKind.Session, np.PlanFor(MediaCommand.Next)!.Target.Kind);
            Assert.Equal("t1", reported ? view.Target.Id : view.FallbackTabKey);
        }
    }

    [Fact]
    public void Holds_A_Tab_Whose_Sound_Starts_And_Stops_Every_Step_Never_Empties_The_Line_Or_Starts_A_Second_Item()
    {
        var np = new NowPlaying();
        NowPlayingView? view = null;
        for (var i = 0; i < 100; i++)
        {
            view = Feed(np, i * 1.0, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com", 3, audible: i % 2 == 0)]);
            Assert.NotNull(view);
            Assert.Equal("Windows title", view.Title);
        }

        // It does empty after a real silence: the grace is four seconds.
        Assert.Null(Feed(np, 200, [Browser(PlaybackState.Playing)], [Silent("t1", "youtube.com", 3, audible: false)]));
    }

    [Fact]
    public void Holds_Windows_Session_Vanishing_For_One_Reading_Keeps_The_Item()
    {
        var np = new NowPlaying();
        var tabs = new[] { Silent("t1", "youtube.com") };
        Assert.Equal("t1", Feed(np, 0, [Browser(PlaybackState.Playing)], tabs)!.FallbackTabKey);

        var gone = Feed(np, 1, [], tabs);
        Assert.Equal("t1", gone!.FallbackTabKey); // held for the grace period
        Assert.False(gone.CanControl);
        Assert.Null(np.PlanFor(MediaCommand.PlayPause)); // no command goes anywhere while the source is gone

        var back = Feed(np, 2, [Browser(PlaybackState.Playing, "browser-new-id")], tabs);
        Assert.True(back!.CanControl);
        Assert.Equal(new MediaTarget(MediaTargetKind.Session, "browser-new-id"), np.PlanFor(MediaCommand.Next)!.Target); // the new session id, not the old one
    }

    [Fact]
    public void Holds_Odd_Data_Never_Throws_And_Never_Invents_Progress()
    {
        double?[] numbers = [null, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, -1e300, 0, 1e300, 5, 200];
        string?[] texts = [null, "", "   ", new string('x', 1_000_000), "title\0with\u0001controls", "\ud800 lone surrogate"];

        foreach (var position in numbers)
        {
            foreach (var length in numbers)
            {
                foreach (var title in texts)
                {
                    var np = new NowPlaying();
                    var session = new MediaSessionInfo("browser-1", "chrome.exe", true, title, title, PlaybackState.Playing, position, length);
                    var view = Feed(np, 0, [session], [Silent("t1", "youtube.com")]);
                    Assert.NotNull(view);
                    Assert.False(string.IsNullOrWhiteSpace(view.Title), "a title that says nothing falls back on the place");
                    if (view.PositionSeconds is { } p) Assert.True(double.IsFinite(p) && p >= 0);
                    if (view.Progress is { } g) Assert.InRange(g, 0, 1);
                    if (view.Progress is not null) Assert.True(view.LengthSeconds is > 0);
                    var later = np.Current(At(1000));
                    Assert.True(later is null || later.PositionSeconds is null || double.IsFinite(later.PositionSeconds.Value));
                }
            }
        }
    }

    [Fact]
    public void Holds_Duplicate_Keys_In_One_Picture_Are_One_Source()
    {
        var np = new NowPlaying();
        var tabs = new[] { Silent("t1", "youtube.com", 3), Silent("t1", "youtube.com", 3), Reporting("t1", "youtube.com", PlaybackState.Playing, true, 3) };
        var view = Feed(np, 0, [Browser(PlaybackState.Playing), Browser(PlaybackState.Playing)], tabs);
        Assert.Equal("Tab title", view!.Title); // a report that plays is better than the fallback of the same key
    }

    [Fact]
    public void Holds_Tabs_Of_Several_Browsers_With_Equal_Keys_Of_Different_Profiles_Stay_Apart()
    {
        var np = new NowPlaying();
        var view = Feed(np, 0, [Browser(PlaybackState.Playing)], [Silent("p1:7", "youtube.com", 3), Silent("p2:7", "twitch.tv", 8)]);
        Assert.Equal("p2:7", view!.FallbackTabKey);
        Assert.Equal("Twitch", view.Where);
    }

    [Fact]
    public void Holds_A_Fuzzed_Run_Of_Ten_Thousand_Steps_Keeps_Every_Rule_And_Never_Throws()
    {
        var rng = new Random(20261007);
        string[] hosts = ["youtube.com", "twitch.tv", "example.org", "music.youtube.com", ""];
        var np = new NowPlaying();
        NowPlayingView? last = null;
        for (var step = 0; step < 10_000; step++)
        {
            var tabs = new List<TabInfo>();
            for (var i = 0; i < rng.Next(0, 5); i++)
            {
                var report = rng.Next(4) switch { 0 => (PlaybackState?)null, 1 => PlaybackState.Playing, 2 => PlaybackState.Paused, _ => PlaybackState.Stopped };
                tabs.Add(new TabInfo("t" + rng.Next(4), 1, i, "page", hosts[rng.Next(hosts.Length)], rng.Next(2) == 0, false, rng.Next(1, 30),
                    report is { } r ? new TabMedia(rng.Next(5) == 0 ? null : "Tab title", null, r, rng.Next(3) == 0 ? double.NaN : rng.Next(-5, 120), rng.Next(3) == 0 ? null : 100) : null));
            }

            var sessions = new List<MediaSessionInfo>();
            for (var i = 0; i < rng.Next(0, 4); i++)
            {
                var browser = rng.Next(3) != 0;
                sessions.Add(new MediaSessionInfo(browser ? "b" + rng.Next(3) : "app" + rng.Next(3), browser ? "chrome.exe" : "alpha.exe", browser,
                    rng.Next(6) == 0 ? null : "Windows title " + rng.Next(3), null, (PlaybackState)rng.Next(3), rng.Next(-5, 150), rng.Next(5) == 0 ? 0 : 100));
            }

            var connected = rng.Next(6) != 0;
            var view = Feed(np, step * 0.4, sessions, tabs, connected);
            last = view;
            if (view is null) continue;

            // The rules that need no picture of the past: what the line may name, and what the fallback looks like.
            if (view.FallbackTabKey is { } key)
            {
                Assert.True(view.IsBrowserSession && view.Target.Kind == MediaTargetKind.Session);
                Assert.True(view.Host is not null && Policy.PickedHosts.Any(h => SiteMatch.Matches(h, view.Host)), "the fallback names a picked site");
            }

            if (view.Target.Kind == MediaTargetKind.Tab) Assert.True(view.Host is not null && Policy.PickedHosts.Any(h => SiteMatch.Matches(h, view.Host)), "a tab on the line is on a picked site");
            if (view.Progress is { } g) Assert.InRange(g, 0, 1);
            Assert.NotNull(PlayingTile.SecondLine(view));
        }

        Assert.True(last is null || last.Title is not null);
    }
}
