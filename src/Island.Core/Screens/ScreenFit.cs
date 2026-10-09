namespace Island.Core;

/// <summary>
/// The rule for a screen narrower than the island: fewer tiles at once until the capsule fits. Decided at the summon,
/// never while anything is visible. Built on <see cref="PageFit"/> so the widths come from the one place that knows them.
/// </summary>
public static class ScreenFit
{
    /// <summary>
    /// How many tiles may be shown at once on a screen whose work area is <paramref name="workWidthDip"/> wide (device-independent
    /// units, see <see cref="ScreenInfo.WorkWidthDip"/>): the lower of <paramref name="wantedTiles"/> and what fits, counting
    /// <paramref name="reservedDip"/> units of the window that the capsule does not use (spring overshoot; the shadow may hang off
    /// the screen's edge and need not be reserved). Both a media page's capsule and any other page's must fit, as in the app today.
    /// At least 1 when something is wanted, even on a screen too narrow for one tile (the capsule is then clipped, never
    /// absent); 0 when nothing is wanted; a NaN, negative or zero width reads as the narrowest case, 1 tile.
    /// </summary>
    public static int Tiles(double workWidthDip, int wantedTiles, double reservedDip = 0)
    {
        if (wantedTiles <= 0) return 0;
        var reserved = double.IsFinite(reservedDip) && reservedDip > 0 ? reservedDip : 0;
        var available = workWidthDip - reserved; // NaN stays NaN: every comparison in PageFit is then false, giving 1
        var fits = Math.Min(PageFit.MaxTiles(available, isMedia: false), PageFit.MaxTiles(available, isMedia: true));
        return Math.Min(wantedTiles, fits);
    }

    /// <summary>The same for a chosen screen, so the app asks once with what the chooser gave it.</summary>
    public static int Tiles(ScreenInfo screen, int wantedTiles, double reservedDip = 0) =>
        Tiles(screen.WorkWidthDip, wantedTiles, reservedDip);
}
