using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 3: which pixels a GDI round-rectangle region holds (a plain GDI object, no window), because the +1 in <c>CutOutRoundedRectangle</c> rests on it.</summary>
public class Round3ProbeTests(ITestOutputHelper output)
{
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);

    [Fact]
    public void Record_Which_Columns_And_Rows_A_Round_Rectangle_Region_Holds()
    {
        foreach (var (x1, x2, e) in new[] { (100, 200, 0), (100, 200, 20), (100, 200, 76), (101, 201, 76) })
        {
            var r = CreateRoundRectRgn(x1, 50, x2, 126, e, e);
            var rect = CreateRectRgn(x1, 50, x2, 126);
            try
            {
                var midY = 88;
                string Cols(nint h) => $"first col {Enumerable.Range(x1 - 3, 10).First(x => PtInRegion(h, x, midY))}, last col {Enumerable.Range(x2 - 6, 10).Last(x => PtInRegion(h, x, midY))}";
                string Rows(nint h) => $"first row {Enumerable.Range(47, 10).First(y => PtInRegion(h, 150, y))}, last row {Enumerable.Range(120, 10).Last(y => PtInRegion(h, 150, y))}";
                output.WriteLine($"round rect ({x1},50)-({x2},126) ellipse {e}: {Cols(r)}; {Rows(r)}   | plain rect: {Cols(rect)}; {Rows(rect)}");
            }
            finally { DeleteObject(r); DeleteObject(rect); }
        }
    }
}
