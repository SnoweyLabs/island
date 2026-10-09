namespace Island.Core;

/// <summary>
/// The notice "Your agent is done" (WORK-ORDER-7 section 4, choice 10C, picture class <c>note big</c>): a pill 50 high with corner radius 25; on the
/// left a disc of 36 in the Vibe-coding colour with a dark check mark (#06281f); then two lines, the project's name (12.5 semi-bold) and under it, 11 at
/// 80%, what happened. Padding 7 on the left and 14 on the right.
/// </summary>
public static class NoticeLayout
{
    public const double Height = 50;
    public const double Radius = 25;
    public const double Disc = 36;
    public const double PadLeft = 7;
    public const double PadRight = 14;
    public const double Gap = 8; // the reference's 8 (it was 10 until WORK-ORDER-13: Dan's P22)
    public const double TextMinWidth = 90;
    public const double TextMaxWidth = 230;

    /// <summary>The colour of the check mark on the disc.</summary>
    public const string CheckColour = "#06281F";

    /// <summary>The line of the edge: 1.5 in the Vibe-coding colour, with a soft glow of 16 at 50%.</summary>
    public const double EdgeWidth = 1.5;

    public static double Width(double textWidth)
    {
        var text = double.IsFinite(textWidth) ? Math.Clamp(textWidth, TextMinWidth, TextMaxWidth) : TextMaxWidth;
        return PadLeft + Disc + Gap + text + PadRight;
    }

    public static double WidestWidth => Width(TextMaxWidth);
}
