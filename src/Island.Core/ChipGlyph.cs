namespace Island.Core;

/// <summary>
/// The colour of the glyph on the page chip (Dan's P22, design-1-13, WORK-ORDER-13). White was 2.1:1 on the lime of Terminals and under 3:1 (WCAG 1.4.11) on four of six pages; the glyph is white where white
/// reaches 3:1 on the chip and a deep dark where the chip is light. The chip is the page colour at <see cref="LookConstants.ChipAlpha"/> over the glass, dark or light.
/// </summary>
public static class ChipGlyph
{
    public static readonly Rgb Dark = new(8, 12, 20);

    // the capsule's glass over the self-test's dark wall and over its light wall (the glass base at 0.6 over (27,31,58) and over (242,243,246)): the numbers the pictures are measured on
    private static readonly Rgb DarkGlass = new(22, 24, 40);
    private static readonly Rgb LightGlass = new(109, 110, 118);

    public const double Aim = 3.0;

    public static Rgb For(Rgb page)
    {
        var worstWhite = Worst(page, Rgb.White);
        if (worstWhite >= Aim) return Rgb.White;
        var worstDark = Worst(page, Dark);
        return worstDark > worstWhite ? Dark : Rgb.White;
    }

    /// <summary>The ratio the glyph has on the chip, on whichever glass is worse for it.</summary>
    public static double Worst(Rgb page, Rgb glyph) => Math.Min(On(page, glyph, DarkGlass), On(page, glyph, LightGlass));

    private static double On(Rgb page, Rgb glyph, Rgb glass) => TileLabel.Contrast(glyph, ColorMath.LerpSrgb(glass, page, LookConstants.ChipAlpha));
}
