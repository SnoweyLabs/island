using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Island.Core;

namespace Island.SettingsUi;

/// <summary>
/// The numbers and colours of look C ("Island glass") of reference/island-setup-previews.html, in one place.
/// Every value is copied from the <c>.shell[data-theme=c]</c> rules and the shared pieces they use.
/// </summary>
internal static class Look
{
    public const double H1Size = 34;
    public const double SubSize = 15;
    public const double BodySize = 14;
    public const double SmallSize = 12;
    public const double HintSize = 12.5;
    public const double ContentMaxWidth = 720;
    public const double GroupRadius = 22;
    public const double RowMinHeight = 52;
    public const double CapHeight = 30;
    public const double CapRadius = 14;
    public const double ButtonHeight = 38;
    public const double TopHeight = 44;

    // Tint over the desktop. Approved is the reference's rgba(12,14,22,.58). Darker adds the same ten points
    // that the capsule's darker glass adds to its own tint (0.60 to 0.70); Blur draws the reference tint and
    // leaves the blur itself to the glass layer behind this view.
    // WORK-ORDER-13 (Dan's P3): the reference's 0.58 and its ten points more left the 12.5 px hints at 2.77:1 and 3.55:1 over a white window behind the screen; 0.80 and 0.86 give 4.9:1 and 5.9:1 there
    // (over the dark wall of the self-test's pictures it was 8:1 and is more).
    public const double ApprovedTintAlpha = 0.80;
    public const double DarkerTintAlpha = 0.86;

    // Reference --sub / --group / --line, and the warn colour lightened from #d9480f so that it passes 4.5:1 on dark glass.
    public static readonly Color Tint = Color.FromRgb(12, 14, 22);
    // With Windows' high contrast on (Dan's P2, WORK-ORDER-13) every one of these is the system's own colour: text in the window text colour, fills in the window colour, lines in the text colour.
    private static bool HighContrast => SystemParameters.HighContrast;

    private static readonly Brush TextNormal = Brushes.White;
    private static readonly Brush SubNormal = Solid(255, 255, 255, 0.68);
    private static readonly Brush GroupNormal = Solid(255, 255, 255, 0.08);
    private static readonly Brush LineNormal = Solid(255, 255, 255, 0.12);
    private static readonly Brush CapFillNormal = Solid(255, 255, 255, 0.14);
    private static readonly Brush CapLineNormal = Solid(255, 255, 255, 0.22);
    private static readonly Brush HoverNormal = Solid(255, 255, 255, 0.07);
    private static readonly Brush ButtonFillNormal = Solid(255, 255, 255, 0.94);
    private static readonly Brush GhostLineNormal = Solid(255, 255, 255, 0.2);
    // the refusal colour, lightened (Dan's P3) from #FF8A5B (2.0:1 over a white window behind the screen) to #FFB08E (4.7:1 on a card over it, 6:1 on the bare tint)
    private static readonly Brush WarnNormal = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x8E));
    private static readonly Brush ScrimNormal = Solid(0, 0, 0, 0.5);
    private static readonly Brush DialogFillNormal = Solid(18, 20, 30, 0.94);
    private static readonly Brush ButtonTextNormal = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));

    public static Brush Text => HighContrast ? SystemColors.WindowTextBrush : TextNormal;
    public static Brush Sub => HighContrast ? SystemColors.WindowTextBrush : SubNormal;
    public static Brush Group => HighContrast ? SystemColors.WindowBrush : GroupNormal;
    public static Brush Line => HighContrast ? SystemColors.WindowTextBrush : LineNormal;
    public static Brush CapFill => HighContrast ? SystemColors.WindowBrush : CapFillNormal;
    public static Brush CapLine => HighContrast ? SystemColors.WindowTextBrush : CapLineNormal;
    public static Brush Hover => HighContrast ? SystemColors.ControlBrush : HoverNormal;
    public static Brush ButtonFill => HighContrast ? SystemColors.ControlBrush : ButtonFillNormal;
    public static Brush GhostLine => HighContrast ? SystemColors.WindowTextBrush : GhostLineNormal;
    public static Brush Warn => HighContrast ? SystemColors.WindowTextBrush : WarnNormal;
    public static Brush Scrim => HighContrast ? SystemColors.WindowBrush : ScrimNormal;
    public static Brush DialogFill => HighContrast ? SystemColors.WindowBrush : DialogFillNormal;
    public static Brush ButtonText => HighContrast ? SystemColors.ControlTextBrush : ButtonTextNormal;

    public static readonly string[] Swatches =
        ["#FF4055", "#FFB81C", "#1F6FFF", "#19E6B3", "#E9A0FF", "#FF7AB6", "#FF8A3D", "#3FD0FF", "#8F6BFF", "#F2F2F2"];

    public static readonly FontFamily Font = new($"{LookConstants.FontPrimary}, {LookConstants.FontFallback}");

    private static readonly DropShadowEffect Shadow = MakeShadow();

    static Look()
    {
        foreach (var brush in new[] { WarnNormal, ButtonTextNormal }) brush.Freeze();
    }

    public static SolidColorBrush Solid(byte r, byte g, byte b, double alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Color ColorOf(string hex, double alpha = 1)
    {
        var c = Rgb.FromHex(hex);
        return Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), (byte)c.R, (byte)c.G, (byte)c.B);
    }

    public static SolidColorBrush BrushOf(string hex, double alpha = 1)
    {
        var brush = new SolidColorBrush(ColorOf(hex, alpha));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Small accent text (links) on dark glass: the page colour mixed a quarter of the way to white in OKLab, so that a
    /// deep blue such as the Apps colour still reads at 12 px (EVALS G5). Dots, rings and chips keep the exact colour.
    /// </summary>
    public const double LinkWhiteMix = 0.25;

    public static string ReadableAccent(string hex) => ColorMath.MixOkLab(Rgb.FromHex(hex), Rgb.White, LinkWhiteMix).ToHex();

    /// <summary>The reference's color-mix(in oklab, accent N%, transparent): the colour at N% opacity.</summary>
    public static SolidColorBrush Mix(string hex, double percent) => BrushOf(hex, percent / 100);

    /// <summary>The tint of the whole screen for a glass.</summary>
    public static Brush TintFor(GlassKind glass) => HighContrast
        ? SystemColors.WindowBrush // high contrast: the window colour, opaque
        : new SolidColorBrush(Color.FromArgb((byte)Math.Round(TintAlphaFor(glass) * 255), Tint.R, Tint.G, Tint.B));

    public static double TintAlphaFor(GlassKind glass) => glass == GlassKind.Darker ? DarkerTintAlpha : ApprovedTintAlpha;

    /// <summary>A piece of text in the reference's type: Segoe UI Variable, white, with the soft text shadow.</summary>
    public static System.Windows.Controls.TextBlock Label(string text, double size = BodySize, FontWeight? weight = null, Brush? brush = null) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = Scaled(size),
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Text,
        TextWrapping = TextWrapping.Wrap,
        Effect = HighContrast ? null : Shadow,
    };

    /// <summary>A type size with Windows' text size applied (Dan's P2).</summary>
    public static double Scaled(double size) => TextScale.Of(size);

    // text-shadow: 0 1px 2px rgba(0,0,0,.35): a CSS blur of 2 px is a standard deviation of 1, which WPF's radius counts three times.
    private static DropShadowEffect MakeShadow()
    {
        var shadow = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.35,
            ShadowDepth = 1,
            Direction = 270,
            BlurRadius = 3,
            RenderingBias = RenderingBias.Quality,
        };
        shadow.Freeze();
        return shadow;
    }
}
