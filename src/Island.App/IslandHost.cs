using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Island.Core;

namespace Island.App;

/// <summary>The window that holds the shape. Takes mouse clicks; sits above the shadow window.</summary>
internal sealed class CapsuleWindow : OverlayWindow
{
    public Island.App.Visuals.IslandRoot Root { get; } = new() { Background = Brushes.Transparent };

    public CapsuleWindow() : base(ignoresMouse: false)
    {
        Title = Island.App.Visuals.IslandRoot.Name; // the window's name for a screen reader
        Content = Root;
    }
}

/// <summary>
/// Holds the drop shadow and the bloom. Ignores the mouse entirely: those pixels are
/// visible, and a visible pixel in a layered window would otherwise catch every click.
/// </summary>
internal sealed class ShadowWindow : OverlayWindow
{
    public Canvas Root { get; } = new() { Background = Brushes.Transparent };

    public ShadowWindow() : base(ignoresMouse: true)
    {
        Title = "Island shadow";
        Content = Root;
    }
}

/// <summary>
/// The two windows together. Both have the same fixed size and position, computed from
/// <see cref="WindowMetrics"/>, and are never resized or moved during an animation.
/// </summary>
internal sealed class IslandHost
{
    public CapsuleWindow Capsule { get; } = new();
    public ShadowWindow Shadow { get; } = new();

    public double WidthDip => WindowMetrics.Width;
    public double HeightDip => WindowMetrics.Height;

    public void Show()
    {
        foreach (var w in new OverlayWindow[] { Shadow, Capsule })
        {
            w.Width = WidthDip;
            w.Height = HeightDip;
        }

        Shadow.Show();
        Capsule.Show();
        Shadow.PlaceAtTopCentre(WidthDip, HeightDip);
        Capsule.PlaceAtTopCentre(WidthDip, HeightDip);

        // Capsule on top, shadow directly beneath it.
        Native.SetWindowPos(Shadow.Handle, Capsule.Handle, 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate);
    }

    /// <summary>Moves both windows, together, to this rectangle in real pixels (no activation, no change of the order they lie in).</summary>
    public void MoveTo(Island.Core.PixelRect rect)
    {
        Shadow.MoveTo(rect);
        Capsule.MoveTo(rect);
    }

    /// <summary>Both windows really are where the rectangle says (the framework may move or resize a window by itself when its scaling changes).</summary>
    public bool RealRectangleMatches(Island.Core.PixelRect rect) => Capsule.RealRectangle == rect && Shadow.RealRectangle == rect;

    public void Close()
    {
        Capsule.Close();
        Shadow.Close();
    }
}

/// <summary>A plain rounded shape with a soft shadow, used by the section 3 self-test.</summary>
internal static class TestShape
{
    public const double Width = 466;
    public const double Height = LookConstants.CapsuleHeight;
    public const double Radius = LookConstants.CapsuleCornerRadius;
    public const double Top = LookConstants.TopGap;

    public static double Left(IslandHost host) => (host.WidthDip - Width) / 2;

    public static void Build(IslandHost host)
    {
        var shape = new Border
        {
            Width = Width,
            Height = Height,
            CornerRadius = new CornerRadius(Radius),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 20, 22, 32)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 255, 255, 255)),
            BorderThickness = new Thickness(1),
        };
        Canvas.SetLeft(shape, Left(host));
        Canvas.SetTop(shape, Top);
        host.Capsule.Root.Children.Add(shape);

        var shadow = new Border
        {
            Width = Width,
            Height = Height,
            CornerRadius = new CornerRadius(Radius),
            Background = new SolidColorBrush(Color.FromArgb((byte)(LookConstants.ShadowAlpha * 255), 0, 0, 0)),
            Effect = new BlurEffect { Radius = WindowMetrics.ShadowReach, KernelType = KernelType.Gaussian },
        };
        Canvas.SetLeft(shadow, Left(host));
        Canvas.SetTop(shadow, Top + LookConstants.ShadowOffsetY);

        // Keep the shadow out from under the shape: it is drawn only outside the outline.
        var hole = new RectangleGeometry(new Rect(Left(host), Top, Width, Height), Radius, Radius);
        var everything = new RectangleGeometry(new Rect(0, 0, host.WidthDip, host.HeightDip));
        host.Shadow.Root.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, everything, hole);
        host.Shadow.Root.Children.Add(shadow);
    }
}
