using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Redteam.Ease.Tests.Harness;

/// <summary>WCAG 2.2 relative luminance and contrast ratio (the formulas of 1.4.3 and 1.4.11), and the pictures the self-test drew, read from review/.</summary>
internal static class Wcag
{
    public static double Channel(double c8)
    {
        var c = c8 / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    public static double Luminance(double r, double g, double b) => 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);

    public static double Ratio(double l1, double l2)
    {
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static double Ratio((double R, double G, double B) a, (double R, double G, double B) b) => Ratio(Luminance(a.R, a.G, a.B), Luminance(b.R, b.G, b.B));

    /// <summary>A colour with an alpha laid over an opaque colour.</summary>
    public static (double R, double G, double B) Over((double R, double G, double B) top, double alpha, (double R, double G, double B) under) =>
        (top.R * alpha + under.R * (1 - alpha), top.G * alpha + under.G * (1 - alpha), top.B * alpha + under.B * (1 - alpha));
}

/// <summary>One picture of review/ as pixels. Looked for in the main folder first (its pictures are the newest), then in the worktree; null when neither has it.</summary>
internal sealed class Picture
{
    private readonly byte[] _bgra;

    private Picture(byte[] bgra, int width, int height)
    {
        _bgra = bgra;
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public static string? Find(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Island.sln"))) dir = dir.Parent;
        if (dir is null) return null;
        var main = dir.Parent?.Parent; // <main>\.worktrees\<name>
        foreach (var root in new[] { main?.FullName, dir.FullName })
        {
            if (root is null) continue;
            var path = Path.Combine(root, "review", relative);
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public static Picture? Load(string relative)
    {
        if (Find(relative) is not { } path) return null;
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var bytes = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(bytes, stride, 0);
        return new Picture(bytes, converted.PixelWidth, converted.PixelHeight);
    }

    public (double R, double G, double B) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (_bgra[i + 2], _bgra[i + 1], _bgra[i]);
    }

    /// <summary>
    /// The contrast of a piece of text or a mark in a rectangle: the background is the most common colour of the rectangle (quantised), the foreground is the pixel farthest from it in luminance
    /// (anti-aliased edges are softer than the text's real colour, so this reads a little low for thin text).
    /// </summary>
    public (double Ratio, (double R, double G, double B) Background, (double R, double G, double B) Foreground) TextIn(int x0, int y0, int x1, int y1)
    {
        var counts = new Dictionary<(int, int, int), int>();
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var (r, g, b) = At(x, y);
                var key = ((int)r / 8, (int)g / 8, (int)b / 8);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

        var top = counts.MaxBy(c => c.Value).Key;
        var bgPixels = new List<(double R, double G, double B)>();
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var (r, g, b) = At(x, y);
                if (((int)r / 8, (int)g / 8, (int)b / 8) == top) bgPixels.Add((r, g, b));
            }

        var bg = (bgPixels.Average(p => p.R), bgPixels.Average(p => p.G), bgPixels.Average(p => p.B));
        var lb = Wcag.Luminance(bg.Item1, bg.Item2, bg.Item3);
        var best = bg;
        var bestDistance = -1.0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var p = At(x, y);
                var d = Math.Abs(Wcag.Luminance(p.R, p.G, p.B) - lb);
                if (d > bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }

        return (Wcag.Ratio(best, bg), bg, best);
    }
}
