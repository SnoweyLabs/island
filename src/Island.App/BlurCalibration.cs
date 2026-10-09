using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Island.App;

/// <summary>
/// Measures how WPF's <see cref="BlurEffect.Radius"/> relates to a Gaussian standard deviation,
/// so the CSS blur values of the reference can be translated. A hard white edge is blurred and its
/// 10%..90% rise is measured: for a Gaussian that rise is 2.563 standard deviations wide.
/// </summary>
internal static class BlurCalibration
{
    private const double RiseInSigmas = 2.563;

    public static IReadOnlyList<(double Radius, double Sigma)> Measure(params double[] radii) =>
        [.. radii.Select(r => (r, MeasureSigma(r)))];

    public static double MeasureSigma(double radius)
    {
        const int size = 600;
        var canvas = new Canvas { Width = size, Height = size, Background = Brushes.Transparent };
        var block = new Border
        {
            Width = 300,
            Height = 300,
            Background = Brushes.White,
            Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian },
        };
        Canvas.SetLeft(block, 150);
        Canvas.SetTop(block, 150);
        canvas.Children.Add(block);
        canvas.Measure(new Size(size, size));
        canvas.Arrange(new Rect(0, 0, size, size));
        canvas.UpdateLayout();

        var snap = Snapshot.Of(canvas, size, size, 96);
        var y = size / 2;
        var alphas = Enumerable.Range(0, size / 2).Select(x => snap.AlphaAt(x, y) / 255.0).ToArray();

        var x10 = Cross(alphas, 0.1);
        var x90 = Cross(alphas, 0.9);
        return (x90 - x10) / RiseInSigmas;
    }

    /// <summary>First position where the rising profile reaches the level, with linear interpolation.</summary>
    private static double Cross(double[] profile, double level)
    {
        for (var i = 1; i < profile.Length; i++)
        {
            if (profile[i] < level) continue;
            var span = profile[i] - profile[i - 1];
            return span <= 0 ? i : i - 1 + (level - profile[i - 1]) / span;
        }

        return profile.Length;
    }

    public static string Describe(IReadOnlyList<(double Radius, double Sigma)> rows) =>
        string.Join("; ", rows.Select(r =>
            $"radius {r.Radius.ToString(CultureInfo.InvariantCulture)} -> sigma {r.Sigma.ToString("0.00", CultureInfo.InvariantCulture)} (radius/sigma {(r.Radius / r.Sigma).ToString("0.00", CultureInfo.InvariantCulture)})"));
}
