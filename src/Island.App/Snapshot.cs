using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.App;

/// <summary>
/// A rendering of the island's own element tree (never of the screen). Pixels are
/// premultiplied BGRA, row by row from the top.
/// </summary>
internal sealed class Snapshot
{
    private readonly byte[] _pixels;

    private Snapshot(BitmapSource bitmap, byte[] pixels)
    {
        Bitmap = bitmap;
        _pixels = pixels;
        Width = bitmap.PixelWidth;
        Height = bitmap.PixelHeight;
    }

    public BitmapSource Bitmap { get; }
    public int Width { get; }
    public int Height { get; }

    public static Snapshot Of(FrameworkElement element, int pixelWidth, int pixelHeight, double dpi)
    {
        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(element);
        rtb.Freeze();
        var stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        rtb.CopyPixels(pixels, stride, 0);
        return new Snapshot(rtb, pixels);
    }

    public (byte R, byte G, byte B, byte A) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (_pixels[i + 2], _pixels[i + 1], _pixels[i], _pixels[i + 3]);
    }

    public byte AlphaAt(int x, int y) => At(x, y).A;

    public bool SameAs(Snapshot other) =>
        Width == other.Width && Height == other.Height && _pixels.AsSpan().SequenceEqual(other._pixels);

    /// <summary>Smallest box holding every pixel whose alpha is at least the threshold; null when there is none.</summary>
    public (int Left, int Top, int Right, int Bottom)? Bounds(byte alphaThreshold)
    {
        int left = Width, top = Height, right = -1, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (_pixels[(y * Width + x) * 4 + 3] < alphaThreshold) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    /// <summary>Smallest box holding every pixel that differs from the other snapshot's (same size); null when they are the same.</summary>
    public (int Left, int Top, int Right, int Bottom)? DiffBounds(Snapshot other)
    {
        if (Width != other.Width || Height != other.Height) return (0, 0, Width - 1, Height - 1);
        int left = Width, top = Height, right = -1, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 4;
                if (_pixels[i] == other._pixels[i] && _pixels[i + 1] == other._pixels[i + 1] && _pixels[i + 2] == other._pixels[i + 2] && _pixels[i + 3] == other._pixels[i + 3]) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < 0 ? null : (left, top, right, bottom);
    }

    /// <summary>Mean colour over a pixel rectangle, un-premultiplied (meant for opaque snapshots).</summary>
    public (double R, double G, double B) Mean(int left, int top, int width, int height)
    {
        double r = 0, g = 0, b = 0;
        var n = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var (pr, pg, pb, _) = At(x, y);
                r += pr;
                g += pg;
                b += pb;
                n++;
            }
        }

        return n == 0 ? (0, 0, 0) : (r / n, g / n, b / n);
    }

    public void SavePng(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
