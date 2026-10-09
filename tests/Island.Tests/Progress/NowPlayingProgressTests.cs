using Island.Core;

namespace Island.Tests;

// EVALS N11 names the test NowPlayingTests.Unknown_Length_Shows_No_Progress. That class already exists in
// Media/NowPlayingTests.cs and this piece may not edit it, so the method lives here under the same method name;
// the main session moves it into NowPlayingTests when it fits this piece in.
public class NowPlayingProgressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unknown_Length_Shows_No_Progress()
    {
        var policy = new NowPlayingPolicy(["youtube.com"], DesktopPlayersCount: true, BrowserSessionCounts: false);

        // The view NowPlaying gives for a source that does not know its length (a live stream, a silent player).
        var np = new NowPlaying();
        var session = new MediaSessionInfo("a", "alpha.exe", false, "Live", null, PlaybackState.Playing, 12, null);
        var view = np.Update(T0, [session], [], false, policy)!;
        Assert.Null(view.Progress);
        Assert.Null(LitShare.Of(ProgressReport.FromView(view, T0), T0));
        Assert.Null(LitShare.Of(ProgressReport.FromView(np.Current(T0.AddSeconds(30))!, T0.AddSeconds(30)), T0.AddSeconds(30)));

        // A zero length (Windows' "no timeline" arrives as zero), a null one, and a tab that sends null.
        var report = new ProgressReport(5, 0, T0, IsPlaying: true);
        Assert.Null(LitShare.Of(report, T0));
        Assert.Null(LitShare.Of(report with { LengthSeconds = null }, T0));
        Assert.Null(LitShare.Of(TabMediaTiming.ToReport(null, null, true, 1, null, T0), T0.AddSeconds(10)));
    }
}
