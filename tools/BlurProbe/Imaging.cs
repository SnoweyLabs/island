using System.IO.Compression;

namespace BlurProbe;

/// <summary>A captured picture, BGRA, top-down, in the pattern window's client pixels.</summary>
internal sealed class Pixels
{
    public readonly int W, H; public readonly byte[] Data;
    public Pixels(int w, int h, byte[] data) { W = w; H = h; Data = data; }
    public int Gray(int x, int y) { int i = (y * W + x) * 4; return (Data[i] + Data[i + 1] + Data[i + 2]) / 3; }
    public int Spread(int x, int y)
    {
        int i = (y * W + x) * 4; int b = Data[i], g = Data[i + 1], r = Data[i + 2];
        return Math.Max(Math.Abs(r - g), Math.Max(Math.Abs(g - b), Math.Abs(r - b)));
    }
}

/// <summary>A rounded rectangle in client pixels; Sd is the signed distance to its outline (negative inside).</summary>
internal readonly record struct Shape(double X, double Y, double W, double H, double R)
{
    public double Sd(double px, double py)
    {
        double hx = W / 2, hy = H / 2, cx = X + hx, cy = Y + hy;
        double qx = Math.Abs(px - cx) - (hx - R), qy = Math.Abs(py - cy) - (hy - R);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - R;
    }
}

internal readonly record struct RegionStats(int Count, int Min, int Max, double Mean, double PureFraction, double NonPureFraction, int MaxSpread);

internal static class Imaging
{
    const int PureTolerance = 8;   // a pixel is "pure" when its three channels agree within 8 and it is within 8 of black or white

    public static RegionStats Measure(Pixels p, Func<int, int, bool> mask)
    {
        int n = 0, min = 255, max = 0, pure = 0, spreadMax = 0; double sum = 0;
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                if (!mask(x, y)) continue;
                int g = p.Gray(x, y), s = p.Spread(x, y);
                n++; sum += g; if (g < min) min = g; if (g > max) max = g; if (s > spreadMax) spreadMax = s;
                if (s <= PureTolerance && (g <= PureTolerance || g >= 255 - PureTolerance)) pure++;
            }
        if (n == 0) return new(0, 0, 0, 0, 0, 0, 0);
        return new(n, min, max, sum / n, (double)pure / n, 1.0 - (double)pure / n, spreadMax);
    }

    public static double MeanAbsDiff(Pixels a, Pixels b, Func<int, int, bool> mask)
    {
        long n = 0; double sum = 0;
        for (int y = 0; y < a.H; y++)
            for (int x = 0; x < a.W; x++)
                if (mask(x, y)) { sum += Math.Abs(a.Gray(x, y) - b.Gray(x, y)); n++; }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>RMS of b(x,y) - a(x+dx,y) over the mask. Small for the right dx means the picture moved by exactly that much.</summary>
    public static double ShiftRms(Pixels a, Pixels b, Func<int, int, bool> mask, int dx)
    {
        long n = 0; double sum = 0;
        for (int y = 0; y < a.H; y++)
            for (int x = 0; x < a.W; x++)
            {
                int xs = x + dx;
                if (!mask(x, y) || xs < 0 || xs >= a.W) continue;
                double d = b.Gray(x, y) - a.Gray(xs, y); sum += d * d; n++;
            }
        return n == 0 ? double.NaN : Math.Sqrt(sum / n);
    }

    // ---- PNG (RGB, 8 bit, no filter), written by hand because no image library may be added ----
    public static void SavePng(string path, Pixels p, int cropX = 0, int cropY = 0, int cropW = -1, int cropH = -1, int scale = 1)
    {
        if (cropW < 0) { cropW = p.W; cropH = p.H; }
        cropX = Math.Clamp(cropX, 0, p.W - 1); cropY = Math.Clamp(cropY, 0, p.H - 1);
        cropW = Math.Min(cropW, p.W - cropX); cropH = Math.Min(cropH, p.H - cropY);
        int ow = cropW * scale, oh = cropH * scale;
        var raw = new byte[(ow * 3 + 1) * oh];
        for (int y = 0; y < oh; y++)
        {
            int o = y * (ow * 3 + 1); raw[o++] = 0;
            int sy = cropY + y / scale;
            for (int x = 0; x < ow; x++)
            {
                int i = (sy * p.W + cropX + x / scale) * 4;
                raw[o++] = p.Data[i + 2]; raw[o++] = p.Data[i + 1]; raw[o++] = p.Data[i];
            }
        }
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, ow); WriteBe(ihdr, 4, oh); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(fs, "IHDR", ihdr);
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw);
        Chunk(fs, "IDAT", z.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void WriteBe(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBe(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, t), data) ^ 0xFFFFFFFF;
        var c = new byte[4]; WriteBe(c, 0, (int)crc); s.Write(c);
    }

    static readonly uint[] Table = MakeTable();
    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }
    static uint Crc(uint crc, byte[] buf) { foreach (var b in buf) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8); return crc; }
}
