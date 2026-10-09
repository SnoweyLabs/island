using System.Text.RegularExpressions;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// The drawing code read as text (this worktree's src/, no stage files): where a movement, a strength or a stroke is written down by hand instead of coming from <see cref="LookConstants"/> or
/// <see cref="ChoiceConstants"/>, and whether the hand-written ones agree with each other (EVALS M: the same thing moves the same way everywhere).
/// </summary>
public class SourceScanTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every animation of the app's own drawing that is not the island's spring or the light's clock, with its duration written out by hand. Four are: the drop zone fades in in 150 ms and out in 120 ms, the lifted
    /// tile springs back in 260 ms with a back-ease of amplitude 0.6 (all in <c>DragView</c>), and the pulse of the key being captured is 1 s with linear keyframes (<c>KeySection</c>). A fifth, the cross-fade of a
    /// pick that opened or closed, takes its 200 ms from <c>ChoiceConstants.ClosedFadeMs</c>. A new hand-written duration turns this red. Finding DESIGN-1-10 records that none of the five is one of the
    /// durations of <see cref="LookConstants"/>, that the zone's 120 and the capsule's dismiss fade (<c>DismissFadeMs</c> 110) are the same kind of movement 10 ms apart, and that a lifted tile does not use the island's spring.
    /// </summary>
    [Fact]
    public void Hand_Written_Durations_Of_Visual_Movement_Are_Exactly_The_Ones_Recorded()
    {
        var found = new List<string>();
        foreach (var (name, text) in Src.Visual())
        {
            if (!name.StartsWith("Island.App/Visuals/", StringComparison.Ordinal) && !name.StartsWith("Island.SettingsUi/", StringComparison.Ordinal)) continue;
            foreach (Match m in Regex.Matches(text, @"TimeSpan\.From(Milliseconds|Seconds)\(\s*([0-9.]+)\s*\)"))
                found.Add($"{Path.GetFileName(name)}: {m.Groups[2].Value} {(m.Groups[1].Value == "Seconds" ? "s" : "ms")}");
        }

        foreach (var f in found.OrderBy(x => x)) output.WriteLine(f);
        string[] expected = ["KeySection.cs: 1 s"]; // WORK-ORDER-13 (Dan's P22): the drag's three durations are ChoiceConstants now
        Assert.Equal(expected, found.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void The_Fade_Durations_Of_The_Island_Are_Recorded_With_Their_Names()
    {
        var table = new (string Name, double Ms)[]
        {
            ("capsule dismiss fade (LookConstants.DismissFadeMs)", LookConstants.DismissFadeMs),
            ("drop zone fade out (ChoiceConstants.DropZoneFadeOutMs)", ChoiceConstants.DropZoneFadeOutMs),
            ("drop zone fade in (ChoiceConstants.DropZoneFadeInMs)", ChoiceConstants.DropZoneFadeInMs),
            ("closed tile cross-fade (ChoiceConstants.ClosedFadeMs)", ChoiceConstants.ClosedFadeMs),
            ("second row fade in (ChoiceConstants.RowFadeInMs)", ChoiceConstants.RowFadeInMs),
            ("contents fade in (LookConstants.ContentsFadeInMs)", LookConstants.ContentsFadeInMs),
            ("colour change (LookConstants.ColorChangeMs)", LookConstants.ColorChangeMs),
        };
        foreach (var (n, ms) in table) output.WriteLine($"{ms,6:0} ms  {n}");
        output.WriteLine($"{table.Select(t => t.Ms).Distinct().Count()} different times for movements that all change an opacity or a colour");
        Assert.Equal(5, table.Select(t => t.Ms).Distinct().Count()); // six until WORK-ORDER-13: the drop zone's 120 is the capsule's 110 now (Dan's P22)
    }

    /// <summary>The lifted tile springs back with a back-ease in 260 ms; every other movement of a shape on the island follows the spring of <see cref="LookConstants"/> (mass, stiffness, damping). Recorded as written.</summary>
    [Fact]
    public void The_Lifted_Tile_Comes_Back_By_An_Ease_Not_By_The_Island_Spring()
    {
        var drag = Src.Read("Island.App/Visuals/DragView.cs");
        Assert.Contains("new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = ChoiceConstants.LiftedReturnAmplitude }", drag);
        Assert.DoesNotContain("new Spring", drag);
        Assert.DoesNotContain("Spring.At", drag);
        Assert.DoesNotContain("LookConstants.Spring", drag);
    }

    /// <summary>The key being captured pulses with linear keyframes; the reference's <c>pulse</c> animation is 1 s, 50% at 0.45, with the default (ease) timing between the keyframes. Recorded.</summary>
    [Fact]
    public void The_Capture_Pulse_Is_Linear_Where_The_Reference_Eases()
    {
        var key = Src.Read("Island.SettingsUi/KeySection.cs");
        Assert.Contains("LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.5))", key);
        var reference = Src.Reference("island-setup-previews.html");
        if (reference is null) return;
        Assert.Contains("@keyframes pulse{50%{opacity:.45}}", reference);
        Assert.Contains("animation:pulse 1s infinite", reference);
    }

    // ---- strokes and strengths -------------------------------------------------------------------------------------

    [Fact]
    public void The_Glyph_Stroke_Widths_Are_Recorded_And_The_Terminal_Glyph_Is_The_Thinnest()
    {
        var icons = Src.Read("Island.App/Visuals/Icons.cs");
        var widths = Regex.Matches(icons, @"\[""(?<name>\w+)""\] = new\(\[[^\]]*\], \[(?<d>[^\]]+)\], (?<w>[0-9.]+), (?<round>true|false)\)")
            .ToDictionary(m => m.Groups["name"].Value, m => (Width: double.Parse(m.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture), Round: m.Groups["round"].Value == "true"));
        foreach (var (name, (w, round)) in widths.OrderBy(x => x.Value.Width)) output.WriteLine($"{name,-9} stroke {w,3} on a 16 grid, round ends {round}");
        // Repaired in WORK-ORDER-13 (Dan's P22): the Terminals glyph is 1.6, the same as the close cross; the thinnest is now the globe
        Assert.Equal(1.6, widths["terminal"].Width);
        Assert.True(widths.Values.All(x => x.Width >= 1.3 || x.Width == 0), "a glyph is thinner than the globe");
        // The family's weights as the reference draws them (previews ICON): close 1.6, term 1.7, globe 1.3.
        var reference = Src.Reference("island-previews.html");
        if (reference is null) return;
        Assert.Contains("stroke-width=\"1.7\"", reference);
        Assert.Contains("stroke-width=\"1.3\"", reference);
    }

    /// <summary>Every dimmed control (a media button with nothing to control, the X that does nothing now, the Terminals page's X) is drawn at one strength, 0.4, written in three places; they agree.</summary>
    [Fact]
    public void Every_Dimmed_Control_Is_Drawn_At_The_Same_Strength()
    {
        // WORK-ORDER-13 (Dan's P5): one constant, LookConstants.DimmedControlOpacity (0.8, was 0.4 written in three places); every dimmed control names it.
        var values = new List<(string File, string Value)>();
        foreach (var f in new[] { "Island.App/Visuals/ContentsLayer.cs", "Island.App/Visuals/PillView.cs" })
            foreach (Match m in Regex.Matches(Src.Read(f), @"\.Opacity = [^;]*?(?<v>LookConstants\.DimmedControlOpacity|0\.\d+)[^;]*;"))
                values.Add((Path.GetFileName(f), m.Groups["v"].Value));

        foreach (var (file, v) in values) output.WriteLine($"{file}: {v}");
        Assert.True(values.Count >= 3, "found: " + string.Join(", ", values.Select(v => v.File + " " + v.Value)));
        Assert.All(values, v => Assert.Equal("LookConstants.DimmedControlOpacity", v.Value));
        Assert.Equal(0.8, LookConstants.DimmedControlOpacity);
    }

    /// <summary>The vertical centre of the row is written twice (<c>ContentsLayer.RowCentre</c> and <c>SearchView.Centre</c>) and must be the same formula; and the capsule's own centre is one dp lower than the middle of its 76 (39 against 38).</summary>
    [Fact]
    public void The_Row_Centre_Is_The_Same_In_The_Capsule_And_In_Search_And_Sits_One_Dp_Below_The_Middle()
    {
        const string formula = "LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth";
        Assert.Contains(formula, Src.Read("Island.App/Visuals/ContentsLayer.cs"));
        Assert.Contains(formula, Src.Read("Island.App/Visuals/SearchView.cs"));
        output.WriteLine($"row centre {LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth} dp from the capsule's top; the middle of {LookConstants.CapsuleHeight} is {LookConstants.CapsuleHeight / 2}");
    }

    /// <summary>The second row's centre is 74 + 1 + 37 - 1 = 111 from the top; the first row's is 39: 72 apart while a row is 74 high.</summary>
    [Fact]
    public void The_Two_Rows_Centres_Are_Seventy_Two_Apart_Where_A_Row_Is_Seventy_Four_High()
    {
        var first = LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth;
        var second = LookConstants.BorderWidth + ChoiceConstants.RowHeight + ChoiceConstants.RowHeight / 2 - 1;
        output.WriteLine($"first row centre {first}, second row centre {second}, apart {second - first}, row height {ChoiceConstants.RowHeight}");
        Assert.Equal(72, second - first);
    }

    [Fact]
    public void The_Glass_Base_Colour_Is_The_Same_Rgb_Wherever_It_Is_Written_By_Hand()
    {
        // 20,22,32 is #141620 (LookConstants.GlassBaseColor). Written as numbers in IslandHost (the ball), StepIsland and TrayIcon; the dialog's own is 18,20,30 and the settings tint 12,14,22 (both from the reference).
        Assert.Equal("#141620", LookConstants.GlassBaseColor);
        Assert.Contains("0x99, 20, 22, 32", Src.Read("Island.App/IslandHost.cs"));
        Assert.Contains("Look.Solid(20, 22, 32, 0.55)", Src.Read("Island.SettingsUi/StepIsland.cs"));
        var reference = Src.Reference("island-setup-previews.html");
        if (reference is null) return;
        Assert.Contains("rgba(20,22,32,.55)", reference); // .c-top
    }
}
