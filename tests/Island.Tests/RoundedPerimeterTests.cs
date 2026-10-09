using Island.Core;

namespace Island.Tests;

public class RoundedPerimeterTests
{
    private static double Dist(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Pill_Length_Is_Two_Straights_Plus_A_Circle()
    {
        var p = new RoundedPerimeter(0, 0, 466, 76, 38);
        Assert.Equal(2 * (466 - 76) + 2 * Math.PI * 38, p.Length, 1e-9);
    }

    [Fact]
    public void Circle_Length_Is_Pi_Times_Diameter()
    {
        var p = new RoundedPerimeter(0, 0, 30, 30, 15);
        Assert.Equal(Math.PI * 30, p.Length, 1e-9);
    }

    [Fact]
    public void Fraction_Zero_Is_Top_Centre_And_Half_Is_Bottom_Centre()
    {
        var p = new RoundedPerimeter(10, 20, 466, 76, 38);
        Assert.Equal(10 + 233, p.PointAt(0).X, 1e-9);
        Assert.Equal(20, p.PointAt(0).Y, 1e-9);
        Assert.Equal(10 + 233, p.PointAt(0.5).X, 1e-9);
        Assert.Equal(20 + 76, p.PointAt(0.5).Y, 1e-9);
        Assert.Equal(p.PointAt(0.3).X, p.PointAt(1.3).X, 1e-9);
    }

    [Fact]
    public void Travel_Is_Clockwise_Top_Centre_Goes_Right_First()
    {
        var p = new RoundedPerimeter(0, 0, 466, 76, 38);
        Assert.True(p.PointAt(0.01).X > p.PointAt(0).X);
        Assert.Equal(0, p.PointAt(0.01).Y, 1e-9);
        // Right end of the pill is reached before the bottom.
        Assert.True(p.PointAt(0.25).X > 466 - 40);
    }

    [Theory]
    [InlineData(466, 76, 38)]
    [InlineData(502, 78, 38)]
    [InlineData(250, 50, 25)]
    [InlineData(30, 30, 15)]
    [InlineData(100, 40, 5)]
    public void Constant_Fraction_Steps_Cover_Constant_Distance_Along_The_Outline(double w, double h, double r)
    {
        // Tiny fraction steps: the chord between neighbours equals the arc length, so
        // speed along the true outline is the same everywhere (no seam, no jump).
        var p = new RoundedPerimeter(0, 0, w, h, r);
        const int n = 20000;
        var step = p.Length / n;
        for (var i = 0; i < n; i++)
        {
            var d = Dist(p.PointAt((double)i / n), p.PointAt((double)(i + 1) / n));
            Assert.InRange(d, step * 0.999, step * 1.0001);
        }
    }

    [Theory]
    [InlineData(0.00, 0.28)]
    [InlineData(0.13, 0.28)]
    [InlineData(0.50, 0.12)]
    [InlineData(0.90, 0.28)]
    [InlineData(0.99, 0.12)]
    public void Walk_Pieces_Join_End_To_End_And_Cover_The_Asked_Length(double start, double length)
    {
        var p = new RoundedPerimeter(0, 0, 466, 76, 38);
        var pieces = p.Walk(start, length);

        Assert.NotEmpty(pieces);
        Assert.True(Dist(pieces[0].From, p.PointAt(start)) < 1e-6);
        Assert.True(Dist(pieces[^1].To, p.PointAt(start + length)) < 1e-6);
        for (var i = 1; i < pieces.Count; i++)
            Assert.True(Dist(pieces[i - 1].To, pieces[i].From) < 1e-6, $"gap before piece {i}");
    }

    [Fact]
    public void Walk_Of_A_Circle_Is_All_Arcs()
    {
        var p = new RoundedPerimeter(0, 0, 30, 30, 15);
        Assert.All(p.Walk(0.1, 0.28), piece => Assert.True(piece.IsArc));
    }

    [Fact]
    public void Walk_Across_The_Wrap_Point_Still_Joins()
    {
        var p = new RoundedPerimeter(0, 0, 466, 76, 38);
        var pieces = p.Walk(0.95, 0.28);
        for (var i = 1; i < pieces.Count; i++)
            Assert.True(Dist(pieces[i - 1].To, pieces[i].From) < 1e-6);
    }
}
