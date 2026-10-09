namespace Island.Core;

/// <summary>
/// WORK-ORDER-10 §1: the main colour of an icon and what its round tile shows. Pure logic on raw pixel values (no drawing, no
/// WPF), made once per icon off the drawing thread. Pixels are straight (not premultiplied) 8-bit BGRA, row after row, stride
/// width * 4, as Windows gives them. It never throws: a picture it cannot read gets <see cref="RoundIconPlan.Letters"/>.
/// </summary>
public static class RoundIconRule
{
    private const int GroupBits = RoundIconConstants.GroupBitsPerChannel;
    private const int GroupShift = 8 - GroupBits;
    private const int GroupCount = 1 << (3 * GroupBits);

    public static RoundIconPlan Analyse(IconImage? icon) =>
        icon is null ? RoundIconPlan.Letters : Analyse(icon.Width, icon.Height, icon.Bgra);

    public static RoundIconPlan Analyse(int width, int height, ReadOnlySpan<byte> straightBgra)
    {
        if (!IsReadable(width, height, straightBgra.Length)) return RoundIconPlan.Letters;

        var scan = Scan(width, height, straightBgra);
        if (scan.Opaque == 0) return RoundIconPlan.Letters;

        var main = scan.MainColour;
        // "The square that bounds them": a square with the longer side of the opaque pixels' bounding box. A circle fills 78%
        // of it, a rounded square about 96%, an ellipse 44 by 30 only 54%, so that ellipse is a flat shape (the six icons of §1).
        long side = Math.Max(scan.MaxX - scan.MinX + 1, scan.MaxY - scan.MinY + 1);
        if ((long)scan.Opaque * 100 >= RoundIconConstants.PlatePercent * side * side)
            return new RoundIconPlan(RoundIconKind.Plate, main, DrawnWhite: false);

        if ((long)scan.WinnerCount * 100 >= (long)RoundIconConstants.FlatPercent * scan.Opaque)
        {
            return IsNearlyWhite(main)
                ? new RoundIconPlan(RoundIconKind.NearlyWhiteFlatShape, RoundIconConstants.DarkNeutral, DrawnWhite: false)
                // The white drawing must stay visible on the disc too (a pale main colour would swallow it): the disc darkens, keeping its hue, as for any other icon.
                : new RoundIconPlan(RoundIconKind.FlatShape, MakeVisible(main, width, height, Whiten(straightBgra)), DrawnWhite: true);
        }

        return new RoundIconPlan(RoundIconKind.Other, MakeVisible(main, width, height, straightBgra), DrawnWhite: false);
    }

    /// <summary>A copy of the picture with every pixel white and its own alpha kept (the flat shape's drawing).</summary>
    public static byte[] Whiten(ReadOnlySpan<byte> straightBgra)
    {
        var copy = straightBgra.ToArray();
        for (var i = 0; i + 3 < copy.Length; i += 4) copy[i] = copy[i + 1] = copy[i + 2] = 255;
        return copy;
    }

    /// <summary>
    /// Whether at least <see cref="RoundIconConstants.VisiblePercent"/> of the opaque pixels differ from the disc by
    /// <see cref="RoundIconConstants.VisibleDifference"/> or more in some channel. A picture with no opaque pixel is visible.
    /// </summary>
    public static bool StaysVisibleOn(RoundIconColour disc, int width, int height, ReadOnlySpan<byte> straightBgra)
    {
        if (!IsReadable(width, height, straightBgra.Length)) return true;
        var (visible, opaque) = CountVisible(disc, straightBgra);
        return Holds(visible, opaque);
    }

    /// <summary>
    /// The disc, made darker in steps of <see cref="RoundIconConstants.DiscStepPercent"/> toward black (lighter toward white when
    /// its brightest channel is below <see cref="RoundIconConstants.DarkDiscMaxChannel"/>), keeping its hue, until the icon stays
    /// visible on it. At most <see cref="RoundIconConstants.MaxDiscSteps"/> steps; when none holds (a rainbow logo can be
    /// unwinnable) the step on which most pixels are visible wins, the earliest on a tie. Always terminates.
    /// </summary>
    public static RoundIconColour MakeVisible(RoundIconColour start, int width, int height, ReadOnlySpan<byte> straightBgra)
    {
        if (!IsReadable(width, height, straightBgra.Length)) return start;

        var lighter = Math.Max(start.R, Math.Max(start.G, start.B)) < RoundIconConstants.DarkDiscMaxChannel;
        var best = start;
        var bestVisible = -1;
        for (var step = 0; step <= RoundIconConstants.MaxDiscSteps; step++)
        {
            var candidate = Shift(start, step * RoundIconConstants.DiscStepPercent, lighter);
            var (visible, opaque) = CountVisible(candidate, straightBgra);
            if (Holds(visible, opaque)) return candidate;
            if (visible > bestVisible)
            {
                bestVisible = visible;
                best = candidate;
            }
        }

        return best;
    }

    private static bool Holds(int visible, int opaque) =>
        (long)visible * 100 >= (long)RoundIconConstants.VisiblePercent * opaque;

    private static bool IsNearlyWhite(RoundIconColour c) =>
        c.R >= RoundIconConstants.NearlyWhiteLine && c.G >= RoundIconConstants.NearlyWhiteLine && c.B >= RoundIconConstants.NearlyWhiteLine;

    // A uniform scale toward black, or a uniform mix toward white, keeps hue (the order of the channels and the ratio of their gaps).
    private static RoundIconColour Shift(RoundIconColour c, int percent, bool lighter) =>
        new(ShiftChannel(c.R, percent, lighter), ShiftChannel(c.G, percent, lighter), ShiftChannel(c.B, percent, lighter));

    private static byte ShiftChannel(byte value, int percent, bool lighter) =>
        lighter
            ? (byte)(value + ((255 - value) * percent + 50) / 100)
            : (byte)((value * (100 - percent) + 50) / 100);

    private static (int Visible, int Opaque) CountVisible(RoundIconColour disc, ReadOnlySpan<byte> bgra)
    {
        int visible = 0, opaque = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < RoundIconConstants.OpaqueAlphaLine) continue;
            opaque++;
            var difference = Math.Max(Math.Abs(bgra[i + 2] - disc.R), Math.Max(Math.Abs(bgra[i + 1] - disc.G), Math.Abs(bgra[i] - disc.B)));
            if (difference >= RoundIconConstants.VisibleDifference) visible++;
        }

        return (visible, opaque);
    }

    // A picture is read only when its buffer is exactly width * height * 4 and it is not larger than MaxPixelsRead. The pixel count
    // is a long and is compared before it is multiplied by 4, so a claimed 2,000,000,000 x 2,000,000,000 cannot overflow.
    private static bool IsReadable(int width, int height, int length)
    {
        if (width <= 0 || height <= 0) return false;
        var pixels = (long)width * height;
        return pixels <= RoundIconConstants.MaxPixelsRead && pixels * 4 == length;
    }

    private static Scanned Scan(int width, int height, ReadOnlySpan<byte> bgra)
    {
        var counts = new int[GroupCount];
        var firstSeen = new int[GroupCount];
        var sumR = new long[GroupCount];
        var sumG = new long[GroupCount];
        var sumB = new long[GroupCount];
        int opaque = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var i = pixel * 4;
            if (bgra[i + 3] < RoundIconConstants.OpaqueAlphaLine) continue;

            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var group = ((r >> GroupShift) << (2 * GroupBits)) | ((g >> GroupShift) << GroupBits) | (b >> GroupShift);
            if (counts[group] == 0) firstSeen[group] = pixel;
            counts[group]++;
            sumR[group] += r;
            sumG[group] += g;
            sumB[group] += b;
            opaque++;

            var x = pixel % width;
            var y = pixel / width;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        // The group with the most pixels; on a tie the one met first, reading row by row. Reading the groups in index order
        // would make the winner depend on the colour values, so the tie is decided by where the group first appeared.
        var winner = -1;
        for (var group = 0; group < GroupCount; group++)
        {
            if (counts[group] == 0) continue;
            if (winner < 0 || counts[group] > counts[winner] || (counts[group] == counts[winner] && firstSeen[group] < firstSeen[winner]))
                winner = group;
        }

        if (winner < 0) return new Scanned(0, default, 0, 0, 0, 0, 0);

        var n = counts[winner];
        var main = new RoundIconColour((byte)((sumR[winner] + n / 2) / n), (byte)((sumG[winner] + n / 2) / n), (byte)((sumB[winner] + n / 2) / n));
        return new Scanned(opaque, main, n, minX, minY, maxX, maxY);
    }

    private readonly record struct Scanned(int Opaque, RoundIconColour MainColour, int WinnerCount, int MinX, int MinY, int MaxX, int MaxY);
}
