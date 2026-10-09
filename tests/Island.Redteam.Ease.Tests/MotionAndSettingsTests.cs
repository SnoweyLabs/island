using Island.Core;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>Movement and time: what moves by itself, whether Windows' "show animations", high-contrast and text-size settings are read anywhere, whether anything flashes. Source scans of src/ (never run).</summary>
public class MotionAndSettingsTests
{
    private static IEnumerable<(string Path, string Text)> App => [.. Source.All("src/Island.App"), .. Source.All("src/Island.SettingsUi"), .. Source.All("src/Island.Glass"), .. Source.All("src/Island.Core")];

    [Fact]
    public void Windows_Show_Animations_Is_Read_By_The_Ring_And_By_WindowsAnimations_Which_The_Island_And_The_Settings_Screen_Obey()
    {
        // ease-1-18 (MEDIUM-LOW, a proposal by the one rule's third case: stopping what moves where Windows shows no animations is a proposal unless added by WORK-ORDER-11 or 12; the working ring was
        // added by WORK-ORDER-11 and obeys it). Everything else that moves by itself ignores the setting: the light round the island's edge (all three kinds, and the Vibe breath), the
        // capsule's springs, the opening and closing of the settings screen, the step island's light and the pulsing "Press your keys" in Settings.
        // Repaired in WORK-ORDER-13 (Dan's P1): the island, its light and breath, the settings screen's opening and the working ring all obey it; the setting is read in the ring (TileView) and in WindowsAnimations.
        var readers = App.Where(f => f.Text.Contains("ClientAreaAnimation") || f.Text.Contains("AnimationsEnabled")).Select(f => f.Path).Order().ToList();
        Assert.Equal(["src/Island.App/Visuals/TileView.cs", "src/Island.App/WindowsAnimations.cs"], readers);
    }

    [Fact]
    public void Windows_High_Contrast_And_Text_Size_Are_Read_By_The_Settings_Screen()
    {
        // ease-1-19 (MEDIUM, a proposal). Neither SystemParameters.HighContrast, AccessibilitySettings.HighContrast nor UISettings.TextScaleFactor appears anywhere in src/. Windows' "Make text bigger" slider does
        // not reach WPF text by itself, so a person who set it to 150% finds the island and the settings screen at the size they were. Display scaling (DPI) does work: the layout test at
        // 150%/200%/300% of 1080p shows nothing cut and nothing off the side (the page scrolls).
        // Repaired in WORK-ORDER-13 (Dan's P2): the text size is read in WindowsTextSize (into TextScale) and the settings screen's Look reads both.
        var hits = App.Where(f => f.Text.Contains("HighContrast") || f.Text.Contains("TextScaleFactor")).Select(f => f.Path).Order().ToList();
        Assert.Equal(["src/Island.App/WindowsTextSize.cs", "src/Island.Core/TextScale.cs", "src/Island.SettingsUi/Look.cs"], hits);
    }

    [Fact]
    public void What_Moves_By_Itself_Is_Listed()
    {
        // The files that start something that never ends or runs every frame. A change to this list is a new mover: it must be looked at for the animation setting.
        var movers = App.Where(f => f.Text.Contains("CompositionTarget.Rendering +=") || f.Text.Contains("RepeatBehavior.Forever") || f.Text.Contains("IterationBehavior")).Select(f => f.Path).Order().ToList();
        Assert.Contains("src/Island.App/IslandController.cs", movers);
        Assert.Contains("src/Island.SettingsUi/KeySection.cs", movers);
        Assert.Contains("src/Island.SettingsUi/StepIsland.cs", movers);
        Assert.Contains("src/Island.Glass/MovingLight.cs", movers);
    }

    [Fact]
    public void Nothing_Repeats_Three_Times_A_Second_Or_Faster_By_The_Constants()
    {
        // WCAG 2.3.1 (three flashes). The fastest repeating changes: the light 0.18 turns a second, the working arc 1.4 s a turn, the Vibe breath 2.2 s, the "Press your keys" pulse 1 s (1 Hz,
        // a fade between 100% and 45%, not a flash), the resume blink 0.5 s once. None reaches 3 a second.
        Assert.True(1.0 / TerminalRing.TurnSeconds < 3);
        Assert.True(1.0 / ModeMark.BreathSeconds < 3);
        Assert.True(LookConstants.ArcSpeedPerSecond < 3);
        Assert.Contains("TimeSpan.FromSeconds(1)", Source.Read("src/Island.SettingsUi/KeySection.cs"));
    }

    [Fact]
    public void The_Settings_Screens_Own_Light_Runs_For_As_Long_As_The_Screen_Is_Open()
    {
        // ease-1-20 (LOW, a proposal): the small island at the top of Settings has the same moving light; it runs on every frame of the screen (StepIsland.OnFrame) for as long as the screen is open
        // (minutes, in the first start), and no setting stops it (the Moving light card is for the island itself). Recorded.
        var text = Source.Read("src/Island.SettingsUi/StepIsland.cs");
        Assert.Contains("CompositionTarget.Rendering += OnFrame", text);
        Assert.DoesNotContain("ClientAreaAnimation", text);
    }
}
