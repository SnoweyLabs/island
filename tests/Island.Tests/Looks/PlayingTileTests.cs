using Island.Core;

namespace Island.Tests;

public class PlayingTileTests
{
    private static readonly Pick Tunes = Pick.ForProgram("Tunes", PageIds.Media, "tunes.exe", null);
    private static readonly Pick Clips = Pick.ForSite("Clips", "example.org", PageIds.Media);
    private static readonly IReadOnlyList<Pick> Picks = [Tunes, Clips];

    private static NowPlayingView Session(string app, bool paused = false) =>
        new("Track", null, "Tunes", "Tunes", paused ? PlaybackState.Paused : PlaybackState.Playing, paused, null, null, null,
            new MediaTarget(MediaTargetKind.Session, "s1"), true, app, null, false);

    private static NowPlayingView Tab(string host, bool paused = false) =>
        new("Clip", null, "Clips", "Clips", paused ? PlaybackState.Paused : PlaybackState.Playing, paused, null, null, null,
            new MediaTarget(MediaTargetKind.Tab, "t1"), true, null, host, false);

    [Fact]
    public void The_Playing_Pick_Dances()
    {
        Assert.Equal(Tunes.Id, PlayingTile.PickIdFor(Session("tunes.exe"), Picks)); // a desktop player, by its file name
        Assert.Equal(Tunes.Id, PlayingTile.PickIdFor(Session("Tunes"), Picks)); // by the file's stem
        Assert.Equal(Clips.Id, PlayingTile.PickIdFor(Tab("www.example.org"), Picks)); // a tab of a picked site

        var bars = Equalizer.Heights(0.5, playing: true);
        Assert.Equal(3, bars.Length);
        Assert.All(bars, h => Assert.InRange(h, Equalizer.Lowest, Equalizer.Highest));
        Assert.Contains(Enumerable.Range(0, 200).Select(i => Equalizer.Heights(i * 0.05, true)[0]), h => h > 16.5); // it does reach the top

        Assert.Equal("Tunes · app", PlayingTile.SecondLine(Session("tunes.exe")));
        Assert.Equal("Clips · tab", PlayingTile.SecondLine(Tab("example.org")));
    }

    [Fact]
    public void A_Paused_Pick_Stands_Still()
    {
        var paused = Session("tunes.exe", paused: true);
        Assert.Equal(Tunes.Id, PlayingTile.PickIdFor(paused, Picks)); // it is still the tile that is playing...
        foreach (var t in new[] { 0.0, 0.3, 0.45, 1.7, 100.123 })
            Assert.Equal([5.0, 5.0, 5.0], Equalizer.Heights(t, playing: false)); // ...but its bars stand still at 5
        Assert.EndsWith("· paused", PlayingTile.SecondLine(paused));
    }

    [Fact]
    public void An_Unpicked_Source_Dances_Nowhere()
    {
        Assert.Null(PlayingTile.PickIdFor(Session("other.exe"), Picks)); // a player nobody picked
        Assert.Null(PlayingTile.PickIdFor(Tab("not-picked.example"), Picks)); // a tab of a site nobody picked
        var browser = Session("chrome.exe") with { IsBrowserSession = true };
        Assert.Null(PlayingTile.PickIdFor(browser, Picks)); // the one session of the whole browser belongs to no pick
        Assert.Null(PlayingTile.PickIdFor(null, Picks)); // nothing has played
        Assert.Null(PlayingTile.PickIdFor(Session("tunes.exe"), [])); // no picks on the page
        Assert.Null(PlayingTile.PickIdFor(Session(""), Picks));
    }

    [Fact]
    public void The_Numbers_Of_The_Equalizer_Are_Pinned()
    {
        Assert.Equal(5, Equalizer.Lowest);
        Assert.Equal(17, Equalizer.Highest);
        Assert.Equal(0.9, Equalizer.Seconds);
        Assert.Equal([0.0, 0.2, 0.45], Equalizer.Delays);
        Assert.Equal(3, Equalizer.BarWidth);
        Assert.Equal(2.5, Equalizer.BarGap);
        Assert.Equal(0.45, Equalizer.DiscAlpha);
    }
}

public class EqualizerTests
{
    [Fact]
    public void Heights_Depend_Only_On_Time()
    {
        // The same moment gives the same heights, however it was reached and however many times it is asked.
        foreach (var t in new[] { 0.0, 0.123, 0.45, 0.9, 1.35, 7.77, 12345.678 })
        {
            Assert.Equal(Equalizer.Heights(t, true), Equalizer.Heights(t, true));

            // A bar that has started repeats every 0.9 s.
            var now = Equalizer.Heights(t, true);
            var later = Equalizer.Heights(t + Equalizer.Seconds, true);
            for (var i = 0; i < now.Length; i++)
                if (t - Equalizer.Delays[i] > 0) Assert.Equal(now[i], later[i], 6);
        }

        // One bar's path: low, up to 17 at half a period, low again at a full period; the second runs 0.2 s behind the first.
        Assert.Equal(5, Equalizer.Heights(0.9, true)[0], 6);
        Assert.Equal(17, Equalizer.Heights(0.45, true)[0], 6);
        Assert.Equal(17, Equalizer.Heights(0.65, true)[1], 6);
        Assert.Equal(17, Equalizer.Heights(0.9, true)[2], 6);

        // Odd times do not break it.
        Assert.Equal([5.0, 5.0, 5.0], Equalizer.Heights(double.NaN, true));
        Assert.Equal([5.0, 5.0, 5.0], Equalizer.Heights(-3, true));
        Assert.All(Equalizer.Heights(1e12, true), h => Assert.InRange(h, 5, 17));
    }
}
