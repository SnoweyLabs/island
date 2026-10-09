namespace Island.Core;

/// <summary>CSS-style cubic-bezier timing functions, for the contents' entrance and exit.</summary>
public static class Easing
{
    /// <summary>CSS "ease": cubic-bezier(.25,.1,.25,1).</summary>
    public static double Ease(double x) => CubicBezier(0.25, 0.1, 0.25, 1, x);

    /// <summary>The curve used for opacity: cubic-bezier(.2,.8,.2,1).</summary>
    public static double Fade(double x) =>
        CubicBezier(LookConstants.FadeEaseX1, LookConstants.FadeEaseY1, LookConstants.FadeEaseX2, LookConstants.FadeEaseY2, x);

    /// <summary>The curve used for rise and scale, with a slight overshoot: cubic-bezier(.2,.9,.25,1.12).</summary>
    public static double Move(double x) =>
        CubicBezier(LookConstants.MoveEaseX1, LookConstants.MoveEaseY1, LookConstants.MoveEaseX2, LookConstants.MoveEaseY2, x);

    /// <summary>
    /// Progress for a cubic Bezier from (0,0) to (1,1) with control points (x1,y1) and (x2,y2),
    /// as CSS defines it. <paramref name="x"/> is clamped to 0..1; y may leave 0..1 (overshoot).
    /// </summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 60; i++)
        {
            t = (lo + hi) / 2;
            var bx = Bezier(x1, x2, t);
            if (Math.Abs(bx - x) < 1e-12) break;
            if (bx < x) lo = t; else hi = t;
        }

        return Bezier(y1, y2, t);
    }

    private static double Bezier(double p1, double p2, double t)
    {
        var u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }
}
