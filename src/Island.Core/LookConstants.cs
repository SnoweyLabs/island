namespace Island.Core;

/// <summary>
/// Every number and colour of the approved look and motion, in one place.
/// Source: reference/island-previews.html ("Your pick", without blur).
/// Sizes are device-independent pixels, times are milliseconds, alphas are 0..1.
/// LookConstantsTests pins each declaration (name and value together).
/// </summary>
public static class LookConstants
{
    // ---- Shape -------------------------------------------------------
    public const double BallSize = 30;
    public const double CapsuleHeight = 76;
    public const double CapsuleCornerRadius = 38;
    public const double TopGap = 14;
    public const double SpawnHeightAboveEdge = 90;
    public const double MinSizeFraction = 0.94;

    // ---- Capsule width rule: 20 + 36 + 12 + (n*48 - 8) + 14 + 130 + 10 + controls + 20
    public const double WidthLeadingInset = 20;
    public const double WidthChip = 36;
    public const double WidthChipGap = 12;
    public const double WidthItemPitch = 48;
    public const double WidthItemTrailingGap = 8;
    public const double WidthItemsToTextGap = 14;
    public const double WidthTextBlock = 130;
    public const double WidthTextToControlsGap = 10;
    public const double WidthMediaControls = 88;
    public const double WidthOtherControls = 28;
    public const double WidthTrailingInset = 20;

    // ---- Glass body --------------------------------------------------
    public const double GlassGradientTopAlpha = 0.24;
    public const double GlassGradientBottomAlpha = 0.05;
    public const string GlassBaseColor = "#141620";
    public const double GlassBaseAlpha = 0.6;
    public const double BorderWidth = 1;
    public const double BorderAlpha = 0.4;
    public const double TopHighlightWidth = 1;
    public const double TopHighlightAlpha = 0.7;
    public const double BottomGlowAlpha = 0.07;
    public const double BottomGlowOffset = 10;
    public const double BottomGlowCssBlur = 24;
    public const double CategoryGlowAlpha = 0.34;
    public const double CategoryGlowCssBlur = 26;
    public const double ShadowAlpha = 0.32;
    public const double ShadowOffsetY = 18;
    public const double ShadowCssBlur = 50;

    // ---- Rim light ---------------------------------------------------
    public const double RimInset = 0.5;
    public const double BaseRimWidth = 1.6;
    public const double BaseRimAlpha = 0.7;
    public const double ArcFraction = 0.28;
    public const double ArcWidth = 2.6;
    public const double ArcMixCategoryParts = 62;
    public const double ArcMixWhiteParts = 38;
    public const double ArcSpeedPerSecond = 0.18;
    public const double SecondArcFraction = 0.12;
    public const double SecondArcWidth = 2;
    public const double SecondArcAlpha = 0.55;
    public const double SecondArcPhase = 0.5;
    public const double FrontRimBlurCss = 0.6;
    public const double BloomBaseWidth = 7;
    public const double BloomBaseAlpha = 0.45;
    public const double BloomArcWidth = 11;
    public const double BloomBlurCss = 10;
    public const double BloomLayerAlpha = 0.85;
    public const double ColorChangeMs = 350;

    // ---- Contents ----------------------------------------------------
    public const double ChipSize = 36;
    public const double ChipAlpha = 0.72;
    public const double ChipGlyphSize = 18;
    public const double ItemSize = 40;
    public const double ItemGap = 8;
    public const double ItemLabelFontSize = 12;
    public const double ItemGradientAngle = 160;
    public const double ItemTopSaturation = 72;
    public const double ItemTopLightness = 62;
    public const double ItemBottomSaturation = 70;
    public const double ItemBottomLightness = 40;
    public const double ItemFillAlpha = 0.92;
    public const double ItemUnselectedOpacity = 0.74;

    /// <summary>A control that does nothing now (the X before a click has armed it, a media button with nothing to control) is drawn at this strength (Dan's P5, WORK-ORDER-13: it was 0.4, 1.85:1 on the light glass; stronger now, still plainly not ready).</summary>
    public const double DimmedControlOpacity = 0.8;
    public const double SelectedRingWidth = 2;
    public const double SelectedRingAlpha = 0.92;
    public const double SelectedGlowRadius = 14;
    public const double TitleFontSize = 13.5;
    public const double SubtitleFontSize = 11.5;
    public const double SubtitleAlpha = 0.8;
    public const double ControlSize = 28;
    public const double ControlGap = 2;
    public const double ControlGlyphSize = 14;
    public const double TextShadowOffsetY = 1;
    public const double TextShadowBlur = 2;
    public const double TextShadowAlpha = 0.42;
    public const string FontPrimary = "Segoe UI Variable Text";
    public const string FontFallback = "Segoe UI";

    // ---- Motion: springs ---------------------------------------------
    public const double SpringMass = 1;
    public const double SpringStiffness = 230;
    public const double SpringDamping = 20;
    public const double SpringStepsPerSecond = 120;

    // ---- Motion: stretch while the ball travels ------------------------
    public const double StretchZoneFraction = 0.12;
    public const double StretchMax = 0.34;
    public const double StretchSpeedDivisor = 2300;

    // ---- Motion: timings ---------------------------------------------
    public const double ExpandDelayMs = 320;
    public const double ContentsDelayMs = 90;
    public const double ContentsStaggerMs = 32;
    public const double ContentsFadeInMs = 280;
    public const double ContentsRise = 6;
    public const double ContentsStartScale = 0.96;
    public const double ContentsScaleMs = 440;
    public const double ContentsBlurStart = 4;
    public const double FadeEaseX1 = 0.2;
    public const double FadeEaseY1 = 0.8;
    public const double FadeEaseX2 = 0.2;
    public const double FadeEaseY2 = 1;
    public const double MoveEaseX1 = 0.2;
    public const double MoveEaseY1 = 0.9;
    public const double MoveEaseX2 = 0.25;
    public const double MoveEaseY2 = 1.12;
    public const double DismissFadeMs = 110;
    public const double DismissShrinkAtMs = 110;
    public const double DismissFlyOutAtMs = 560;
    public const double SwitchSwapDelayMs = 120;
    public const double HiddenSettlePosition = 0.5;
    public const double HiddenSettleVelocity = 5;

    // ---- The darker glass (a choice offered tonight, 6 Oct 2026; the approved glass is unchanged) ----
    public const double GlassDarkerAlpha = 0.70;

    // ---- The blurred glass: the dark base is much lighter so the blur behind shows (the reference's blurred variant: rgba(16,18,28,.20)).
    public const double GlassBlurTintAlpha = 0.20;

    // ---- Idle --------------------------------------------------------
    public const double IdleSeconds = 5;

    // ---- Category colours --------------------------------------------
    public const string MediaColor = "#FF4055";
    public const string FoldersColor = "#FFB81C";
    public const string AppsColor = "#1F6FFF";
    public const string VibeColor = "#19E6B3";
    public const string BrowserColor = "#E9A0FF";

    /// <summary>The Terminals page (WORK-ORDER-11 section 1): a lime (Claude).</summary>
    public const string TerminalsColor = "#B8F03A";
}
