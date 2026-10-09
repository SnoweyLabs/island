using System.Runtime.InteropServices;
using Island.Core;

namespace Island.Sources.Programs;

/// <summary>Turns the GDI handles Windows gives for icons into <see cref="IconImage"/> (32-bit BGRA, top row first) and frees them.</summary>
internal static class IconReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorUsed, ColorImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public bool IsIcon;
        public int XHotspot, YHotspot;
        public nint Mask, Color;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObjectW(nint handle, int size, out Bitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(nint icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    /// <summary>Reads and deletes a bitmap handle that carries its own alpha (what the shell's image factory returns).</summary>
    public static IconImage? FromBitmap(nint bitmap)
    {
        if (bitmap == 0) return null;
        try
        {
            var image = Pixels(bitmap);
            return image is not null && IconChoice.IsUsable(image) ? image : null;
        }
        finally
        {
            DeleteObject(bitmap);
        }
    }

    /// <summary>Reads and destroys an icon handle. Old icons without alpha get it from their mask.</summary>
    public static IconImage? FromIcon(nint icon)
    {
        if (icon == 0) return null;
        try
        {
            if (!GetIconInfo(icon, out var info)) return null;
            try
            {
                var image = Pixels(info.Color);
                if (image is null) return null;
                if (!image.Bgra.Where((_, i) => i % 4 == 3).Any(a => a != 0) && Pixels(info.Mask) is { } mask && mask.Width == image.Width)
                    ApplyMask(image.Bgra, mask.Bgra);
                return IconChoice.IsUsable(image) ? image : null;
            }
            finally
            {
                if (info.Color != 0) DeleteObject(info.Color);
                if (info.Mask != 0) DeleteObject(info.Mask);
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    /// <summary>A mask pixel that is black means "draw here".</summary>
    private static void ApplyMask(byte[] bgra, byte[] mask)
    {
        for (var i = 0; i + 3 < bgra.Length && i + 3 < mask.Length; i += 4)
            bgra[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
    }

    private static IconImage? Pixels(nint bitmap)
    {
        if (bitmap == 0 || GetObjectW(bitmap, Marshal.SizeOf<Bitmap>(), out var bm) == 0 || bm.Width <= 0 || bm.Height <= 0) return null;

        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = bm.Width,
            Height = -bm.Height, // negative: top row first
            Planes = 1,
            BitCount = 32,
        };
        var bytes = new byte[bm.Width * bm.Height * 4];
        var dc = GetDC(0);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)bm.Height, bytes, ref header, 0) == bm.Height ? new IconImage(bm.Width, bm.Height, bytes) : null;
        }
        finally
        {
            ReleaseDC(0, dc);
        }
    }
}
