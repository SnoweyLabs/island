namespace Island.Core;

/// <summary>
/// WORK-ORDER-10 §1: every number of the round-icon rule, in one place. RoundIconTests pins each value. A number marked (Claude)
/// was chosen by Claude, not by Dan, and is listed for his decision; the rest are the numbers WORK-ORDER-10 §1 names.
/// </summary>
public static class RoundIconConstants
{
    /// <summary>A pixel counts only when its alpha is at least this (Claude, WORK-ORDER-10 §1).</summary>
    public const int OpaqueAlphaLine = 128;

    /// <summary>Colours are grouped by this many top bits of red, green and blue (Claude).</summary>
    public const int GroupBitsPerChannel = 4;

    /// <summary>A plate: opaque pixels cover at least this percent of the square that bounds them (Claude).</summary>
    public const int PlatePercent = 70;

    /// <summary>A flat shape: at least this percent of the opaque pixels are in the winning colour group (Claude).</summary>
    public const int FlatPercent = 90;

    /// <summary>A main colour with every channel at or above this is "nearly white" (Claude).</summary>
    public const int NearlyWhiteLine = 230;

    /// <summary>The dark neutral disc rgb(40,42,50) for a nearly white flat shape (Claude).</summary>
    public const byte DarkNeutralRed = 40;
    public const byte DarkNeutralGreen = 42;
    public const byte DarkNeutralBlue = 50;

    /// <summary>A pixel is told apart from the disc when some channel differs by at least this (Claude).</summary>
    public const int VisibleDifference = 60;

    /// <summary>At least this percent of the opaque pixels must be told apart from the disc (Claude).</summary>
    public const int VisiblePercent = 90;

    /// <summary>On a closed (grey) tile a pixel is told apart from the grey disc when it differs by at least this (Claude).</summary>
    public const int ClosedVisibleDifference = 24;

    /// <summary>The icon is drawn in a box of this fraction of the tile (Claude, from the picture).</summary>
    public const double IconBoxFraction = 0.66;

    /// <summary>Each step moves the disc this many percent of the way to black (or to white) (Claude).</summary>
    public const int DiscStepPercent = 8;

    /// <summary>At most this many steps are tried; 12 x 8 = 96 percent, so the disc never reaches pure black or white (Claude).</summary>
    public const int MaxDiscSteps = 12;

    /// <summary>A disc whose brightest channel is below this is "already dark", so it is made lighter, not darker (Claude).</summary>
    public const int DarkDiscMaxChannel = 100;

    /// <summary>
    /// A picture of more pixels than this is not read: the plan is the two letters (Claude). Windows icons are at most about 512 x 512
    /// (262,144 pixels); this allows 2048 x 2048 and keeps a made-up 10000 x 10000 claim from costing anything.
    /// </summary>
    public const int MaxPixelsRead = 2048 * 2048;

    public static RoundIconColour DarkNeutral { get; } = new(DarkNeutralRed, DarkNeutralGreen, DarkNeutralBlue);
}
