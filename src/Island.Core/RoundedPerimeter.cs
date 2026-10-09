namespace Island.Core;

public readonly record struct Pt(double X, double Y);

/// <summary>
/// One piece of an outline walk: a straight line, or a quarter-circle (or part of one)
/// turning clockwise on screen with the given radius.
/// </summary>
public readonly record struct PathPiece(bool IsArc, Pt From, Pt To, double Radius);

/// <summary>
/// The true outline of a rounded rectangle, parameterised by fraction of its length.
/// Fraction 0 is the middle of the top edge and fractions grow clockwise on screen, so
/// a highlight that advances by a fixed fraction per second moves at constant speed along
/// the real perimeter at any size, and a shape that is a plain circle works as well.
/// </summary>
public sealed class RoundedPerimeter
{
    private readonly record struct Segment(bool IsArc, double Length, Pt Start, Pt End, Pt Centre, double StartAngle, double Radius);

    private readonly Segment[] _segments;

    public RoundedPerimeter(double x, double y, double width, double height, double radius)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        Right = x + width;
        var bottom = y + height;
        var cx = x + width / 2;
        var quarter = Math.PI * r / 2;
        var d90 = Math.PI / 2;

        _segments =
        [
            Line(new Pt(cx, y), new Pt(Right - r, y)),
            Arc(new Pt(Right - r, y + r), r, -d90, quarter),
            Line(new Pt(Right, y + r), new Pt(Right, bottom - r)),
            Arc(new Pt(Right - r, bottom - r), r, 0, quarter),
            Line(new Pt(Right - r, bottom), new Pt(x + r, bottom)),
            Arc(new Pt(x + r, bottom - r), r, d90, quarter),
            Line(new Pt(x, bottom - r), new Pt(x, y + r)),
            Arc(new Pt(x + r, y + r), r, 2 * d90, quarter),
            Line(new Pt(x + r, y), new Pt(cx, y)),
        ];
        Length = _segments.Sum(s => s.Length);
    }

    private double Right { get; }

    public double Length { get; }

    private static Segment Line(Pt a, Pt b) =>
        new(false, Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)), a, b, default, 0, 0);

    private static Segment Arc(Pt centre, double r, double startAngle, double length) =>
        new(true, length, At(centre, r, startAngle), At(centre, r, startAngle + Math.PI / 2), centre, startAngle, r);

    private static Pt At(Pt c, double r, double angle) =>
        new(c.X + r * Math.Cos(angle), c.Y + r * Math.Sin(angle));

    /// <summary>The point at the given fraction of the way round; any real number, it wraps.</summary>
    public Pt PointAt(double fraction)
    {
        var d = Wrap(fraction) * Length;
        foreach (var s in _segments)
        {
            if (s.Length <= 0) continue;
            if (d <= s.Length) return Along(s, d / s.Length);
            d -= s.Length;
        }

        return _segments[0].Start;
    }

    /// <summary>
    /// The pieces that cover <paramref name="lengthFraction"/> of the outline starting at
    /// <paramref name="startFraction"/> and going clockwise. Consecutive pieces join end to end,
    /// also across the point where the fractions wrap round.
    /// </summary>
    public IReadOnlyList<PathPiece> Walk(double startFraction, double lengthFraction)
    {
        var remaining = Math.Clamp(lengthFraction, 0, 1) * Length;
        var pieces = new List<PathPiece>(6);
        var d = Wrap(startFraction) * Length;
        var i = 0;

        // Find the segment holding the start.
        while (i < _segments.Length && d >= _segments[i].Length)
        {
            d -= _segments[i].Length;
            i++;
        }

        if (i >= _segments.Length) { i = 0; d = 0; }

        for (var guard = 0; remaining > 1e-9 && guard < 3 * _segments.Length; guard++)
        {
            var s = _segments[i];
            var take = Math.Min(remaining, s.Length - d);
            if (take > 1e-9)
            {
                var from = Along(s, d / s.Length);
                var to = Along(s, (d + take) / s.Length);
                pieces.Add(new PathPiece(s.IsArc, from, to, s.Radius));
                remaining -= take;
            }

            d = 0;
            i = (i + 1) % _segments.Length;
        }

        return pieces;
    }

    private static Pt Along(Segment s, double t) =>
        s.IsArc
            ? At(s.Centre, s.Radius, s.StartAngle + t * Math.PI / 2)
            : new Pt(s.Start.X + (s.End.X - s.Start.X) * t, s.Start.Y + (s.End.Y - s.Start.Y) * t);

    private static double Wrap(double f) => ArcClock.Wrap(f);
}
