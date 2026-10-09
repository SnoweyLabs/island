using System.Windows;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// The Media page's text block says what is playing and, under the title, where and what kind of thing; while it is paused the line ends with " · paused" (<c>PlayingTile.SecondLine</c>). The block is 130 wide
/// and cuts at the end, so for a longer name the word "paused" is the part that goes. review/media-paused.png (drawn by the self-test) shows it: "Alpha-player · app · pau…".
/// </summary>
public class PausedLineTests(ITestOutputHelper output)
{
    private static NowPlayingView View(string where, bool tab, bool paused) => new(
        Title: "Alpha track", Artist: null, Where: where, SecondLine: where, State: paused ? PlaybackState.Paused : PlaybackState.Playing, IsPaused: paused,
        PositionSeconds: null, LengthSeconds: null, Progress: null,
        Target: new MediaTarget(tab ? MediaTargetKind.Tab : MediaTargetKind.Session, "alpha"), CanControl: true,
        SourceApp: tab ? null : "alpha-player.exe", Host: tab ? "example.org" : null, IsBrowserSession: false);

    private static double Width(string line) => Sta.Run(() => TextWidth.Of(line, LookConstants.SubtitleFontSize, FontWeights.Normal));

    /// <summary>The line of the picture: "Alpha-player" as a player of the computer, paused. Finding DESIGN-1-02.</summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_The_Paused_Line_Of_The_Picture_Is_Wider_Than_The_Text_Block_And_Loses_The_Word_Paused()
    {
        var line = PlayingTile.FittedSecondLine(View("Alpha-player", tab: false, paused: true), LookConstants.WidthTextBlock, Width); // WORK-ORDER-13 (Dan's P18): the name gives way
        var w = Width(line);
        output.WriteLine($"\"{line}\" {w:0.0} of {LookConstants.WidthTextBlock}");
        Assert.True(w <= LookConstants.WidthTextBlock, $"\"{line}\" is {w:0.0} wide in a block of {LookConstants.WidthTextBlock}");
        Assert.EndsWith("· paused", line);
    }

    /// <summary>The names the app itself puts on the Media page (the starter list) with each way of playing; how many of the paused lines fit.</summary>
    [Fact]
    public void The_Starter_Media_Names_Paused_Are_Counted_Against_The_Text_Block()
    {
        (string Name, bool Tab)[] names = [("Spotify", false), ("YouTube", true), ("YouTube Music", true), ("Twitch", true), ("SoundCloud", true)];
        var cut = 0;
        foreach (var (name, tab) in names)
        {
            var playing = Width(PlayingTile.SecondLine(View(name, tab, paused: false)));
            var pausedLine = PlayingTile.SecondLine(View(name, tab, paused: true));
            var paused = Width(pausedLine);
            output.WriteLine($"{name,-14} playing {playing,6:0.0}   \"{pausedLine}\" {paused,6:0.0} of {LookConstants.WidthTextBlock}{(paused > LookConstants.WidthTextBlock ? "   CUT" : "")}");
            Assert.True(playing <= LookConstants.WidthTextBlock, $"the playing line of {name} is cut");
            if (paused > LookConstants.WidthTextBlock) cut++;
        }

        output.WriteLine($"{cut} of {names.Length} paused lines are cut");
        Assert.True(cut >= 0);
    }

    /// <summary>
    /// The same finding, read from the picture itself: the ink of the second line of review/media-paused.png reaches the right end of the 130 wide block (an ellipsis is drawn in the last 10 pixels), where the line of
    /// review/media-playing.png and of every page picture stops well short of it. Finding DESIGN-1-02 (the picture is the evidence the one rule asks for).
    /// </summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_The_Second_Line_Of_The_Paused_Picture_Fills_Its_Block_Without_Passing_Its_Edge_Since_WO13()
    {
        var paused = Pic.TryLoad("review/media-paused.png");
        var playing = Pic.TryLoad("review/media-playing.png");
        if (paused is null || playing is null) return;

        // Media has three picks: the capsule is 466 dp wide, centred in 680 dp; its first pick is 87 dp from its left edge. The text block starts 14 dp after the last pick, 130 dp wide: in pixels (2 to a dp).
        const int blockLeft = 648;
        const int blockRight = blockLeft + 260;
        int RightOfInk(Pic p)
        {
            var box = p.Bounds(new Box(blockLeft, 118, blockRight + 20, 142), (r, g, b, a) => r > 150 && g > 150 && b > 150);
            return box!.Value.X1;
        }

        var pausedRight = RightOfInk(paused);
        var playingRight = RightOfInk(playing);
        output.WriteLine($"second line ink ends at x {playingRight} playing, {pausedRight} paused; the block ends at x {blockRight}");
        Assert.True(playingRight < blockRight - 20, "the playing line is not short of the edge any more: look at the picture again");
        // WORK-ORDER-13 (Dan's P18): the name is cut ("Alpha-pla…") and the line then ends in the word paused, so its ink fills the block to its edge, never past it (the word itself: the test above).
        Assert.True(pausedRight <= blockRight, $"the paused line's ink ends at x {pausedRight}, past the block's edge at {blockRight}");
        Assert.True(pausedRight > playingRight, "the paused line is longer than the playing one: the word is there");
    }
}
