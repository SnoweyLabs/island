namespace Island.Redteam.Design.Tests;

/// <summary>A rectangle of pixels, inclusive of both ends.</summary>
internal readonly record struct Box(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;
    public double CentreX => (X0 + X1) / 2.0;
    public double CentreY => (Y0 + Y1) / 2.0;
}

internal static class PicMeasure
{
    /// <summary>The bounding boxes of the 4-connected groups of pixels the predicate accepts, inside a region, largest first.</summary>
    public static List<Box> Components(this Pic p, Box region, Func<byte, byte, byte, byte, bool> accept)
    {
        var w = region.Width;
        var h = region.Height;
        var seen = new bool[w * h];
        var result = new List<(Box Box, int Count)>();
        var stack = new Stack<(int X, int Y)>();
        for (var y0 = 0; y0 < h; y0++)
            for (var x0 = 0; x0 < w; x0++)
            {
                if (seen[y0 * w + x0]) continue;
                var c = p.At(region.X0 + x0, region.Y0 + y0);
                if (!accept(c.R, c.G, c.B, c.A)) continue;
                var minX = x0; var maxX = x0; var minY = y0; var maxY = y0; var count = 0;
                stack.Push((x0, y0));
                seen[y0 * w + x0] = true;
                while (stack.Count > 0)
                {
                    var (x, y) = stack.Pop();
                    count++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        var nx = x + dx; var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx]) continue;
                        var n = p.At(region.X0 + nx, region.Y0 + ny);
                        if (!accept(n.R, n.G, n.B, n.A)) continue;
                        seen[ny * w + nx] = true;
                        stack.Push((nx, ny));
                    }
                }

                result.Add((new Box(region.X0 + minX, region.Y0 + minY, region.X0 + maxX, region.Y0 + maxY), count));
            }

        return [.. result.OrderByDescending(r => r.Count).Select(r => r.Box)];
    }

    /// <summary>The bounding box of the pixels the predicate accepts inside a region; null when none.</summary>
    public static Box? Bounds(this Pic p, Box region, Func<byte, byte, byte, byte, bool> accept)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = region.Y0; y <= region.Y1; y++)
            for (var x = region.X0; x <= region.X1; x++)
            {
                var c = p.At(x, y);
                if (!accept(c.R, c.G, c.B, c.A)) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

        return maxX < 0 ? null : new Box(minX, minY, maxX, maxY);
    }

    public static bool NearWhite(byte r, byte g, byte b, byte a) => r >= 225 && g >= 225 && b >= 225;
}
