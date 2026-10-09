using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island.SettingsUi;

/// <summary>The first step of the setup: the orb of the preview, a ball of the step's colour with a soft light in it, and nothing else.</summary>
internal static class WelcomeSection
{
    private const double OrbSize = 64;

    public static FrameworkElement Build(ISectionHost host)
    {
        var colour = Look.ColorOf(host.Accent);
        var shade = Color.FromRgb((byte)(colour.R * 0.4), (byte)(colour.G * 0.4), (byte)(colour.B * 0.4));
        var fill = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.35, 0.3), RadiusX = 0.85, RadiusY = 0.85 };
        fill.GradientStops.Add(new GradientStop(Colors.White, 0));
        fill.GradientStops.Add(new GradientStop(colour, 0.6));
        fill.GradientStops.Add(new GradientStop(shade, 1));
        fill.Freeze();

        return new Border
        {
            Width = OrbSize,
            Height = OrbSize,
            CornerRadius = new CornerRadius(OrbSize / 2),
            Background = fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 26, 0, 26),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = colour, Opacity = 0.5, ShadowDepth = 0, BlurRadius = 40 },
        };
    }
}
