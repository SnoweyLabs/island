using System.Windows;
using System.Windows.Media;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>Where the shape is, and what colour and rim position it has, for one drawn frame. Window coordinates, DIPs.</summary>
internal readonly record struct ShapeFrame(
    double CentreX,
    double Top,
    double Width,
    double Height,
    double Radius,
    Rgb Category,
    double ArcHead)
{
    public double Left => CentreX - Width / 2;

    public Rect Rect => new(Left, Top, Width, Height);

    public Point Centre => new(CentreX, Top + Height / 2);

    /// <summary>The outer outline of the shape.</summary>
    public RectangleGeometry Outline() => Geo.RoundedRect(Rect, Radius);

    /// <summary>The outline inside the 1 px border.</summary>
    public RectangleGeometry PaddingBox() => Geo.RoundedRect(Geo.Inset(Rect, LookConstants.BorderWidth), Radius - LookConstants.BorderWidth);
}

internal static class Paint
{
    public static Color Of(Rgb c, double alpha = 1) =>
        Color.FromArgb(
            (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(c.R, 0, 255)),
            (byte)Math.Round(Math.Clamp(c.G, 0, 255)),
            (byte)Math.Round(Math.Clamp(c.B, 0, 255)));

    public static SolidColorBrush Brush(Rgb c, double alpha = 1)
    {
        var b = new SolidColorBrush(Of(c, alpha));
        b.Freeze();
        return b;
    }

    public static Pen Pen(Rgb c, double alpha, double width, bool roundCaps = false)
    {
        var p = new Pen(Brush(c, alpha), width);
        if (roundCaps)
        {
            p.StartLineCap = PenLineCap.Round;
            p.EndLineCap = PenLineCap.Round;
        }

        p.Freeze();
        return p;
    }

    public static readonly Rgb Black = new(0, 0, 0);
}

internal static class Geo
{
    public static Rect Inset(Rect r, double d) =>
        new(r.X + d, r.Y + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));

    public static RectangleGeometry RoundedRect(Rect r, double radius)
    {
        var rad = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        return new RectangleGeometry(r, rad, rad);
    }

    /// <summary>An open path along part of a rounded rectangle's outline, as one figure.</summary>
    public static StreamGeometry Arc(RoundedPerimeter perimeter, double startFraction, double lengthFraction)
    {
        var pieces = perimeter.Walk(startFraction, lengthFraction);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            if (pieces.Count == 0) return g;
            ctx.BeginFigure(new Point(pieces[0].From.X, pieces[0].From.Y), false, false);
            foreach (var piece in pieces)
            {
                var to = new Point(piece.To.X, piece.To.Y);
                if (piece.IsArc)
                    ctx.ArcTo(to, new Size(piece.Radius, piece.Radius), 0, false, SweepDirection.Clockwise, true, false);
                else
                    ctx.LineTo(to, true, false);
            }
        }

        g.Freeze();
        return g;
    }

    /// <summary>Everything in a big rectangle except the given shape: used to draw inner shadows and glows.</summary>
    public static Geometry Frame(Rect outer, Geometry hole) =>
        new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outer), hole);
}
