namespace Island.Core;

/// <summary>Where an icon is drawn on the round tile, in device-independent pixels from the tile's top-left corner.</summary>
public readonly record struct RoundIconBox(double X, double Y, double Width, double Height);

/// <summary>
/// WORK-ORDER-10 §1, "Sharpness": the icon is drawn in a box of <see cref="RoundIconConstants.IconBoxFraction"/> of the tile, centred.
/// A picture that fits in the box on this screen's scaling is drawn at its own size, never stretched; a larger one is scaled down to
/// fit the box, keeping its aspect.
/// </summary>
public static class RoundIconLayout
{
    public static RoundIconBox Place(int iconWidth, int iconHeight, double tileSize, double pixelsPerDip)
    {
        // A scale that is zero, negative, NaN or infinite counts as 1, as in IconFit.Place.
        var scale = pixelsPerDip > 0 && double.IsFinite(pixelsPerDip) ? pixelsPerDip : 1;
        var tile = tileSize > 0 && double.IsFinite(tileSize) ? tileSize : 0;
        var box = tile * RoundIconConstants.IconBoxFraction;

        var longest = Math.Max(iconWidth, iconHeight);
        if (longest <= 0) return new RoundIconBox(tile / 2, tile / 2, 0, 0);

        // Work in device pixels so a huge or tiny scale cannot overflow: the factor is 1 (own size) or box-in-pixels over the longer side.
        var factor = Math.Min(1.0, box * scale / longest);
        var width = Math.Clamp(Math.Max(0, iconWidth) * factor / scale, 0, box);
        var height = Math.Clamp(Math.Max(0, iconHeight) * factor / scale, 0, box);
        return new RoundIconBox((tile - width) / 2, (tile - height) / 2, width, height);
    }

    /// <summary>
    /// <see cref="Place"/> with the corner moved to the nearest whole device pixel (at most half a device pixel off centre), so a picture drawn at its own size is not
    /// sampled across two pixels and blurred. The tile draws with this one.
    /// </summary>
    public static RoundIconBox PlaceOnPixels(int iconWidth, int iconHeight, double tileSize, double pixelsPerDip)
    {
        var box = Place(iconWidth, iconHeight, tileSize, pixelsPerDip);
        var scale = pixelsPerDip > 0 && double.IsFinite(pixelsPerDip) ? pixelsPerDip : 1;
        return box with { X = OnPixel(box.X, scale), Y = OnPixel(box.Y, scale) };
    }

    private static double OnPixel(double dip, double scale) 
    {
        var snapped = Math.Round(dip * scale, MidpointRounding.AwayFromZero) / scale;
        return double.IsFinite(snapped) ? snapped : dip;
    }
}
