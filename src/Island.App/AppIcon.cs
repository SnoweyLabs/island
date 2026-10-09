using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Island.Core;

namespace Island.App;

/// <summary>
/// The app's icon (WORK-ORDER-8 section 4), drawn with the island's own shapes and colours and nothing else: the glass of the capsule, its rim, and the
/// light on the rim. Three candidates; A is the default until the owner chooses. Drawn on a 256 grid, rendered at any size, with a transparent background.
/// No picture of a real program, no text.
/// </summary>
internal static class AppIcon
{
    public enum Candidate
    {
        /// <summary>The ball with its lit rim, in the Apps blue.</summary>
        A,

        /// <summary>The small capsule with the moving light caught at the top, in the Media red.</summary>
        B,

        /// <summary>The ball with the rim divided into the five page colours.</summary>
        C,
    }

    public const Candidate Default = Candidate.A;

    private const double Grid = 256;

    private static readonly string[] PageColours = [LookConstants.MediaColor, LookConstants.FoldersColor, LookConstants.AppsColor, LookConstants.VibeColor, LookConstants.BrowserColor];

    public static string Describe(Candidate candidate) => candidate switch
    {
        Candidate.A => "A  the ball, its rim lit, Apps blue",
        Candidate.B => "B  the capsule, the light at the top, Media red",
        _ => "C  the ball, the rim in the five page colours",
    };

    /// <summary>The icon at a size (pixels, square), transparent around it.</summary>
    public static BitmapSource Render(Candidate candidate, int size)
    {
        var visual = Draw(candidate);
        visual.Transform = new ScaleTransform(size / Grid, size / Grid);
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); // frames only: it adds no text, date or camera data
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>An .ico holding the sizes, each as a PNG (the format Windows has read since Vista). 256 is stored as 0, as the format says.</summary>
    public static byte[] Ico(Candidate candidate, IReadOnlyList<int> sizes)
    {
        var images = sizes.Select(s => (Size: s, Data: Png(Render(candidate, s)))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0);
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images) writer.Write(data);
        return stream.ToArray();
    }

    /// <summary>The three candidates side by side, labelled, at 256 and at 32, on a plain dark background (review/choices/app-icon.png).</summary>
    public static BitmapSource Sheet()
    {
        const int column = 360;
        const int height = 470;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 31, 58)), null, new Rect(0, 0, column * 3, height));
            var index = 0;
            foreach (var candidate in Enum.GetValues<Candidate>())
            {
                var left = index++ * column;
                dc.DrawText(Text(Describe(candidate), 15, FontWeights.SemiBold), new Point(left + 24, 18));
                dc.DrawImage(Render(candidate, 256), new Rect(left + 52, 56, 256, 256));
                dc.DrawText(Text("256 x 256", 12, FontWeights.Normal, 0.6), new Point(left + 52, 318));
                dc.DrawImage(Render(candidate, 32), new Rect(left + 52, 360, 32, 32));
                dc.DrawText(Text("32 x 32", 12, FontWeights.Normal, 0.6), new Point(left + 94, 368));
                dc.DrawImage(Render(candidate, 16), new Rect(left + 52, 408, 16, 16));
                dc.DrawText(Text("16 x 16", 12, FontWeights.Normal, 0.6), new Point(left + 78, 408));
            }
        }

        var bitmap = new RenderTargetBitmap(column * 3, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static FormattedText Text(string text, double size, FontWeight weight, double alpha = 1) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size,
            new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 255, 255, 255)), 1.0);

    // ---- the drawing ---------------------------------------------------------------------------------------------------------------------

    private static DrawingVisual Draw(Candidate candidate)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            switch (candidate)
            {
                case Candidate.A:
                    visual.Effect = Glow(LookConstants.AppsColor);
                    Ball(dc, LookConstants.AppsColor);
                    break;
                case Candidate.B:
                    visual.Effect = Glow(LookConstants.MediaColor);
                    Capsule(dc, LookConstants.MediaColor);
                    break;
                default:
                    visual.Effect = Glow(LookConstants.AppsColor, 0.35);
                    ColourWheelBall(dc);
                    break;
            }
        }

        return visual;
    }

    private static readonly Point Centre = new(Grid / 2, Grid / 2);

    private const double BallRadius = 92;
    private const double RimWidth = 9;

    private static Color Hex(string hex, double alpha = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);
    }

    private static Color Lighter(string hex, double towardsWhite)
    {
        var c = Hex(hex);
        byte Mix(byte v) => (byte)(v + (255 - v) * towardsWhite);
        return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private static DropShadowEffect Glow(string hex, double opacity = 0.65) =>
        new() { Color = Hex(hex), BlurRadius = 22, ShadowDepth = 0, Opacity = opacity };

    /// <summary>The glass of the island: its dark base and the paler light on the upper half.</summary>
    private static Brush GlassBase() => new SolidColorBrush(Hex(LookConstants.GlassBaseColor, 0.94));

    private static Brush GlassLight() => new LinearGradientBrush(Color.FromArgb(78, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90);

    private static void Ball(DrawingContext dc, string colour)
    {
        dc.DrawEllipse(GlassBase(), null, Centre, BallRadius, BallRadius);
        dc.DrawEllipse(GlassLight(), null, Centre, BallRadius, BallRadius);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Hex(colour, 0.55)), RimWidth), Centre, BallRadius, BallRadius);
        Arc(dc, Centre, BallRadius, -160, -20, new Pen(new SolidColorBrush(Lighter(colour, 0.35)), RimWidth + 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Arc(dc, Centre, BallRadius, -112, -76, new Pen(Brushes.White, RimWidth - 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    }

    private static void Capsule(DrawingContext dc, string colour)
    {
        var rect = new Rect(18, 73, 220, 110);
        const double radius = 55;
        dc.DrawRoundedRectangle(GlassBase(), null, rect, radius, radius);
        dc.DrawRoundedRectangle(GlassLight(), null, rect, radius, radius);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Hex(colour, 0.55)), RimWidth), rect, radius, radius);
        // The moving light, caught at the top: along the straight part of the rim and a little round both curves.
        var top = new StreamGeometry();
        using (var g = top.Open())
        {
            g.BeginFigure(new Point(rect.Left + radius - 30, rect.Top), false, false);
            g.LineTo(new Point(rect.Right - radius + 4, rect.Top), true, false);
            g.ArcTo(new Point(rect.Right - radius + 4 + 28, rect.Top + 5.5), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }

        top.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Lighter(colour, 0.35)), RimWidth + 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, top);
        var tip = new StreamGeometry();
        using (var g = tip.Open())
        {
            g.BeginFigure(new Point(rect.Left + radius + 20, rect.Top), false, false);
            g.LineTo(new Point(rect.Left + radius + 62, rect.Top), true, false);
        }

        tip.Freeze();
        dc.DrawGeometry(null, new Pen(Brushes.White, RimWidth - 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, tip);
    }

    private static void ColourWheelBall(DrawingContext dc)
    {
        dc.DrawEllipse(GlassBase(), null, Centre, BallRadius, BallRadius);
        dc.DrawEllipse(GlassLight(), null, Centre, BallRadius, BallRadius);
        const double gap = 6;
        for (var i = 0; i < PageColours.Length; i++)
        {
            var from = -90 + i * 72 + gap / 2;
            var to = -90 + (i + 1) * 72 - gap / 2;
            Arc(dc, Centre, BallRadius, from, to, new Pen(new SolidColorBrush(Hex(PageColours[i])), RimWidth + 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        }
    }

    /// <summary>An arc of a circle from one angle to another, in degrees, clockwise from three o'clock.</summary>
    private static void Arc(DrawingContext dc, Point centre, double radius, double fromDegrees, double toDegrees, Pen pen)
    {
        Point At(double degrees)
        {
            var r = degrees * Math.PI / 180;
            return new Point(centre.X + radius * Math.Cos(r), centre.Y + radius * Math.Sin(r));
        }

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(At(fromDegrees), false, false);
            g.ArcTo(At(toDegrees), new Size(radius, radius), 0, toDegrees - fromDegrees > 180, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
