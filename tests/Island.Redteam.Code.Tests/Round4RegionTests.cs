using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 4: what a GDI polygon region covers at its right and bottom edge (no window, no screen: a region is a plain object). Probe for the +1 in GlassWindow.CutOutRoundedRectangle.</summary>
public sealed class Round4RegionTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Pt { public int X, Y; }

    [DllImport("gdi32.dll")]
    private static extern nint CreatePolygonRgn([In] Pt[] points, int count, int fillMode);

    [DllImport("gdi32.dll")]
    private static extern bool PtInRegion(nint region, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [Fact]
    public void Held_The_Hole_Polygon_Of_The_Glow_Covers_Exactly_As_Wide_And_High_As_The_Outline()
    {
        // WORK-ORDER-13: the hole is a polygon on the true outline (HoleOutline.Polygon from x to x + width) and no longer GDI's round-rectangle region with its +1. A polygon region covers the pixels
        // whose centre is inside it: x to x + width - 1 (measured here with a plain GDI region: no window, no screen).
        foreach (var (x, y, width, height, radius) in new[] { (10, 10, 30, 20, 8), (0, 0, 200, 64, 32), (3, 7, 57, 33, 16), (5, 5, 40, 40, 20) })
        {
            var polygon = Island.Core.HoleOutline.Polygon(x, y, x + width, y + height, radius, radius);
            var region = CreatePolygonRgn(polygon.Select(p => new Pt { X = p.X, Y = p.Y }).ToArray(), polygon.Count, 1);
            try
            {
                var midY = y + height / 2;
                var midX = x + width / 2;
                var xs = Enumerable.Range(x - 3, width + 8).Where(px => PtInRegion(region, px, midY)).ToList();
                var ys = Enumerable.Range(y - 3, height + 8).Where(py => PtInRegion(region, midX, py)).ToList();
                Assert.Equal((x, x + width - 1), (xs.Min(), xs.Max()));
                Assert.Equal((y, y + height - 1), (ys.Min(), ys.Max()));
            }
            finally
            {
                DeleteObject(region);
            }
        }
    }
}
