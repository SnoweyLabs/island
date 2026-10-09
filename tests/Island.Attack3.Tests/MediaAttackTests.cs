using System.Diagnostics;
using Island.Core;

namespace Island.Attack3.Tests;

/// <summary>ATTACK3 on NowPlaying: odd numbers, odd ids, many sessions, flicker. Simulated sessions only.</summary>
public class MediaAttackTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch.AddDays(20000);
    private static readonly NowPlayingPolicy Desktop = new([], DesktopPlayersCount: true, BrowserSessionCounts: false);

    private static MediaSessionInfo Session(string id, string title, PlaybackState state, double? position = null, double? length = null) =>
        new(id, id + ".exe", false, title, "Artist", state, position, length);

    private static NowPlayingView? At(NowPlaying np, double seconds, params MediaSessionInfo[] sessions) =>
        np.Update(T0.AddSeconds(seconds), sessions, [], false, Desktop);

    // ---- Defects ---------------------------------------------------------------------------------------------

    [Fact]
    public void NaN_Position_Never_Gives_A_NaN_Progress()
    {
        // NowPlayingView.Progress is documented "0 to 1; null when the length is not known".
        var view = At(new NowPlaying(), 0, Session("alpha", "Song A", PlaybackState.Playing, double.NaN, 100));

        Assert.NotNull(view);
        Assert.True(view.Progress is null || view.Progress is >= 0 and <= 1, $"progress {view.Progress}");
    }

    [Fact]
    public void Two_Sessions_With_One_Id_Do_Not_Take_The_Line_Back_At_Every_Report()
    {
        // The id "dup" is reported twice, once playing and once paused. Its remembered state flips Playing -> Paused
        // inside every report, so every report counts it as "started again" and it takes the line from Beta, which
        // started later and still plays.
        var dupPlaying = Session("dup", "Song A", PlaybackState.Playing);
        var dupPaused = Session("dup", "Song A", PlaybackState.Paused);
        var beta = Session("beta", "Song B", PlaybackState.Playing);
        var np = new NowPlaying();

        At(np, 0, dupPlaying, dupPaused);
        var afterBetaStarts = At(np, 1, dupPlaying, dupPaused, beta);
        var nothingNew = At(np, 2, dupPlaying, dupPaused, beta);

        Assert.Equal("Song B", afterBetaStarts?.Title);
        Assert.Equal("Song B", nothingNew?.Title);
    }

    [Fact]
    public void A_Background_Player_Changing_Track_Does_Not_Take_The_Line()
    {
        // Alpha plays in the background; Beta started later and holds the line. Alpha moves to its next track:
        // Windows reports "Changing" for a moment, which MediaSessionReader maps to Paused ("between tracks"), then
        // Playing again. That blink counts as a fresh start and the line jumps to Alpha at every track change.
        var np = new NowPlaying();
        At(np, 0, Session("alpha", "Song A1", PlaybackState.Playing));
        At(np, 10, Session("alpha", "Song A1", PlaybackState.Playing), Session("beta", "Song B", PlaybackState.Playing));

        At(np, 60.0, Session("alpha", "Song A1", PlaybackState.Paused), Session("beta", "Song B", PlaybackState.Playing));
        var view = At(np, 60.3, Session("alpha", "Song A2", PlaybackState.Playing), Session("beta", "Song B", PlaybackState.Playing));

        Assert.Equal("Song B", view?.Title);
    }

    [Fact]
    public void A_Session_That_Vanishes_After_A_Quiet_Spell_Still_Gets_Its_Grace()
    {
        // NowPlaying is fed "each time something is reported", and a steady player reports nothing (a moving
        // position alone does not raise Changed in MediaSessionReader). The grace is counted from the last report,
        // not from the moment the session vanished: after a minute of quiet it is already over and the line blanks
        // at once. Its remembered state is dropped as well, so when it comes back it counts as a fresh start.
        var np = new NowPlaying();
        At(np, 0, Session("alpha", "Song A", PlaybackState.Playing));

        var justGone = At(np, 60);

        Assert.NotNull(justGone);
        Assert.False(justGone.CanControl);
        Assert.Equal("Song A", justGone.Title);
    }

    [Fact]
    public void A_Background_Player_Back_From_Between_Tracks_After_A_Quiet_Spell_Does_Not_Take_The_Line()
    {
        // Same root cause, seen from the other side: Beta holds the line, Alpha plays quietly in the background,
        // vanishes between tracks for one second and returns. Its state was forgotten, so it counts as started.
        var np = new NowPlaying();
        At(np, 0, Session("alpha", "Song A1", PlaybackState.Playing));
        At(np, 1, Session("alpha", "Song A1", PlaybackState.Playing), Session("beta", "Song B", PlaybackState.Playing));

        At(np, 120, Session("beta", "Song B", PlaybackState.Playing));
        var view = At(np, 121, Session("alpha", "Song A2", PlaybackState.Playing), Session("beta", "Song B", PlaybackState.Playing));

        Assert.Equal("Song B", view?.Title);
    }

    // ---- Coverage that holds -------------------------------------------------------------------------------

    [Fact]
    public void Five_Thousand_Sessions_One_Starting_Wins_Quickly_And_A_Same_Instant_Swap_Follows_The_New_One()
    {
        var np = new NowPlaying();
        var many = Enumerable.Range(0, 5000).Select(i => Session($"s{i}", $"Song {i}", PlaybackState.Paused, i, 300)).ToList();
        var clock = Stopwatch.StartNew();

        At(np, 0, [.. many]);
        var first = At(np, 1, [.. many.Select(s => s.SessionId == "s4321" ? s with { State = PlaybackState.Playing } : s)]);
        // At one instant s4321 vanishes and s17 starts.
        var swapped = At(np, 2, [.. many.Where(s => s.SessionId != "s4321").Select(s => s.SessionId == "s17" ? s with { State = PlaybackState.Playing } : s)]);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.Equal("Song 4321", first?.Title);
        Assert.Equal("Song 17", swapped?.Title);
        Assert.True(swapped!.CanControl);
        Assert.Equal(new MediaTarget(MediaTargetKind.Session, "s17"), np.PlanFor(MediaCommand.Next)?.Target);
    }

    [Theory]
    [InlineData(-30.0, 100.0)]
    [InlineData(500.0, 100.0)]
    [InlineData(double.PositiveInfinity, 100.0)]
    [InlineData(10.0, double.NaN)]
    [InlineData(10.0, -5.0)]
    [InlineData(10.0, 0.0)]
    [InlineData(10.0, double.PositiveInfinity)]
    public void Odd_Positions_And_Lengths_Give_Progress_In_Range_Or_None(double position, double length)
    {
        var view = At(new NowPlaying(), 0, Session("alpha", "Song A", PlaybackState.Playing, position, length));

        Assert.NotNull(view);
        Assert.True(view.Progress is null || view.Progress is >= 0 and <= 1, $"progress {view.Progress}");
    }

    [Fact]
    public void Time_Going_Backwards_Never_Moves_The_Position_Backwards_Past_The_Report()
    {
        var np = new NowPlaying();
        At(np, 100, Session("alpha", "Song A", PlaybackState.Playing, 20, 200));

        var earlier = np.Current(T0.AddSeconds(50));
        var later = np.Current(T0.AddSeconds(103));

        Assert.Equal(20, earlier?.PositionSeconds);
        Assert.Equal(23, later!.PositionSeconds!.Value, 6);
    }

    [Fact]
    public void Many_Threads_Updating_At_Once_Never_Throw()
    {
        var np = new NowPlaying();
        Parallel.For(0, 8, worker =>
        {
            var rng = new Random(worker);
            for (var i = 0; i < 2000; i++)
            {
                var sessions = Enumerable.Range(0, rng.Next(0, 6))
                    .Select(n => Session($"s{rng.Next(4)}", $"Song {n}", (PlaybackState)rng.Next(3), rng.NextDouble() * 100, rng.Next(2) == 0 ? null : 100))
                    .ToArray();
                At(np, rng.NextDouble() * 60, sessions);
                np.Current(T0.AddSeconds(rng.NextDouble() * 60));
                np.PlanFor(MediaCommand.Next);
            }
        });
    }
}
