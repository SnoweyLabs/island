namespace Island.Core;

/// <summary>
/// Which screen the island comes on, and the rectangle in real pixels its window gets there. Pure: no Windows call,
/// no clock. Never throws; bad input gives the documented fallbacks named on each member.
/// </summary>
public static class ScreenChooser
{
    /// <summary>Only the first this many screens are looked at, so forty or four thousand screens cost the same.</summary>
    public const int MaxScreens = 64;

    /// <summary>A window never gets more than this many pixels on a side, so the arithmetic cannot overflow.</summary>
    private const double MaxWindowPixels = 1_000_000;

    /// <summary>
    /// The screen that holds <paramref name="pointer"/> (full rectangle, edges half-open; the first in the list when
    /// two overlap). A pointer on no screen takes the nearest one (ties: the first). An unreadable pointer (null)
    /// takes the primary screen, or the first usable one. Unusable screens (no pixels) are skipped; with none left the
    /// result is <see cref="ScreenInfo.Fallback"/> with <see cref="ScreenPlacement.IsFallback"/> set. The window is
    /// <paramref name="windowWidthDip"/> by <paramref name="windowHeightDip"/> device-independent units, turned into
    /// pixels with the chosen screen's own scaling, centred on the work area's width and placed at the work area's
    /// top, so a bar docked at the top is never covered. A window wider than the work area stays centred and
    /// overhangs both sides equally.
    /// </summary>
    public static ScreenPlacement Choose(ScreenPoint? pointer, IReadOnlyList<ScreenInfo>? screens, double windowWidthDip, double windowHeightDip)
    {
        var index = ChooseIndex(pointer, screens);
        if (index < 0) return Place(-1, ScreenInfo.Fallback, windowWidthDip, windowHeightDip, isFallback: true);
        return Place(index, screens![index], windowWidthDip, windowHeightDip, isFallback: false);
    }

    /// <summary>The index of the chosen screen in <paramref name="screens"/>, or -1 when none is usable.</summary>
    public static int ChooseIndex(ScreenPoint? pointer, IReadOnlyList<ScreenInfo>? screens)
    {
        if (screens is null) return -1;
        var count = Math.Min(screens.Count, MaxScreens);
        return pointer is { } p ? NearestIndex(p, screens, count) : PrimaryOrFirstIndex(screens, count);
    }

    private static int NearestIndex(ScreenPoint p, IReadOnlyList<ScreenInfo> screens, int count)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < count; i++)
        {
            if (!screens[i].IsUsable) continue;
            var distance = DistanceSquared(screens[i].Full, p); // 0 when the pointer is inside
            if (distance == 0) return i;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private static int PrimaryOrFirstIndex(IReadOnlyList<ScreenInfo> screens, int count)
    {
        var first = -1;
        for (var i = 0; i < count; i++)
        {
            if (!screens[i].IsUsable) continue;
            if (screens[i].IsPrimary) return i;
            if (first < 0) first = i;
        }

        return first;
    }

    /// <summary>Squared distance from a point to a rectangle; 0 when inside. In double, because int squares overflow.</summary>
    private static double DistanceSquared(PixelRect r, ScreenPoint p)
    {
        // Right and Bottom are outside the rectangle, so the last pixel inside is one before.
        double dx = p.X < r.Left ? (double)r.Left - p.X : p.X >= r.Right ? (double)p.X - (r.Right - 1) : 0;
        double dy = p.Y < r.Top ? (double)r.Top - p.Y : p.Y >= r.Bottom ? (double)p.Y - (r.Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }

    private static ScreenPlacement Place(int index, ScreenInfo screen, double widthDip, double heightDip, bool isFallback)
    {
        var work = screen.SafeWork;
        var scale = screen.SafeScale;
        var width = ToPixels(widthDip, scale);
        var height = ToPixels(heightDip, scale);
        var left = work.Left + (long)Math.Floor((work.Width - width) / 2.0);
        var window = new PixelRect(ClampToInt(left), work.Top, ClampToInt(left + width), ClampToInt((long)work.Top + height));
        return new ScreenPlacement(index, screen, window, isFallback);
    }

    /// <summary>Units to pixels, rounded, at least 1; NaN, infinity and non-positive sizes give 1.</summary>
    private static long ToPixels(double dip, double scale)
    {
        if (!double.IsFinite(dip) || dip <= 0) return 1;
        var px = Math.Round(dip * scale, MidpointRounding.AwayFromZero);
        return (long)Math.Clamp(px, 1, MaxWindowPixels);
    }

    private static int ClampToInt(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
