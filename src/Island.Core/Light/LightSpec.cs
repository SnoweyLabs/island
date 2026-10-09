namespace Island.Core;

/// <summary>A line as the old light draws it: a colour, how opaque, how thick, and whether its ends are round.</summary>
public readonly record struct StrokeSpec(Rgb Colour, double Alpha, double Width, bool RoundCaps);

/// <summary>
/// An arc of the moving light: a stroke covering <paramref name="Fraction"/> of the outline, whose far (clockwise) end is <paramref name="EndAheadOfHead"/> of the outline ahead of the
/// head the light clock gives (0 for the first arc, half a lap for the second).
/// </summary>
public readonly record struct ArcSpec(StrokeSpec Stroke, double Fraction, double EndAheadOfHead);

/// <summary>
/// Everything the moving light is made of, as numbers, for a page colour and a mode's look (WORK-ORDER-12 section 2). The numbers are the ones the old drawing gives its pens:
/// the rim (a base line and two moving arcs, at the outline inset by <see cref="LookConstants.RimInset"/>) and the glow behind it (a ring and an arc, blurred, at a share of
/// <see cref="LookConstants.BloomLayerAlpha"/>). The graphics-card light is given exactly these (LightTests pin them against <see cref="LookConstants"/> and
/// <see cref="ModeMark"/>); the old drawing is where they come from.
/// </summary>
public sealed record LightSpec(
    double Inset,
    StrokeSpec? BaseRim,
    ArcSpec? FirstArc,
    ArcSpec? SecondArc,
    StrokeSpec BloomRing,
    ArcSpec? BloomArc,
    double BloomBlurSigma,
    double GlowOpacity,
    double SpeedPerSecond,
    double RimBlurSigma = LookConstants.FrontRimBlurCss) // the old rim's own softening, 0.6 px (Dan's P23, WORK-ORDER-13: the graphics-card light carries it)
{
    /// <summary>
    /// The numbers for a page colour and a look; null where the old drawing draws something else on the edge (Do not disturb's dashed rim) or nothing moves and nothing glows, so the graphics-card
    /// light has nothing to draw.
    /// </summary>
    public static LightSpec? For(Rgb category, ModeMark.Look look)
    {
        if (look.DashedRim > 0 || look.MovingLight <= 0) return null;
        var hot = ColorMath.ArcColor(category);
        StrokeSpec? baseRim = look.PageRim > 0 ? new StrokeSpec(category, LookConstants.BaseRimAlpha * look.PageRim, LookConstants.BaseRimWidth, false) : null;
        var first = new ArcSpec(new StrokeSpec(hot, look.MovingLight, LookConstants.ArcWidth, true), LookConstants.ArcFraction, 0);
        var second = new ArcSpec(new StrokeSpec(hot, LookConstants.SecondArcAlpha * look.MovingLight, LookConstants.SecondArcWidth, true), LookConstants.SecondArcFraction, LookConstants.SecondArcPhase);
        var bloomArc = new ArcSpec(new StrokeSpec(category, 1, LookConstants.BloomArcWidth, true), LookConstants.ArcFraction, 0);
        return new LightSpec(
            LookConstants.RimInset,
            baseRim,
            first,
            second,
            new StrokeSpec(category, LookConstants.BloomBaseAlpha, LookConstants.BloomBaseWidth, false),
            bloomArc,
            LookConstants.BloomBlurCss,
            LookConstants.BloomLayerAlpha * Math.Clamp(look.Glow, 0, 1),
            LookConstants.ArcSpeedPerSecond);
    }

    /// <summary>The seconds one lap of the light takes.</summary>
    public double LapSeconds => 1 / SpeedPerSecond;
}

/// <summary>
/// The outline the light runs along for a capsule's shape, and where the compositor's own path for a rounded rectangle starts on it. The old light measures its place from the
/// middle of the top edge, clockwise (<see cref="RoundedPerimeter"/>). Microsoft Learn does not say where the compositor's rounded rectangle starts or which way it runs (read
/// 2026-10-07): the start is taken to be where Direct2D's own rounded rectangle figure starts, which the self-test measured with Direct2D itself (GeometryProbe): on the left edge, where
/// the straight part begins (x = left, y = top + radius), running upwards, that is clockwise on the screen (<see cref="CompositorPath"/>). That the compositor's own path does the same
/// is an assumption, UNVERIFIED on this laptop, and is the first thing Dan is asked to look at.
/// </summary>
public readonly record struct RimOutline(double X, double Y, double Width, double Height, double Radius, double Length, double CompositorStart, double RadiusX, double RadiusY)
{
    /// <summary>The outline the rim is drawn on for a capsule at (left, top) of the given size and corner radius, all in device-independent pixels.</summary>
    public static RimOutline Of(double left, double top, double width, double height, double radius, double radiusX = double.NaN, double radiusY = double.NaN)
    {
        var inset = LookConstants.RimInset;
        var w = Math.Max(0, width - 2 * inset);
        var h = Math.Max(0, height - 2 * inset);
        var r = Math.Max(0, Math.Min(radius - inset, Math.Min(w, h) / 2));
        var perimeter = new RoundedPerimeter(left + inset, top + inset, w, h, r);
        var length = perimeter.Length;
        // Fraction of the old path (from the middle of the top edge, clockwise) at which the compositor's path begins: the foot of the top left corner on the left edge, which is the half of the
        // straight top edge and the corner's quarter circle before the middle of the top edge, going the other way round.
        var start = length > 0 ? Wrap(1 - (w / 2 - r + Math.PI * r / 2) / length) : 0;
        // Stretched into an ellipse (the fly-in), each corner is an ellipse of its own radii; the path's length and start are worked out on the circle of the mean radius, as the lap is only an approximation then.
        var rx = double.IsFinite(radiusX) ? Math.Max(0, Math.Min(radiusX - inset, w / 2)) : r;
        var ry = double.IsFinite(radiusY) ? Math.Max(0, Math.Min(radiusY - inset, h / 2)) : r;
        return new RimOutline(left + inset, top + inset, w, h, r, length, start, rx, ry);
    }

    private static double Wrap(double f) => ArcClock.Wrap(f);
}

/// <summary>What is assumed about the compositor's rounded rectangle path (see <see cref="RimOutline"/>).</summary>
public static class CompositorPath
{
    /// <summary>The path runs clockwise on screen (Claude; UNVERIFIED: not stated on Microsoft Learn).</summary>
    public const bool IsClockwise = true;

    /// <summary>
    /// The place a trim is set to so that the compositor's trimmed arc lies on the old arc: the compositor shows the part of its path from <c>TrimStart + TrimOffset</c> to
    /// <c>TrimEnd + TrimOffset</c> (fractions of the path, wrapping), so the offset is the old arc's start minus where the compositor's path begins on the old path.
    /// </summary>
    public static double TrimOffset(double head, ArcSpec arc, RimOutline outline) => Wrap(head + arc.EndAheadOfHead - arc.Fraction - outline.CompositorStart);

    private static double Wrap(double f) => ArcClock.Wrap(f);
}
