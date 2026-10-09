namespace Island.Core;

/// <summary>What the two letters of a tile are drawn in, and the soft disc behind them when the colour alone does not make them readable (Dan's P4, WORK-ORDER-13).</summary>
/// <param name="Text">White, or black on the light tiles (gold, green).</param>
/// <param name="Scrim">The colour of the disc behind the letters: the opposite of the text.</param>
/// <param name="ScrimAlpha">0 when the colour alone is enough.</param>
public readonly record struct LabelPlan(Rgb Text, Rgb Scrim, double ScrimAlpha);

/// <summary>
/// WCAG 1.4.3 asks 4.5:1 for 12 px text. The letters sit on the tile's own gradient (62% to 40% lightness, the middle is what is under them), the tile is drawn at 92% over the capsule, and an unselected
/// tile at 74% as a whole, the letters with it: that dimming pulls any text colour towards the capsule's, so the choice is made on what is left after it. White is kept wherever it reaches the ratio;
/// else black; else the better of the two with a disc of the other colour behind the letters, as light as it takes (never more than <see cref="MaxScrim"/>).
/// </summary>
public static class TileLabel
{
    /// <summary>The ratio aimed at, a little above 4.5 so that the pictures' rounding does not take it away (Claude).</summary>
    public const double Aim = 4.8;

    public const double MaxScrim = 0.6;

    // what the tile sits on: the capsule's glass over a dark wall and over a light wall (the numbers the pictures are measured on); the letters must read on both
    private static readonly Rgb DarkBackdrop = new(22, 24, 40);
    private static readonly Rgb LightBackdrop = new(109, 110, 118);

    /// <summary>The plan for a tile of this hue (degrees), open.</summary>
    public static LabelPlan ForHue(double hue)
    {
        var top = ColorMath.FromHsl(hue, LookConstants.ItemTopSaturation, LookConstants.ItemTopLightness);
        var bottom = ColorMath.FromHsl(hue, LookConstants.ItemBottomSaturation, LookConstants.ItemBottomLightness);
        return For(ColorMath.LerpSrgb(top, bottom, 0.5), LookConstants.ItemFillAlpha);
    }

    /// <summary>The plan for a tile whose face is this colour at this opacity over the capsule (a helper's disc is full).</summary>
    public static LabelPlan For(Rgb face, double faceAlpha)
    {
        var white = Try(WhiteText, BlackText, face, faceAlpha);
        if (white.Alpha == 0) return new LabelPlan(WhiteText, BlackText, 0);
        var black = Try(BlackText, WhiteText, face, faceAlpha);
        if (black.Alpha == 0) return new LabelPlan(BlackText, WhiteText, 0);
        return white.Alpha <= black.Alpha
            ? new LabelPlan(WhiteText, BlackText, white.Alpha)
            : new LabelPlan(BlackText, WhiteText, black.Alpha);
    }

    private static readonly Rgb WhiteText = Rgb.White;
    private static readonly Rgb BlackText = new(0, 0, 0);

    /// <summary>The ratio the letters have, for the tests: the plan on a tile of this face at the unselected opacity.</summary>
    public static double RatioOf(LabelPlan plan, Rgb face, double faceAlpha) => Ratio(plan.Text, plan.Scrim, plan.ScrimAlpha, face, faceAlpha);

    private static (double Alpha, double Ratio) Try(Rgb text, Rgb scrim, Rgb face, double faceAlpha)
    {
        double best = 0;
        for (var alpha = 0.0; alpha <= MaxScrim + 1e-9; alpha += 0.02)
        {
            var ratio = Ratio(text, scrim, alpha, face, faceAlpha);
            best = ratio;
            if (ratio >= Aim) return (alpha, ratio);
        }

        return (MaxScrim, best);
    }

    /// <summary>The ratio on the worse of the two backdrops.</summary>
    private static double Ratio(Rgb text, Rgb scrim, double scrimAlpha, Rgb face, double faceAlpha) =>
        Math.Min(RatioOn(text, scrim, scrimAlpha, face, faceAlpha, DarkBackdrop), RatioOn(text, scrim, scrimAlpha, face, faceAlpha, LightBackdrop));

    private static double RatioOn(Rgb text, Rgb scrim, double scrimAlpha, Rgb face, double faceAlpha, Rgb backdrop)
    {
        var opacity = LookConstants.ItemUnselectedOpacity;
        var tile = Over(face, faceAlpha, backdrop);
        var behind = Over(scrim, scrimAlpha, tile); // the disc is part of the face
        var backgroundSeen = Over(behind, opacity, backdrop);
        var textSeen = Over(text, opacity, backdrop);
        return Contrast(textSeen, backgroundSeen);
    }

    private static Rgb Over(Rgb top, double alpha, Rgb below) => ColorMath.LerpSrgb(below, top, Math.Clamp(alpha, 0, 1));

    /// <summary>WCAG relative luminance of an sRGB colour (0 to 255 channels).</summary>
    public static double Luminance(Rgb c)
    {
        static double Lin(double v)
        {
            v = Math.Clamp(v, 0, 255) / 255;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
