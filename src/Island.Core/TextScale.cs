namespace Island.Core;

/// <summary>
/// Windows' "Make text bigger" (Settings, Accessibility, Text size), 100% to 225% (Dan's P2, WORK-ORDER-13). WPF does not apply it by itself; the app reads it (WinRT UISettings.TextScaleFactor) and
/// puts it here, and the settings screen sizes its type with <see cref="Of"/>. The island's own type sits in a capsule whose size is fixed by the look Dan approved and is not scaled.
/// </summary>
public static class TextScale
{
    public const double Min = 1;
    public const double Max = 2.25;

    private static double _factor = 1;

    /// <summary>The factor, 1 until the app says otherwise; held between <see cref="Min"/> and <see cref="Max"/>.</summary>
    public static double Factor
    {
        get => _factor;
        set => _factor = Clamp(value);
    }

    public static double Clamp(double factor) => double.IsFinite(factor) ? Math.Clamp(factor, Min, Max) : Min;

    /// <summary>A type size with the factor applied, to a tenth of a point.</summary>
    public static double Of(double size) => Math.Round(size * _factor, 1);
}
