namespace Island.Core;

/// <summary>Decides when the pill's ring is worth drawing again: the pill may be up for hours, so not on every frame.</summary>
public static class RedrawGate
{
    /// <summary>
    /// A change is worth a redraw when the lit part's end has moved by at least this much of the outline, in device
    /// pixels. Only the end moves (the start stays at the top centre), so one pixel of outline is one pixel on screen;
    /// anything smaller cannot be seen.
    /// </summary>
    public const double MinVisibleChangePixels = 1.0;

    /// <summary>
    /// True when the ring should be drawn again. <paramref name="drawn"/> is the share last drawn (null: no ring was
    /// drawn, the moving light was), <paramref name="next"/> the new share (null: no progress now). A switch between ring
    /// and moving light always redraws. Reaching the very end (0 or 1) redraws too, so a finished ring is really empty
    /// and not one sub-pixel short. A share that is not finite counts as no progress.
    /// <paramref name="outlinePixels"/> is the outline's length in device pixels; when it is not usable, any change redraws.
    /// </summary>
    public static bool ShouldRedraw(double? drawn, double? next, double outlinePixels)
    {
        var from = Usable(drawn);
        var to = Usable(next);
        if (from is null || to is null) return (from is null) != (to is null);
        if (from == to) return false;
        if (to is 0 or 1) return true;
        if (!double.IsFinite(outlinePixels) || outlinePixels <= 0) return true;
        return Math.Abs(to.Value - from.Value) * outlinePixels >= MinVisibleChangePixels;
    }

    private static double? Usable(double? share) => share is { } s && double.IsFinite(s) ? Math.Clamp(s, 0, 1) : null;
}
