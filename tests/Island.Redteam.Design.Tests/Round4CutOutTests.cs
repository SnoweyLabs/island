using System.Runtime.InteropServices;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 4: the glow's hole after the repair of design-3-1 (<c>MovingLight.PlaceShapes</c>: the capsule's OUTER outline, each edge rounded on its own, <c>grow = RimInset * scale</c>), replayed with real GDI regions that belong
/// to no window (a region is a plain GDI object), and read pixel by pixel with PtInRegion, against the signed distance of each pixel's centre from the capsule's outline (negative inside). Round 3 swept whole widths, centred,
/// at five scales; this sweeps fractional widths, lefts and tops, eleven scales (the Windows ones up to 300 percent), two heights, the straight edges and the corners apart. Nothing is shown, rendered or captured.
/// </summary>
public class Round4CutOutTests(ITestOutputHelper output)
{
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }
    [DllImport("gdi32.dll")] private static extern nint CreatePolygonRgn([In] Pt[] points, int count, int fillMode);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    private const int RgnDiff = 4;

    /// <summary>The glow window's region as <c>MovingLight.PlaceShapes</c> asks for it (the same arithmetic, in the same float and double steps: checked against the source by the first test).</summary>
    private static nint Region(double left, double top, double width, double height, double radius, float scale, int windowPxW, int windowPxH)
    {
        var o = RimOutline.Of(left, top, width, height, radius);
        var offset = new System.Numerics.Vector2((float)o.X * scale, (float)o.Y * scale);
        var size = new System.Numerics.Vector2((float)o.Width * scale, (float)o.Height * scale);
        var rad = new System.Numerics.Vector2((float)o.RadiusX * scale, (float)o.RadiusY * scale);
        var grow = (float)(LookConstants.RimInset * scale);
        double l = offset.X - grow, t = offset.Y - grow, r = offset.X + size.X + grow, b = offset.Y + size.Y + grow; // not rounded (WORK-ORDER-13)
        var whole = CreateRectRgn(0, 0, windowPxW, windowPxH);
        var polygon = HoleOutline.Polygon(l, t, r, b, rad.X + grow, rad.Y + grow); // WORK-ORDER-13: a polygon on the true outline
        var hole = CreatePolygonRgn(polygon.Select(p => new Pt { X = p.X, Y = p.Y }).ToArray(), polygon.Count, 1);
        CombineRgn(whole, whole, hole, RgnDiff);
        DeleteObject(hole);
        return whole;
    }

    private record Depth(int ShownStraight, double DeepestShownStraight, int CutStraight, double DeepestCutStraight, int ShownCorner, double DeepestShownCorner, int CutCorner, double DeepestCutCorner);

    /// <summary>Pixels within 4 px of the outline: shown (glow visible) although the pixel's centre is inside the outline, and cut (glow hidden) although it is outside; counted apart for the straight edges and for the corner arcs. A tie (a centre exactly on the outline) is neither.</summary>
    private static Depth Measure(double left, double top, double width, double height, float scale)
    {
        var wpx = (int)Math.Ceiling(WindowMetrics.Width * scale) + 8;
        var hpx = (int)Math.Ceiling((top + height + 40) * scale);
        var region = Region(left, top, width, height, LookConstants.CapsuleCornerRadius, scale, wpx, hpx);
        try
        {
            double x0 = left * scale, y0 = top * scale, x1 = (left + width) * scale, y1 = (top + height) * scale;
            var r = Math.Min(LookConstants.CapsuleCornerRadius * scale, Math.Min(x1 - x0, y1 - y0) / 2);
            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, hx = (x1 - x0) / 2 - r, hy = (y1 - y0) / 2 - r;
            int shownS = 0, cutS = 0, shownC = 0, cutC = 0; double deepShownS = 0, deepCutS = 0, deepShownC = 0, deepCutC = 0;
            for (var y = Math.Max(0, (int)Math.Floor(y0) - 4); y < Math.Min(hpx, (int)Math.Ceiling(y1) + 4); y++)
                for (var x = Math.Max(0, (int)Math.Floor(x0) - 4); x < Math.Min(wpx, (int)Math.Ceiling(x1) + 4); x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double qx = Math.Abs(px - cx) - hx, qy = Math.Abs(py - cy) - hy;
                    var d = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - r;
                    if (Math.Abs(d) > 4 || Math.Abs(d) < 1e-9) continue;
                    var corner = qx > 0 && qy > 0;
                    var shown = PtInRegion(region, x, y);
                    if (shown && d < 0) { if (corner) { shownC++; deepShownC = Math.Max(deepShownC, -d); } else { shownS++; deepShownS = Math.Max(deepShownS, -d); } }
                    if (!shown && d > 0) { if (corner) { cutC++; deepCutC = Math.Max(deepCutC, d); } else { cutS++; deepCutS = Math.Max(deepCutS, d); } }
                }

            return new Depth(shownS, deepShownS, cutS, deepCutS, shownC, deepShownC, cutC, deepCutC);
        }
        finally { DeleteObject(region); }
    }

    [Fact]
    public void The_Replayed_Arithmetic_Is_The_Sources_Arithmetic()
    {
        var light = Src.Read("Island.Glass/MovingLight.cs");
        Assert.Contains("var grow = (float)(LookConstants.RimInset * _scale);", light);
        Assert.Contains("double left = offset.X - grow, top = offset.Y - grow, right = offset.X + size.X + grow, bottom = offset.Y + size.Y + grow;", light);
        Assert.Contains("GlassWindow.CutOutRoundedRectangle(_glowWindow, left, top, right - left, bottom - top, radius.X + grow, radius.Y + grow);", light);
        Assert.Contains("HoleOutline.Polygon(x, y, x + width, y + height, radiusX, radiusY)", Src.Read("Island.Glass/GlassWindow.cs"));
    }

    private static readonly float[] Scales = [1.0f, 1.1f, 1.25f, 1.33f, 1.5f, 1.67f, 1.75f, 2.0f, 2.25f, 2.5f, 3.0f];

    /// <summary>
    /// The straight edges (the part of the repair design-3-1 was about): every scale, a capsule at rest from 200 to 640 dips in fractional steps, its left where the window centres it, both heights the capsule has (one row, two rows):
    /// not one pixel is on the wrong side. (A hole made of whole pixels with each edge rounded on its own agrees with a centre-of-pixel test, so a tie is the worst there is: half a pixel.)
    /// </summary>
    [Fact]
    public void The_Straight_Edges_Of_The_Hole_Are_On_The_Outline_At_Every_Scale_Width_And_Height()
    {
        int shown = 0, cut = 0, cases = 0;
        foreach (var scale in Scales)
            foreach (var width in new[] { 200.0, 233.25, 306.0, 306.5, 307.0, 399.75, 466.0, 466.25, 520.5, 640.0 })
                foreach (var height in new[] { LookConstants.CapsuleHeight, 132.0 })
                {
                    var m = Measure((WindowMetrics.Width - width) / 2, LookConstants.TopGap, width, height, scale);
                    shown += m.ShownStraight; cut += m.CutStraight; cases++;
                }

        output.WriteLine($"{cases} cases: {shown} straight-edge pixels shown inside the outline, {cut} cut outside it");
        Assert.Equal(0, shown + cut);
    }

    /// <summary>The same while the shape moves to places that are not whole pixels (a slide, a resize: PlaceShapes runs for every frame of them).</summary>
    [Fact]
    public void The_Straight_Edges_Stay_On_The_Outline_While_The_Shape_Moves_To_Fractional_Places()
    {
        int wrong = 0, cases = 0;
        for (var scale = 1.0f; scale <= 3.0f; scale += 0.25f)
            for (var top = -30.0; top <= 14.0; top += 3.7)
                for (var left = 100.0; left < 130.0; left += 7.13)
                {
                    var m = Measure(left, top, 306.37, LookConstants.CapsuleHeight, scale);
                    wrong += m.ShownStraight + m.CutStraight; cases++;
                }

        output.WriteLine($"{cases} places: {wrong} straight-edge pixels on the wrong side");
        Assert.Equal(0, wrong);
    }

    /// <summary>A ball and a capsule narrower than tall at rest (the stretch of the fly-in is the recorded proposal design-3-2): the straight edges of the hole are on the outline.</summary>
    [Fact]
    public void The_Straight_Edges_Of_A_Ball_And_A_Short_Capsule_At_Rest_Are_On_The_Outline()
    {
        var wrong = 0;
        foreach (var scale in new[] { 1.0f, 1.25f, 1.5f, 2.0f })
            foreach (var (w, h) in new[] { (30.0, 30.0), (36.0, 36.0), (50.0, 36.0), (80.0, 44.0), (76.0, 76.0), (40.0, 76.0) })
            {
                var m = Measure((WindowMetrics.Width - w) / 2, LookConstants.TopGap, w, h, scale);
                wrong += m.ShownStraight + m.CutStraight;
            }

        Assert.Equal(0, wrong);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2.0f)]
    [InlineData(2.25f)]
    [InlineData(2.5f)]
    [InlineData(3.0f)]
    public void Record_How_Deep_The_Glow_Shows_Inside_The_Outline_At_The_Corners_By_Scale(float scale)
    {
        foreach (var width in new[] { 306.0, 466.0 })
        {
            var m = Measure((WindowMetrics.Width - width) / 2, LookConstants.TopGap, width, LookConstants.CapsuleHeight, scale);
            output.WriteLine($"scale {scale:0.00}, capsule {width} wide: corner arcs: {m.ShownCorner} px shown inside the outline (deepest {m.DeepestShownCorner:0.00} px), {m.CutCorner} px cut outside (deepest {m.DeepestCutCorner:0.00}); straight edges: {m.ShownStraight} and {m.CutStraight}; outline {2 * (width + 76) * scale:0} px long");
        }
    }

    /// <summary>
    /// FINDING design-4-1 (LOW). Expected: the hole's corner arcs lie on the capsule's corner arcs to within a pixel (the order says every edge within half a pixel; the straight edges are, the arcs of a GDI round-rectangle
    /// region are not): at 125%, 225% and 250% (all three are Windows scales) 12 to 150 pixels of the arcs show glow 1.0 to 1.3 px inside the outline.
    /// </summary>
    [Fact] // repaired in WORK-ORDER-13 (Dan's P31): a polygon on the true outline instead of GDI's round-rectangle region
    public void Defect_The_Corner_Arcs_Of_The_Hole_Are_Within_A_Pixel_Of_The_Outline_At_Every_Windows_Scale()
    {
        var worst = 0.0; string? where = null;
        foreach (var scale in new[] { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.25f, 2.5f, 3.0f })
        {
            var m = Measure((WindowMetrics.Width - 306.0) / 2, LookConstants.TopGap, 306.0, LookConstants.CapsuleHeight, scale);
            foreach (var d in new[] { m.DeepestShownCorner, m.DeepestCutCorner })
                if (d > worst) { worst = d; where = $"scale {scale}: {m.ShownCorner} px shown inside, {m.CutCorner} px cut outside"; }
        }

        Assert.True(worst <= 1.0 + 1e-9, $"a corner pixel is {worst:0.00} px on the wrong side of the outline ({where})");
    }
}
