using System.IO;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-3 section 5 checks: the Media page drawn from a simulated session (never a real one: nothing is
/// read from or sent to Windows' media sessions here except counting them). Proves, with made-up names only, that the
/// "Now playing" line shows the title and where it plays, that the middle button shows play when paused, that the
/// buttons act on that item and nothing else, that a click on the line asks for its window, and that when the session
/// goes away the page shows the selected pick again after the grace. Snapshots go to review/.
/// </summary>
internal sealed class MediaStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    public async Task RunAsync()
    {
        var pretend = new PretendWorld
        {
            Windows = [new OpenWindow(77, "alpha-player.exe", null, "t", 0)],
            Sessions = [Session(PlaybackState.Playing)],
        };
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var store = new PickStore(
        [
            Pick.ForProgram("Alpha Player", PageIds.Media, "alpha-player.exe", null),
            Pick.ForSite("Example Site", "example.org", PageIds.Media),
        ]);
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages);
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;
        var contents = rt.View.Contents;

        c.PageKey(PageIds.Media);
        await Waiter.UntilAsync(() => m.IsAtRest, "the Media page open and at rest", hangLimit, report);
        c.MediaChanged();
        var shown = await Waiter.UntilAsync(() => contents.TitleText == "Alpha track", "the title of the simulated session on the line", hangLimit, report);
        report.Check("the Media page's line shows the title of what is playing and where it plays", shown && contents.SubtitleText.StartsWith("Alpha", StringComparison.Ordinal),
            $"title \"{contents.TitleText}\", second line \"{contents.SubtitleText}\" (simulated session, made-up names)");
        report.Check("while it plays the middle button shows pause", contents.MiddleGlyph == "pause", contents.MiddleGlyph);
        await Task.Delay(120); // a frame or two, so the bars are drawn
        report.Check("the tile of the pick that is playing dances: its bars are drawn and no other tile has any",
            contents.TileAt(0).EqualizerHeights is { Count: 3 } && contents.TileAt(1).EqualizerHeights is null,
            "bars on the first tile only");

        // The buttons act on that item and nothing else.
        rt.Media!.Send(MediaCommand.Next);
        rt.Media.Send(MediaCommand.PlayPause);
        report.Check("next and play/pause go to the now-playing session and nowhere else", pretend.Sent.SequenceEqual(["media:s1:Next", "media:s1:PlayPause"]), string.Join(", ", pretend.Sent.Select(s => s.Split(':')[2])));

        // A click on the line asks for the player's window through the outside door.
        var brought = rt.Media.BringLineForward();
        report.Check("a click on the line asks for the player's window", brought && recording.Calls.Contains("bring:77"), recording.Calls.Count + " call(s)");

        // Paused: the middle button shows play and the line stays.
        pretend.Sessions = [Session(PlaybackState.Paused)];
        c.MediaChanged();
        var paused = await Waiter.UntilAsync(() => contents.MiddleGlyph == "play", "the middle button showing play", hangLimit, report);
        report.Check("when it is paused the middle button shows play and the line stays", paused && contents.TitleText == "Alpha track", contents.SubtitleText);
        await Task.Delay(120);
        report.Check("a paused pick's bars stand still at the lowest height", contents.TileAt(0).EqualizerHeights is { } still && still.All(h => h == Equalizer.Lowest), "three bars at 5");

        SaveSnapshots(pretend);

        // The session goes away: after the grace the page shows the selected pick as on other pages.
        pretend.Sessions = [];
        c.MediaChanged();
        var cleared = await Waiter.UntilAsync(() => contents.TitleText != "Alpha track", "the line leaving after the session went away", TimeSpan.FromSeconds(10), report);
        report.Check("when the session is gone the line shows the selected pick again after a short grace", cleared && contents.TitleText == "Alpha Player", contents.TitleText.Length > 0 ? "a pick" : "empty");
        await Task.Delay(120);
        report.Check("nothing is playing: no tile dances", contents.TileAt(0).EqualizerHeights is null, "no bars");

        // Only counts about the real sessions.
        using var reader = new Island.Sources.Media.MediaSessionReader();
        reader.Start();
        reader.WaitForFirstRead(hangLimit);
        report.Info["mediaSessions"] = reader.Sessions.Count;
        report.Info["mediaManagerAvailable"] = reader.ManagerAvailable;
    }

    private static MediaSessionInfo Session(PlaybackState state) =>
        new("s1", "alpha-player.exe", false, "Alpha track", "Beta artist", state, 30, 200);

    private void SaveSnapshots(PretendWorld pretend)
    {
        var media = Pages.Placeholder(PageIds.Media);
        var now = DateTimeOffset.UtcNow;
        foreach (var (state, file) in new[] { (PlaybackState.Playing, "media-playing.png"), (PlaybackState.Paused, "media-paused.png") })
        {
            var model = new NowPlaying();
            var policy = new NowPlayingPolicy(["example.org"], DesktopPlayersCount: true, BrowserSessionCounts: false);
            model.Update(now, [], [], false, policy);
            model.Update(now.AddSeconds(1), [Session(PlaybackState.Playing)], [], false, policy);   // something has to play before it can be paused
            var view = model.Update(now.AddSeconds(2), [Session(state)], [], false, policy);
            var scene = new OffscreenScene(new Rgb(27, 31, 58));
            scene.Render(OffscreenScene.RestFrame(media, 0.3), media, 2, nowPlaying: view).SavePng(Path.Combine(folder, file));
        }

        report.Check("the Media page was drawn from a simulated session, playing and paused", File.Exists(Path.Combine(folder, "media-playing.png")) && File.Exists(Path.Combine(folder, "media-paused.png")), "review/media-playing.png and media-paused.png");
    }
}
