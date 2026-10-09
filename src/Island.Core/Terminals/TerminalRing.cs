using Island.Core.Terminals;

namespace Island.Core;

/// <summary>
/// The ring around a tile of the Terminals page that says what a helper inside it is doing (WORK-ORDER-11 section 3, variant A of the picture, fitted to the
/// room the island has: tiles stand 8 apart, so the ring's outer edge is 3.25 beyond the disc and two rings never touch). Every number is Claude's.
/// </summary>
public static class TerminalRing
{
    /// <summary>The ring's inner edge is this far beyond the disc (Claude).</summary>
    public const double InnerGap = 0.75;

    /// <summary>The ring is this thick (Claude).</summary>
    public const double Thickness = 2.5;

    /// <summary>The outer edge of the ring beyond the disc: with the tiles 8 apart, two neighbouring rings are left 1.5 apart (Claude).</summary>
    public const double OuterReach = InnerGap + Thickness;

    /// <summary>The working arc is this share of the circle (Claude).</summary>
    public const double ArcShare = 0.28;

    /// <summary>The working arc turns once in this many seconds (Claude).</summary>
    public const double TurnSeconds = 1.4;

    /// <summary>The track under the working arc: white at this strength (Claude).</summary>
    public const double TrackAlpha = 0.12;

    /// <summary>Working: blue (Claude).</summary>
    public static HelperColor Working { get; } = new(76, 141, 255);

    /// <summary>Waiting for the person: orange (Claude).</summary>
    public static HelperColor NeedsYou { get; } = new(255, 176, 32);

    /// <summary>Finished: green (Claude).</summary>
    public static HelperColor Finished { get; } = new(53, 196, 106);

    /// <summary>The colour of a state's ring; null for idle (no ring).</summary>
    public static HelperColor? ColourOf(HelperState state) => state switch
    {
        HelperState.Working => Working,
        HelperState.NeedsYou => NeedsYou,
        HelperState.Finished => Finished,
        _ => null,
    };

    /// <summary>
    /// Whether the white ring of the selected tile is drawn: not on a tile that has a state ring, which leaves no room for both between two tiles (WORK-ORDER-11 section 3;
    /// its glow and its full brightness still say which tile is selected).
    /// </summary>
    public static bool WhiteRingShown(HelperState ring, bool selected) => selected && ring == HelperState.Idle;

    /// <summary>Where the head of the working arc is, as a fraction of the circle from the top, clockwise: a function of the island's clock and nothing else.</summary>
    public static double ArcStart(double seconds)
    {
        var f = seconds / TurnSeconds;
        return f - Math.Floor(f);
    }

    /// <summary>The working arc's turn in degrees: where Windows is set to show no animations it stands still at the top (WORK-ORDER-11 section 3).</summary>
    public static double ArcAngle(double seconds, bool animationsOn) => animationsOn ? ArcStart(seconds) * 360 : 0;
}
