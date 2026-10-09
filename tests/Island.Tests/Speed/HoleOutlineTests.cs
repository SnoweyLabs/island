using Island.Core;

namespace Island.Tests.Speed;

/// <summary>WORK-ORDER-13 (Dan's P30 and P31): the hole in the glow's window holds exactly the pixels whose centre is inside the outline, corners that are ellipses included.</summary>
public class HoleOutlineTests
{
    /// <summary>The pixels of the polygon by the even-odd rule at each pixel's centre (a staircase of whole numbers: no edge passes through a centre).</summary>
    private static bool InPolygon(IReadOnlyList<(int X, int Y)> polygon, int px, int py)
    {
        double x = px + 0.5, y = py + 0.5;
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (double)(yj - yi) + xi) inside = !inside;
        }

        return inside;
    }

    private static bool InOutline(int px, int py, double l, double t, double r, double b, double rx, double ry)
    {
        double x = px + 0.5, y = py + 0.5;
        if (x < l || x >= r || y < t || y >= b) return false;
        var cx = Math.Clamp(x, l + rx, r - rx);
        var cy = Math.Clamp(y, t + ry, b - ry);
        if (rx <= 0 || ry <= 0) return true;
        var dx = (x - cx) / rx;
        var dy = (y - cy) / ry;
        return dx * dx + dy * dy <= 1 + 1e-9;
    }

    [Theory]
    [InlineData(10.0, 10.0, 40.0, 30.0, 0.0, 0.0)]
    [InlineData(10.3, 10.7, 40.2, 30.1, 8.0, 8.0)]
    [InlineData(0.0, 0.0, 306.0, 64.0, 32.0, 32.0)]
    [InlineData(5.5, 3.25, 60.75, 80.5, 27.2, 40.0)]
    [InlineData(5.5, 3.25, 35.5, 33.5, 19.0, 21.0)]
    public void The_Polygon_Holds_Exactly_The_Pixels_Whose_Centre_Is_Inside_The_Outline(double l, double t, double r, double b, double rx, double ry)
    {
        var polygon = HoleOutline.Polygon(l, t, r, b, rx, ry);
        Assert.NotEmpty(polygon);
        var wrong = 0;
        var width = (int)r + 3;
        var height = (int)b + 3;
        var rxHeld = Math.Min(rx, (r - l) / 2);
        var ryHeld = Math.Min(ry, (b - t) / 2);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (InPolygon(polygon, x, y) != InOutline(x, y, l, t, r, b, rxHeld, ryHeld)) wrong++;
        Assert.True(wrong <= 2, $"{wrong} pixels are on the wrong side of the outline"); // a tie on an arc can fall either way
    }

    [Fact]
    public void A_Whole_Number_Box_Without_Corners_Is_The_Plain_Rectangle()
    {
        var polygon = HoleOutline.Polygon(10, 20, 50, 60, 0, 0);
        Assert.Equal([(10, 20), (10, 60), (50, 60), (50, 20)], polygon.ToArray());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5.0)]
    [InlineData(1e9)]
    public void Odd_Radii_Never_Throw_And_Never_Leave_The_Box(double radius)
    {
        var polygon = HoleOutline.Polygon(10, 10, 60, 40, radius, radius);
        Assert.All(polygon, p => Assert.InRange(p.X, 10, 60));
        Assert.All(polygon, p => Assert.InRange(p.Y, 10, 40));
    }

    [Theory]
    [InlineData(5.0, 5.0, 5.0, 9.0)]
    [InlineData(5.0, 5.0, 9.0, 5.0)]
    [InlineData(double.NaN, 0.0, 10.0, 10.0)]
    public void An_Empty_Or_Odd_Box_Gives_No_Polygon(double l, double t, double r, double b) => Assert.Empty(HoleOutline.Polygon(l, t, r, b, 3, 3));

    [Fact]
    public void A_Big_Capsule_Needs_Few_Enough_Points_To_Be_Made_Again_On_Every_Frame_Of_A_Movement()
    {
        var polygon = HoleOutline.Polygon(100.4, 12.3, 1000.6, 160.2, 64, 64);
        Assert.InRange(polygon.Count, 4, 700);
    }
}
