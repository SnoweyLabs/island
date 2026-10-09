namespace Island.Core;

/// <summary>A point in real (physical) pixels of the virtual screen. Signed: a screen left of or above the main one has negative coordinates.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// A rectangle in real pixels, edges as Windows gives them (Right and Bottom are the first pixel outside).
/// Width and Height are long so that absurd input cannot overflow.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public long Width => (long)Right - Left;

    public long Height => (long)Bottom - Top;

    /// <summary>True when the rectangle has at least one pixel.</summary>
    public bool HasArea => Width > 0 && Height > 0;

    public bool Contains(ScreenPoint p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
}

/// <summary>
/// One screen as Windows reports it, all in real pixels: its full rectangle, its work area (the full rectangle
/// without the taskbar and other docked bars), its scaling (1.0 = 100%, 1.5 = 150%) and whether it is the main one.
/// </summary>
public readonly record struct ScreenInfo(PixelRect Full, PixelRect Work, double Scale, bool IsPrimary = false)
{
    /// <summary>The stand-in used only when no screen can be read at all: 1920 x 1080 at the origin, 100%.</summary>
    public static ScreenInfo Fallback { get; } = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), 1.0, true);

    /// <summary>A screen the chooser can use: its full rectangle has pixels. A work area without pixels is replaced by the full rectangle.</summary>
    public bool IsUsable => Full.HasArea;

    /// <summary>The scaling made safe: NaN, infinity, zero and negative values read as 1.0.</summary>
    public double SafeScale => double.IsFinite(Scale) && Scale > 0 ? Scale : 1.0;

    /// <summary>The work area cut to the full rectangle, or the full rectangle when the reported work area is empty or lies outside it.</summary>
    public PixelRect SafeWork => Work.HasArea && CutTo(Work, Full) is { HasArea: true } cut ? cut : Full;

    /// <summary>A work area is part of its screen: whatever lies outside the full rectangle is cut away.</summary>
    private static PixelRect CutTo(PixelRect work, PixelRect full) =>
        new(Math.Max(work.Left, full.Left), Math.Max(work.Top, full.Top), Math.Min(work.Right, full.Right), Math.Min(work.Bottom, full.Bottom));

    /// <summary>Width of the work area in device-independent units (real pixels divided by the scaling).</summary>
    public double WorkWidthDip => SafeWork.Width / SafeScale;
}

/// <summary>
/// The result of choosing a screen: which one (its index in the list handed in; -1 for the stand-in), the screen,
/// and the rectangle in real pixels the island's window gets on it. <see cref="IsFallback"/> is true when the list
/// held no usable screen and <see cref="ScreenInfo.Fallback"/> was used instead; the app may then leave the window where it is.
/// </summary>
public readonly record struct ScreenPlacement(int Index, ScreenInfo Screen, PixelRect Window, bool IsFallback);
