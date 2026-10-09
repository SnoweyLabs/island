using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 3: the hole the graphics-card light's glow window is cut with (<c>GlassWindow.CutOutRoundedRectangle</c>, called from <c>MovingLight.PlaceShapes</c>) against the outline the old bloom is cut with
/// (<c>IslandView.Apply</c>: a hole in the shape of the capsule's OUTER outline). The formula is replayed here with the same GDI calls on regions that belong to no window (a region is a plain GDI object:
/// no window is made, shown or touched); the pixels are read with PtInRegion, as the self-test does. Nothing is rendered, nothing is captured.
/// </summary>
public class Round3CutOutTests(ITestOutputHelper output)
{
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }
    [DllImport("gdi32.dll")] private static extern nint CreatePolygonRgn([In] Pt[] points, int count, int fillMode);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    private const int RgnDiff = 4;

    /// <summary>The glow window's region as MovingLight.PlaceShapes asks for it, for a capsule at (left, top) of the given size in dips at a scale: the same arithmetic as the source (checked by the source test below).</summary>
    private static nint GlowRegion(double left, double top, double width, double height, double radius, float scale, int windowPxW, int windowPxH, double? radiusOverride = null, double radiusX = double.NaN, double radiusY = double.NaN)
    {
        // Repaired in WORK-ORDER-13 (Dan's P30 and P31): the hole is a polygon on the true outline (Island.Core HoleOutline), and its corners are ellipses of the stretched radii; the production code does the same.
        var o = RimOutline.Of(left, top, width, height, radius, radiusX, radiusY);
        var grow = (float)(LookConstants.RimInset * scale); // the hole is the outer outline: the rim's outline grown by the rim's inset (design-3-1 repaired)
        var offsetX = (float)o.X * scale - grow;
        var offsetY = (float)o.Y * scale - grow;
        var sizeX = (float)o.Width * scale + 2 * grow;
        var sizeY = (float)o.Height * scale + 2 * grow;
        var rX = (float)(radiusOverride ?? o.RadiusX) * scale + grow;
        var rY = (float)(radiusOverride ?? o.RadiusY) * scale + grow;
        var whole = CreateRectRgn(0, 0, windowPxW, windowPxH);
        var polygon = HoleOutline.Polygon(offsetX, offsetY, offsetX + sizeX, offsetY + sizeY, rX, rY); // the edges are not rounded (WORK-ORDER-13)
        var hole = CreatePolygonRgn(polygon.Select(p => new Pt { X = p.X, Y = p.Y }).ToArray(), polygon.Count, 1);
        CombineRgn(whole, whole, hole, RgnDiff);
        DeleteObject(hole);
        return whole;
    }

    /// <summary>Whether the centre of a pixel lies inside a rectangle (device pixels) with elliptical corners.</summary>
    private static bool Inside(int px, int py, double x0, double y0, double x1, double y1, double rx, double ry)
    {
        double x = px + 0.5, y = py + 0.5;
        if (x < x0 || x > x1 || y < y0 || y > y1) return false;
        double cx = x < x0 + rx ? x0 + rx : x > x1 - rx ? x1 - rx : x, cy = y < y0 + ry ? y0 + ry : y > y1 - ry ? y1 - ry : y;
        if (rx <= 0 || ry <= 0) return true;
        var dx = (x - cx) / rx;
        var dy = (y - cy) / ry;
        return dx * dx + dy * dy <= 1;
    }

    // ---- the source is what this test replays ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Replayed_Arithmetic_Is_The_Sources_Arithmetic()
    {
        var glass = Src.Read("Island.Glass/GlassWindow.cs");
        var light = Src.Read("Island.Glass/MovingLight.cs");
        Assert.Contains("HoleOutline.Polygon(x, y, x + width, y + height, radiusX, radiusY)", glass); // WORK-ORDER-13: a polygon on the true outline, no longer GDI's round-rectangle region
        Assert.Contains("CombineRgn(whole, whole, hole, RGN_DIFF)", glass);
        Assert.Contains("GlassWindow.CutOutRoundedRectangle(_glowWindow, left, top, right - left, bottom - top, radius.X + grow, radius.Y + grow);", light);
        Assert.Contains("double left = offset.X - grow, top = offset.Y - grow, right = offset.X + size.X + grow, bottom = offset.Y + size.Y + grow;", light);
        Assert.Contains("var grow = (float)(LookConstants.RimInset * _scale);", light);
        Assert.Contains("var offset = new Vector2((float)o.X * _scale, (float)o.Y * _scale);", light);
        Assert.Contains("var size = new Vector2((float)o.Width * _scale, (float)o.Height * _scale);", light);
        Assert.Contains("var radius = new Vector2((float)o.RadiusX * _scale, (float)o.RadiusY * _scale);", light);
        // the hole is the RIM's outline (inset by RimInset), the old bloom's hole is the OUTER outline
        var view = Src.Read("Island.App/Visuals/IslandView.cs");
        Assert.Contains("var hole = frame.Outline();", view);
        Assert.Equal(0.5, LookConstants.RimInset);
    }

    /// <summary>A region's right and bottom edge are not in it (the source says so too): the +1 adds a pixel on the right and at the bottom, where the continuous outline ends at x + w.</summary>
    [Fact]
    public void A_Regions_Right_And_Bottom_Edge_Are_Not_In_It()
    {
        var r = CreateRectRgn(10, 10, 20, 20);
        try
        {
            Assert.True(PtInRegion(r, 10, 10));
            Assert.True(PtInRegion(r, 19, 19));
            Assert.False(PtInRegion(r, 20, 15));
            Assert.False(PtInRegion(r, 15, 20));
        }
        finally { DeleteObject(r); }
    }

    // ---- how far the cut is from the outline, edge by edge -----------------------------------------------------------------------------------------------------

    private record Edges(double Left, double Top, double Right, double Bottom);

    /// <summary>The hole's four straight edges against the outer outline's, in device pixels: positive Left/Top = the hole starts that far INSIDE the outline (glow leaks into the capsule); NEGATIVE Right/Bottom = the hole ends that far inside it (the same leak); positive Right/Bottom = the hole ends that far OUTSIDE it (glow lost).</summary>
    private static Edges EdgeDeltas(double left, double top, double width, double height, float scale)
    {
        var o = RimOutline.Of(left, top, width, height, LookConstants.CapsuleCornerRadius);
        var grow = (float)(LookConstants.RimInset * scale);
        double ox = (float)o.X * scale - grow, oy = (float)o.Y * scale - grow, ex = (float)o.X * scale + (float)o.Width * scale + grow, ey = (float)o.Y * scale + (float)o.Height * scale + grow;
        // WORK-ORDER-13: the hole holds the pixels whose centre is inside the outline: its first column is ceil(edge - 0.5) and its end is floor(edge + 0.5), so every edge is within half a pixel
        double x = Math.Ceiling(ox - 0.5), y = Math.Ceiling(oy - 0.5), xe = Math.Floor(ex + 0.5), ye = Math.Floor(ey + 0.5);
        return new Edges(x - left * scale, y - top * scale, xe - (left + width) * scale, ye - (top + height) * scale);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2.0f)]
    public void Record_How_Far_The_Holes_Edges_Are_From_The_Outline_At_Rest(float scale)
    {
        var windowWidth = WindowMetrics.Width;
        var top = LookConstants.TopGap;
        var lefts = new List<double>(); var rights = new List<double>(); var tops = new List<double>(); var bottoms = new List<double>();
        for (var width = 200; width <= 640; width++)
        {
            var left = (windowWidth - width) / 2;
            var e = EdgeDeltas(left, top, width, LookConstants.CapsuleHeight, scale);
            lefts.Add(e.Left); rights.Add(e.Right); tops.Add(e.Top); bottoms.Add(e.Bottom);
        }

        string Sum(string name, List<double> v) => $"{name}: min {v.Min():0.00} max {v.Max():0.00} mean {v.Average():0.00}; |delta| >= 1 px in {v.Count(d => Math.Abs(d) >= 1) * 100 / v.Count}% of 441 widths";
        output.WriteLine($"scale {scale:0.00}, window {windowWidth} dip wide, top {top}");
        output.WriteLine("  " + Sum("left  (+ = leaks inside)", lefts));
        output.WriteLine("  " + Sum("top   (+ = leaks inside)", tops));
        output.WriteLine("  " + Sum("right (+ = glow lost outside)", rights));
        output.WriteLine("  " + Sum("bottom(+ = glow lost outside)", bottoms));
    }

    /// <summary>
    /// FINDING design-3-1. Expected: the hole lies on the capsule's outline to within the half pixel an integer region can be off by, on every edge and at every scale (the old bloom is cut exactly at the outer outline).
    /// It is the RIM's outline, 0.5 dip inside the outer one, rounded to whole pixels: at 200% the hole is a whole pixel inside the outline on all four sides (the glow, at its strongest, shows in a pixel-wide band inside the
    /// capsule's edge all round); at the other scales 0.25 to 1.4 px inside on one side and about even on another (rounding .5 to even makes it depend on the parity of the capsule's left).
    /// </summary>
    [Fact]
    public void Defect_The_Hole_Lies_On_The_Capsules_Outline_Within_Half_A_Pixel_At_Every_Scale()
    {
        var worst = 0.0;
        string? where = null;
        foreach (var scale in new[] { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f })
            for (var width = 200; width <= 640; width++)
            {
                var e = EdgeDeltas((WindowMetrics.Width - width) / 2, LookConstants.TopGap, width, LookConstants.CapsuleHeight, scale);
                foreach (var d in new[] { e.Left, e.Top, e.Right, e.Bottom })
                    if (Math.Abs(d) > worst) { worst = Math.Abs(d); where = $"scale {scale}, capsule {width} dip wide: {e}"; }
            }

        Assert.True(worst <= 0.5 + 1e-9, $"the hole's edge is {worst:0.00} px from the capsule's outline ({where})");
    }

    private static double GlowAt(double depthDip)
    {
        double sigma = LookConstants.BloomBlurCss, centre = LookConstants.RimInset;
        double Blurred(double depth, double width)
        {
            var sum = 0.0;
            for (var u = centre - width / 2; u <= centre + width / 2; u += 0.05)
            {
                var z = (depth - u) / sigma;
                sum += Math.Exp(-0.5 * z * z) / (sigma * Math.Sqrt(2 * Math.PI)) * 0.05;
            }
            return sum;
        }

        return LookConstants.BloomLayerAlpha * (Blurred(depthDip, LookConstants.BloomArcWidth) + LookConstants.BloomBaseAlpha * Blurred(depthDip, LookConstants.BloomBaseWidth));
    }

    /// <summary>What one pixel of cut error is worth: the glow's strength (share of the page colour, before the glass) at the outline and a pixel to either side, under the arc.</summary>
    [Fact]
    public void Record_The_Glows_Strength_Next_To_The_Cut_And_What_A_Pixels_Error_Moves()
    {
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            var px = 1 / scale;
            output.WriteLine($"scale {scale:0.0}: glow one pixel outside the outline {GlowAt(-px):0.000}, at the outline {GlowAt(0):0.000}, one pixel inside {GlowAt(px):0.000} (page colour share, before the glass)");
        }

        // the cut moves the visible band next to the edge by a pixel: the glow there is about a third of the page colour, which is the same order as the capsule's own inner glow (CategoryGlowAlpha)
        Assert.InRange(GlowAt(0), 0.25, 0.5);
    }

    // ---- the corners, pixel by pixel ----------------------------------------------------------------------------------------------------------------------------------

    private static (int Leaked, int Lost, int Total) Mismatch(double left, double top, double width, double height, double radiusX, double radiusY, float scale, double? regionRadius = null, double? regionW = null, double? regionH = null)
    {
        var wpx = (int)Math.Ceiling((WindowMetrics.Width) * scale);
        var hpx = (int)Math.Ceiling((top + height + 40) * scale);
        var region = GlowRegion(left, top, regionW ?? width, regionH ?? height, LookConstants.CapsuleCornerRadius, scale, wpx, hpx, regionRadius, radiusX, radiusY);
        try
        {
            int leaked = 0, lost = 0;
            double x0 = left * scale, y0 = top * scale, x1 = (left + width) * scale, y1 = (top + height) * scale, rx = radiusX * scale, ry = radiusY * scale;
            for (var y = 0; y < hpx; y++)
                for (var x = 0; x < wpx; x++)
                {
                    var inOutline = Inside(x, y, x0, y0, x1, y1, rx, ry);
                    var shown = PtInRegion(region, x, y);
                    if (inOutline && shown) leaked++;      // glow shown where the capsule is
                    if (!inOutline && !shown) lost++;      // glow cut where the capsule is not
                }
            return (leaked, lost, 0);
        }
        finally { DeleteObject(region); }
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Record_Pixels_Of_The_Cut_That_Are_Not_On_The_Outline_For_A_Capsule_At_Rest(float scale)
    {
        foreach (var width in new[] { 306.0, 307.0, 466.0, 467.0 })
        {
            var left = (WindowMetrics.Width - width) / 2;
            var (leaked, lost, _) = Mismatch(left, LookConstants.TopGap, width, LookConstants.CapsuleHeight, LookConstants.CapsuleCornerRadius, LookConstants.CapsuleCornerRadius, scale);
            var perimeter = 2 * (width + LookConstants.CapsuleHeight) * scale;
            output.WriteLine($"scale {scale:0.00} capsule {width} wide at left {left}: {leaked} px of the capsule's inside show glow, {lost} px just outside are cut; the outline is about {perimeter:0} px long");
        }
    }

    /// <summary>
    /// During the fly-in the ball is stretched about its centre (StretchY up to 1.34, StretchX = 1/sqrt). The old hole is the stretched outline: a rectangle with ELLIPTICAL corners (the group's ScaleTransform). The new one is the
    /// rim's rounded rectangle given the stretched width and height and the UNSTRETCHED radius: circular corners, which for a ball is a stadium. Finding design-3-2: the pixels where the two differ.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Record_The_Cut_Of_A_Stretched_Ball_Against_The_Stretched_Outline(float scale)
    {
        foreach (var (w0, h0, stretchY) in new[] { (30.0, 30.0, 1.34), (30.0, 30.0, 1.15), (50.0, 36.0, 1.34), (80.0, 44.0, 1.2) })
        {
            var stretchX = 1 / Math.Sqrt(stretchY);
            double r0 = h0 / 2;
            var centreX = WindowMetrics.Width / 2;
            var centreY = LookConstants.TopGap + h0 / 2;
            double w = w0 * stretchX, h = h0 * stretchY;
            double left = centreX - w / 2, top = centreY - h / 2;
            var (leaked, lost, _) = Mismatch(left, top, w, h, r0 * stretchX, r0 * stretchY, scale);
            output.WriteLine($"scale {scale:0.0} ball {w0}x{h0} stretched ({stretchX:0.000}, {stretchY:0.00}) = {w:0.0}x{h:0.0}: {leaked} px of the capsule show glow, {lost} px outside it are cut; outline about {2 * (w + h) * scale:0} px");
        }
    }

    /// <summary>
    /// Finding design-3-2. Expected: the cut never reaches outside the capsule (at rest it does not: 0 pixels, above), so the glow is never missing next to the edge. At the largest stretch of the fly-in the hole is a stadium
    /// (circular ends) round a shape that is an ellipse (the capsule's groups carry the stretch as a ScaleTransform): at 200% 116 pixels outside the ball are cut, of an outline 264 pixels long.
    /// </summary>
    [Fact] // repaired in WORK-ORDER-13 (Dan's P30): the corners of the glow's hole are the stretched ellipse's
    public void Defect_The_Cut_Of_A_Stretched_Ball_Never_Reaches_Outside_The_Stretched_Outline()
    {
        double stretchY = 1.34, stretchX = 1 / Math.Sqrt(stretchY);
        double w = 30 * stretchX, h = 30 * stretchY, left = WindowMetrics.Width / 2 - w / 2, top = LookConstants.TopGap + 15 - h / 2;
        var (_, lost, _) = Mismatch(left, top, w, h, 15 * stretchX, 15 * stretchY, 2.0f);
        Assert.True(lost == 0, $"{lost} px outside the stretched ball are cut out of the glow (the outline is about {2 * (w + h) * 2:0} px long at 200%)");
    }

    // ---- the cut is rebuilt only when the shape moved ----------------------------------------------------------------------------------------------------------------------

    /// <summary>PlaceShapes runs when the outline moved by more than 0.01 dip; the region is made in the window's pixels at that moment. A region is NOT remade when only the scale changes (Show sets _haveOutline false, which does it).</summary>
    [Fact]
    public void The_Region_Is_Made_Again_Whenever_Show_Lays_The_Outline_Out_Again()
    {
        var light = Src.Read("Island.Glass/MovingLight.cs");
        Assert.Matches(@"_haveOutline = false;[^\n]*\n\s*_animating = false;", light);
        var set = Regex.Matches(light, "CutOutRoundedRectangle").Count;
        Assert.Equal(1, set);
    }
}
