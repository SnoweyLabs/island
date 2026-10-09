namespace Island.Core;

/// <summary>
/// The shape of the hole cut in the glow's window (WORK-ORDER-13, Dan's P30 and P31): the capsule's outer outline, a rounded rectangle whose corners may be ellipses (the island is stretched into an
/// ellipse during the fly-in), as a polygon of whole pixels that holds exactly the pixels whose centre lies inside the outline. A GDI round-rectangle region draws its corner arcs up to 1.3 px inside
/// the true arc at 125%, 225% and 250% scaling and can only have circular corners; a staircase made from the outline, row by row, is exact (a tie is the worst there is). The right and the bottom
/// edge are not part of a region: a hole from <c>left</c> to <c>right</c> covers the pixels <c>left</c> to <c>right - 1</c>.
/// </summary>
public static class HoleOutline
{
    /// <summary>
    /// The polygon, clockwise from the top left. The box is in pixels, not rounded (an edge at 12.4 is where the outline is: the pixels whose centre lies inside it are the hole). The radii are in pixels
    /// and are held to half the width and half the height.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> Polygon(double left, double top, double right, double bottom, double radiusX, double radiusY)
    {
        var points = new List<(int X, int Y)>();
        if (!(double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(right) && double.IsFinite(bottom)) || right <= left || bottom <= top) return points;
        var width = right - left;
        var height = bottom - top;
        var rx = Math.Clamp(double.IsFinite(radiusX) ? radiusX : 0, 0, width / 2.0);
        var ry = Math.Clamp(double.IsFinite(radiusY) ? radiusY : 0, 0, height / 2.0);

        // The rows whose centre is inside the box, and for each the first pixel of the span whose centre is inside, and the first pixel after it.
        var first = (int)Math.Ceiling(top - 0.5);
        var last = (int)Math.Ceiling(bottom - 0.5); // exclusive
        var rows = last - first;
        if (rows <= 0) return points;
        var from = new int[rows];
        var to = new int[rows];
        for (var row = 0; row < rows; row++)
        {
            var inset = InsetAt(first + row + 0.5, top, bottom, rx, ry);
            from[row] = (int)Math.Ceiling(left + inset - 0.5);
            to[row] = (int)Math.Floor(right - inset + 0.5);
            if (to[row] < from[row]) to[row] = from[row];
        }

        void Add(int x, int y)
        {
            if (points.Count > 0 && points[^1] == (x, y)) return;
            points.Add((x, y));
        }

        // down the left side as a staircase (each step has both its corners), across the bottom, and up the right side the same way
        Add(from[0], first);
        for (var row = 1; row < rows; row++)
        {
            if (from[row] == from[row - 1]) continue;
            Add(from[row - 1], first + row);
            Add(from[row], first + row);
        }

        Add(from[rows - 1], last);
        Add(to[rows - 1], last);
        for (var row = rows - 2; row >= 0; row--)
        {
            if (to[row] == to[row + 1]) continue;
            Add(to[row + 1], first + row + 1);
            Add(to[row], first + row + 1);
        }

        Add(to[0], first);
        return points;
    }

    /// <summary>How far the outline is inside the bounding rectangle at height <paramref name="y"/> (the centre of a row), because of the elliptical corners.</summary>
    private static double InsetAt(double y, double top, double bottom, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return 0;
        double dy;
        if (y < top + ry) dy = (top + ry - y) / ry;
        else if (y > bottom - ry) dy = (y - (bottom - ry)) / ry;
        else return 0;
        return rx * (1 - Math.Sqrt(Math.Max(0, 1 - dy * dy)));
    }
}
