using System.Globalization;

namespace Island.Core;

/// <summary>An opaque sRGB colour, each channel 0..255.</summary>
public readonly record struct Rgb(double R, double G, double B)
{
    public static Rgb White { get; } = new(255, 255, 255);

    public static Rgb FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6) throw new FormatException($"Expected #RRGGBB, got '{hex}'.");
        return new Rgb(
            int.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(s[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public string ToHex() =>
        $"#{(int)Math.Round(Math.Clamp(R, 0, 255)):X2}{(int)Math.Round(Math.Clamp(G, 0, 255)):X2}{(int)Math.Round(Math.Clamp(B, 0, 255)):X2}";
}

public static class ColorMath
{
    /// <summary>Straight blend in sRGB, t = 0 gives a, t = 1 gives b.</summary>
    public static Rgb LerpSrgb(Rgb a, Rgb b, double t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    /// <summary>
    /// Mix in the OKLab colour space (Bjorn Ottosson, bottosson.github.io/posts/oklab).
    /// t = 0 gives a, t = 1 gives b. Out-of-gamut results are clamped.
    /// </summary>
    public static Rgb MixOkLab(Rgb a, Rgb b, double t)
    {
        var (l1, a1, b1) = ToOkLab(a);
        var (l2, a2, b2) = ToOkLab(b);
        return FromOkLab(l1 + (l2 - l1) * t, a1 + (a2 - a1) * t, b1 + (b2 - b1) * t);
    }

    /// <summary>The moving highlight's colour: category colour 62 parts, white 38 parts.</summary>
    public static Rgb ArcColor(Rgb category)
    {
        var total = LookConstants.ArcMixCategoryParts + LookConstants.ArcMixWhiteParts;
        return MixOkLab(category, Rgb.White, LookConstants.ArcMixWhiteParts / total);
    }

    public static (double L, double A, double B) ToOkLab(Rgb c)
    {
        var r = SrgbToLinear(c.R / 255.0);
        var g = SrgbToLinear(c.G / 255.0);
        var b = SrgbToLinear(c.B / 255.0);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    public static Rgb FromOkLab(double L, double A, double B)
    {
        var l = Cube(L + 0.3963377774 * A + 0.2158037573 * B);
        var m = Cube(L - 0.1055613458 * A - 0.0638541728 * B);
        var s = Cube(L - 0.0894841775 * A - 1.2914855480 * B);

        var r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return new Rgb(
            Math.Clamp(LinearToSrgb(r), 0, 1) * 255,
            Math.Clamp(LinearToSrgb(g), 0, 1) * 255,
            Math.Clamp(LinearToSrgb(b), 0, 1) * 255);
    }

    /// <summary>HSL (h degrees, s and l in percent) to sRGB.</summary>
    public static Rgb FromHsl(double hDegrees, double sPercent, double lPercent)
    {
        var h = ((hDegrees % 360) + 360) % 360;
        var s = sPercent / 100.0;
        var l = lPercent / 100.0;
        var a = s * Math.Min(l, 1 - l);
        double F(double n)
        {
            var k = (n + h / 30) % 12;
            return l - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1));
        }

        return new Rgb(F(0) * 255, F(8) * 255, F(4) * 255);
    }

    private static double Cube(double x) => x * x * x;

    private static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double LinearToSrgb(double c) =>
        c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
}
