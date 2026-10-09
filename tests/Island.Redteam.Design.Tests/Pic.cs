using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Redteam.Design.Tests;

/// <summary>A PNG the self-test drew, decoded into memory (a read of a file is not showing a window). Pixels are straight BGRA.</summary>
internal sealed class Pic
{
    public int Width { get; }
    public int Height { get; }
    public double DpiX { get; }
    private readonly byte[] _bgra;

    private Pic(int w, int h, double dpiX, byte[] bgra) { Width = w; Height = h; DpiX = dpiX; _bgra = bgra; }

    /// <summary>The folder that holds review/ and reference/ (the main folder; a worktree sits in .worktrees/ inside it).</summary>
    public static string? Root()
    {
        // The main folder first (a worktree sits in .worktrees/ inside it and its review/ may be older), then the worktree itself.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (dir.Parent is { Name: ".worktrees" } wt && wt.Parent is { } main
                && File.Exists(Path.Combine(main.FullName, "review", "media-dark.png"))) return main.FullName;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "review", "media-dark.png"))) return dir.FullName;
        return null;
    }

    public static Pic? TryLoad(string relative)
    {
        var root = Root();
        if (root is null) return null;
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
        return new Pic(converted.PixelWidth, converted.PixelHeight, frame.DpiX, bytes);
    }

    public (byte R, byte G, byte B, byte A) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (_bgra[i + 2], _bgra[i + 1], _bgra[i], _bgra[i + 3]);
    }

    public static double Lin(byte c)
    {
        var s = c / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    public double Lum(int x, int y)
    {
        var (r, g, b, _) = At(x, y);
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    public static double Ratio(double l1, double l2) => (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

    /// <summary>Distance of a pixel from a colour, summed over the three channels.</summary>
    public int Dist(int x, int y, (byte R, byte G, byte B) c)
    {
        var p = At(x, y);
        return Math.Abs(p.R - c.R) + Math.Abs(p.G - c.G) + Math.Abs(p.B - c.B);
    }
}
