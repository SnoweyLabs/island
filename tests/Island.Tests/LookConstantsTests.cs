using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Island.Core;

namespace Island.Tests;

public class LookConstantsTests
{
    // Name, runtime value, and the literal exactly as it must appear in the source.
    private static readonly (string Name, object Value, string Literal)[] Expected =
    [
        // Shape
        ("BallSize", 30.0, "30"),
        ("CapsuleHeight", 76.0, "76"),
        ("CapsuleCornerRadius", 38.0, "38"),
        ("TopGap", 14.0, "14"),
        ("SpawnHeightAboveEdge", 90.0, "90"),
        ("MinSizeFraction", 0.94, "0.94"),
        // Width rule
        ("WidthLeadingInset", 20.0, "20"),
        ("WidthChip", 36.0, "36"),
        ("WidthChipGap", 12.0, "12"),
        ("WidthItemPitch", 48.0, "48"),
        ("WidthItemTrailingGap", 8.0, "8"),
        ("WidthItemsToTextGap", 14.0, "14"),
        ("WidthTextBlock", 130.0, "130"),
        ("WidthTextToControlsGap", 10.0, "10"),
        ("WidthMediaControls", 88.0, "88"),
        ("WidthOtherControls", 28.0, "28"),
        ("WidthTrailingInset", 20.0, "20"),
        // Glass
        ("GlassGradientTopAlpha", 0.24, "0.24"),
        ("GlassGradientBottomAlpha", 0.05, "0.05"),
        ("GlassBaseColor", "#141620", "\"#141620\""),
        ("GlassBaseAlpha", 0.6, "0.6"),
        ("BorderWidth", 1.0, "1"),
        ("BorderAlpha", 0.4, "0.4"),
        ("TopHighlightWidth", 1.0, "1"),
        ("TopHighlightAlpha", 0.7, "0.7"),
        ("BottomGlowAlpha", 0.07, "0.07"),
        ("BottomGlowOffset", 10.0, "10"),
        ("BottomGlowCssBlur", 24.0, "24"),
        ("CategoryGlowAlpha", 0.34, "0.34"),
        ("CategoryGlowCssBlur", 26.0, "26"),
        ("ShadowAlpha", 0.32, "0.32"),
        ("ShadowOffsetY", 18.0, "18"),
        ("ShadowCssBlur", 50.0, "50"),
        // Rim
        ("RimInset", 0.5, "0.5"),
        ("BaseRimWidth", 1.6, "1.6"),
        ("BaseRimAlpha", 0.7, "0.7"),
        ("ArcFraction", 0.28, "0.28"),
        ("ArcWidth", 2.6, "2.6"),
        ("ArcMixCategoryParts", 62.0, "62"),
        ("ArcMixWhiteParts", 38.0, "38"),
        ("ArcSpeedPerSecond", 0.18, "0.18"),
        ("SecondArcFraction", 0.12, "0.12"),
        ("SecondArcWidth", 2.0, "2"),
        ("SecondArcAlpha", 0.55, "0.55"),
        ("SecondArcPhase", 0.5, "0.5"),
        ("FrontRimBlurCss", 0.6, "0.6"),
        ("BloomBaseWidth", 7.0, "7"),
        ("BloomBaseAlpha", 0.45, "0.45"),
        ("BloomArcWidth", 11.0, "11"),
        ("BloomBlurCss", 10.0, "10"),
        ("BloomLayerAlpha", 0.85, "0.85"),
        ("ColorChangeMs", 350.0, "350"),
        // Contents
        ("ChipSize", 36.0, "36"),
        ("ChipAlpha", 0.72, "0.72"),
        ("ChipGlyphSize", 18.0, "18"),
        ("ItemSize", 40.0, "40"),
        ("ItemGap", 8.0, "8"),
        ("ItemLabelFontSize", 12.0, "12"),
        ("ItemGradientAngle", 160.0, "160"),
        ("ItemTopSaturation", 72.0, "72"),
        ("ItemTopLightness", 62.0, "62"),
        ("ItemBottomSaturation", 70.0, "70"),
        ("ItemBottomLightness", 40.0, "40"),
        ("ItemFillAlpha", 0.92, "0.92"),
        ("ItemUnselectedOpacity", 0.74, "0.74"),
        ("DimmedControlOpacity", 0.8, "0.8"), // WORK-ORDER-13 (Dan's P5): it was 0.4, written in three places
        ("SelectedRingWidth", 2.0, "2"),
        ("SelectedRingAlpha", 0.92, "0.92"),
        ("SelectedGlowRadius", 14.0, "14"),
        ("TitleFontSize", 13.5, "13.5"),
        ("SubtitleFontSize", 11.5, "11.5"),
        ("SubtitleAlpha", 0.8, "0.8"),
        ("ControlSize", 28.0, "28"),
        ("ControlGap", 2.0, "2"),
        ("ControlGlyphSize", 14.0, "14"),
        ("TextShadowOffsetY", 1.0, "1"),
        ("TextShadowBlur", 2.0, "2"),
        ("TextShadowAlpha", 0.42, "0.42"),
        ("FontPrimary", "Segoe UI Variable Text", "\"Segoe UI Variable Text\""),
        ("FontFallback", "Segoe UI", "\"Segoe UI\""),
        // Springs
        ("SpringMass", 1.0, "1"),
        ("SpringStiffness", 230.0, "230"),
        ("SpringDamping", 20.0, "20"),
        ("SpringStepsPerSecond", 120.0, "120"),
        // Stretch
        ("StretchZoneFraction", 0.12, "0.12"),
        ("StretchMax", 0.34, "0.34"),
        ("StretchSpeedDivisor", 2300.0, "2300"),
        // Timings
        ("ExpandDelayMs", 320.0, "320"),
        ("ContentsDelayMs", 90.0, "90"),
        ("ContentsStaggerMs", 32.0, "32"),
        ("ContentsFadeInMs", 280.0, "280"),
        ("ContentsRise", 6.0, "6"),
        ("ContentsStartScale", 0.96, "0.96"),
        ("ContentsScaleMs", 440.0, "440"),
        ("ContentsBlurStart", 4.0, "4"),
        ("FadeEaseX1", 0.2, "0.2"),
        ("FadeEaseY1", 0.8, "0.8"),
        ("FadeEaseX2", 0.2, "0.2"),
        ("FadeEaseY2", 1.0, "1"),
        ("MoveEaseX1", 0.2, "0.2"),
        ("MoveEaseY1", 0.9, "0.9"),
        ("MoveEaseX2", 0.25, "0.25"),
        ("MoveEaseY2", 1.12, "1.12"),
        ("DismissFadeMs", 110.0, "110"),
        ("DismissShrinkAtMs", 110.0, "110"),
        ("DismissFlyOutAtMs", 560.0, "560"),
        ("SwitchSwapDelayMs", 120.0, "120"),
        ("HiddenSettlePosition", 0.5, "0.5"),
        ("HiddenSettleVelocity", 5.0, "5"),
        // The darker glass choice
        ("GlassDarkerAlpha", 0.70, "0.70"),
        ("GlassBlurTintAlpha", 0.20, "0.20"),
        // Idle
        ("IdleSeconds", 5.0, "5"),
        // Colours
        ("MediaColor", "#FF4055", "\"#FF4055\""),
        ("FoldersColor", "#FFB81C", "\"#FFB81C\""),
        ("AppsColor", "#1F6FFF", "\"#1F6FFF\""),
        ("VibeColor", "#19E6B3", "\"#19E6B3\""),
        ("BrowserColor", "#E9A0FF", "\"#E9A0FF\""),
        ("TerminalsColor", "#B8F03A", "\"#B8F03A\""),
    ];

    private static IEnumerable<FieldInfo> ConstFields() =>
        typeof(LookConstants).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral);

    [Fact]
    public void Approved_Values_Are_Pinned()
    {
        var source = StripComments(File.ReadAllText(RepoPaths.File("src", "Island.Core", "LookConstants.cs")));
        var fields = ConstFields().ToDictionary(f => f.Name);

        // Nothing may exist in LookConstants without being pinned here, and nothing pinned may be missing.
        Assert.Equal(
            Expected.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal),
            fields.Keys.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(Expected.Length, Expected.Select(e => e.Name).Distinct().Count());

        foreach (var (name, value, literal) in Expected)
        {
            Assert.Equal(value, fields[name].GetRawConstantValue());

            var declaration = new Regex(
                @"\b" + Regex.Escape(name) + @"\s*=\s*" + Regex.Escape(literal) + @"\s*;");
            Assert.True(declaration.IsMatch(source),
                $"LookConstants.cs does not declare '{name} = {literal}'.");
        }
    }

    [Fact]
    public void Pinned_Literal_Matches_Pinned_Value()
    {
        // Guards the table itself: a literal and its value must say the same thing.
        foreach (var (name, value, literal) in Expected)
        {
            if (value is string s) Assert.Equal($"\"{s}\"", literal);
            else if (value is double d) Assert.Equal(d, double.Parse(literal, CultureInfo.InvariantCulture));
            else if (value is int i) Assert.Equal(i, int.Parse(literal, CultureInfo.InvariantCulture));
            else Assert.Fail($"{name}: unexpected type {value.GetType()}");
        }
    }

    private static string StripComments(string code) =>
        Regex.Replace(code, @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);
}
