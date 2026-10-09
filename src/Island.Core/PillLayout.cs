namespace Island.Core;

/// <summary>
/// The small pill that shows while something plays (WORK-ORDER-7 section 2, choice 8A): 44 high, corner radius 22. From left to right the
/// source's tile at 30, the title (at most 150 wide), and four buttons of 26 (previous, play or pause, next, search); 6 between elements,
/// 7 of padding on the left, 8 on the right. The ring that is its edge is 2.4 thick.
/// </summary>
public static class PillLayout
{
    public const double Height = 44;
    public const double Radius = 22;
    public const double Tile = 30;
    public const double TitleMaxWidth = 150;
    public const double Button = 26;
    public const double Gap = 6;
    public const double PadLeft = 7;
    public const double PadRight = 8;
    public const int Buttons = 4;
    public const double RingThickness = 2.4;

    /// <summary>The dark base of the pill's glass (rgb(20,22,32) at 70%; the capsule's is 60%).</summary>
    public const double GlassAlpha = 0.70;

    /// <summary>The part of the ring that has already played: white at 16%.</summary>
    public const double PlayedAlpha = 0.16;

    /// <summary>Narrowest title the pill makes room for (an empty title still keeps its place).</summary>
    public const double TitleMinWidth = 24;

    /// <summary>The pill's width for a title of this width, cut to 24 to 150 (a width that is not a number reads as the widest).</summary>
    public static double Width(double titleWidth)
    {
        var title = double.IsFinite(titleWidth) ? Math.Clamp(titleWidth, TitleMinWidth, TitleMaxWidth) : TitleMaxWidth;
        return PadLeft + Tile + Gap + title + Gap + Buttons * Button + (Buttons - 1) * Gap + PadRight;
    }

    public static double WidestWidth => Width(TitleMaxWidth);
}
