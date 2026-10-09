namespace Island.Core;

/// <summary>
/// WORK-ORDER-10 §1, "the six test icons, by value": drawn in code, the same for the tests, the self-test and the picture.
/// Every pixel is either fully opaque (alpha 255) or fully transparent (0, 0, 0, 0): a pixel is inside a shape when its centre
/// (x + 0.5, y + 0.5) is. There is no antialiasing, so the pixel counts, and with them the plans, are exact and repeatable.
/// </summary>
public static class RoundIconSamples
{
    // The colours come first: static initialisers run in the order written, and the icons below read them.
    private static readonly (byte R, byte G, byte B) Blue = (47, 99, 224);
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);
    private static readonly (byte R, byte G, byte B) Orange = (245, 131, 18);
    private static readonly (byte R, byte G, byte B) NearWhite = (250, 250, 250);
    private static readonly (byte R, byte G, byte B) Red = (220, 60, 60);
    private static readonly (byte R, byte G, byte B) Yellow = (240, 190, 40);
    private static readonly (byte R, byte G, byte B) Green = (60, 170, 90);
    private static readonly (byte R, byte G, byte B) Blue4 = (70, 130, 240);
    private static readonly (byte R, byte G, byte B) SmallGreen = (30, 160, 90);

    public static IconImage PlateWithWhiteSquare { get; } = Draw(64, 64, (cx, cy) =>
    {
        if (Math.Abs(cx - 32) < 10 && Math.Abs(cy - 32) < 10) return White;
        return InRoundedSquare(cx, cy, 64, 14) ? Blue : null;
    });

    public static IconImage OrangeEllipse { get; } = Draw(64, 64, (cx, cy) => InEllipse(cx, cy, 22, 15) ? Orange : null);

    public static IconImage NearlyWhiteEllipse { get; } = Draw(64, 64, (cx, cy) => InEllipse(cx, cy, 22, 15) ? NearWhite : null);

    /// <summary>Four equal quarters: red top left, yellow top right, green bottom right, blue bottom left (clockwise from top left).</summary>
    public static IconImage FourColourRing { get; } = Draw(64, 64, (cx, cy) =>
    {
        var dx = cx - 32;
        var dy = cy - 32;
        var d2 = dx * dx + dy * dy;
        if (d2 < 12 * 12 || d2 > 26 * 26) return null;
        return dx < 0 ? (dy < 0 ? Red : Blue4) : (dy < 0 ? Yellow : Green);
    });

    public static IconImage SmallPlate { get; } = Draw(16, 16, (_, _) => SmallGreen);

    public static IconImage Nothing { get; } = Draw(32, 32, (_, _) => null);

    /// <summary>The six, in the order of the work order.</summary>
    public static IReadOnlyList<IconImage> All { get; } = [PlateWithWhiteSquare, OrangeEllipse, NearlyWhiteEllipse, FourColourRing, SmallPlate, Nothing];

    private static bool InEllipse(double cx, double cy, double radiusX, double radiusY)
    {
        var dx = (cx - 32) / radiusX;
        var dy = (cy - 32) / radiusY;
        return dx * dx + dy * dy <= 1;
    }

    private static bool InRoundedSquare(double cx, double cy, double size, double radius)
    {
        // Inside the square, and where the point is in a corner square, within the corner circle.
        var qx = Math.Max(radius - cx, cx - (size - radius));
        var qy = Math.Max(radius - cy, cy - (size - radius));
        return qx <= 0 || qy <= 0 || qx * qx + qy * qy <= radius * radius;
    }

    private static IconImage Draw(int width, int height, Func<double, double, (byte R, byte G, byte B)?> colourAt)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (colourAt(x + 0.5, y + 0.5) is not { } c) continue;
            var i = (y * width + x) * 4;
            bgra[i] = c.B;
            bgra[i + 1] = c.G;
            bgra[i + 2] = c.R;
            bgra[i + 3] = 255;
        }

        return new IconImage(width, height, bgra);
    }
}
