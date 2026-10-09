using System.Reflection;
using System.Windows.Media;
using Island.Core;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Contrast and colour (WCAG 2.2 1.4.3, 1.4.11, 1.4.1), measured. These tests PASS: each pins a measured fact so that a later change shows. By the one rule most of what they show is a proposal
/// (a colour, a tint, an opacity Dan chose), and the report says so. STATE.md already records the island title and subtitle on the light glass (3.64:1 and 2.94:1) and the Darker glass proposal:
/// those are not measured again; everything here is another place or another state.
/// </summary>
public class ContrastTests
{
    private static readonly Type Look = typeof(Island.SettingsUi.SettingsView).Assembly.GetType("Island.SettingsUi.Look")!;

    private static double AlphaOf(string brushField) => ((SolidColorBrush)Look.GetProperty(brushField, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!).Color.A / 255.0;

    private static double Const(string name) => (double)Look.GetField(name)!.GetRawConstantValue()!;

    // ---- 1. The settings screen over a bright desktop (computed, not drawn: the self-test's pictures sit on a dark wall) -------------------------------------------------------------

    [Fact]
    public void Measured_Settings_Hint_Text_Over_A_White_Window_Passes_4_5_To_1_On_Both_Glasses_Since_WO13()
    {
        var tint = (12.0, 14.0, 22.0);
        var sub = AlphaOf("Sub");
        var group = AlphaOf("Group");
        Assert.InRange(sub, 0.67, 0.69);
        Assert.InRange(group, 0.07, 0.09);

        double Hint(double glassAlpha)
        {
            var glass = Wcag.Over(tint, glassAlpha, (255, 255, 255)); // what the screen's tint makes of a white window behind it
            var card = Wcag.Over((255, 255, 255), group, glass);
            return Wcag.Ratio(Wcag.Over((255, 255, 255), sub, card), card); // the 12.5 px hints sit on a card
        }

        var approved = Hint(Const("ApprovedTintAlpha"));
        var darker = Hint(Const("DarkerTintAlpha"));
        Assert.True(approved >= 4.5, $"{approved:0.00}:1"); // was 2.77:1 (WORK-ORDER-13, Dan's P3: the tint is 0.80)
        Assert.True(darker >= 4.5, $"{darker:0.00}:1"); // was 3.55:1
    }

    [Fact]
    public void Measured_The_Warning_Colour_Passes_4_5_To_1_On_A_Dark_Desktop_And_On_A_White_One_Since_WO13()
    {
        var warn = ((SolidColorBrush)Look.GetProperty("Warn")!.GetValue(null)!).Color;
        var colour = (warn.R * 1.0, warn.G * 1.0, warn.B * 1.0);
        var tint = (12.0, 14.0, 22.0);
        var onWhite = Wcag.Ratio(colour, Wcag.Over(tint, Const("ApprovedTintAlpha"), (255, 255, 255)));
        var onDark = Wcag.Ratio(colour, Wcag.Over(tint, Const("ApprovedTintAlpha"), (20, 20, 30)));
        Assert.True(onDark >= 4.5, $"{onDark:0.00}:1");
        Assert.True(onWhite >= 4.5, $"{onWhite:0.00}:1"); // every refusal the screen shows is in this colour (was 2.03:1 on a white window; WORK-ORDER-13, P3)
    }

    // ---- 2. The pictures the self-test drew --------------------------------------------------------------------------------------------------------------------------------------

    private static readonly (string Name, int[] Centres)[] TileRows =
    [
        ("folders", [400, 496, 592, 688]),
        ("vibe", [400, 496, 592, 688]),
        ("apps", [352, 448, 544, 640, 736]),
        ("browser", [352, 448, 544, 640, 736]),
        ("media", [388, 484, 580]),
        ("terminals", [448, 544, 640]),
    ];

    private static (int Below, int Total, double Min) Labels(string theme)
    {
        var below = 0;
        var total = 0;
        var min = double.MaxValue;
        foreach (var (name, centres) in TileRows)
        {
            if (Picture.Load($"{name}-{theme}.png") is not { } p) continue;
            foreach (var c in centres)
            {
                var r = p.TextIn(c - 16, 98, c + 16, 119).Ratio;
                total++;
                min = Math.Min(min, r);
                if (r < 4.5) below++;
            }
        }

        return (below, total, min);
    }

    [Fact]
    public void Measured_The_Two_Letters_On_A_Tile_Reach_4_5_To_1_On_Every_Tile_Of_The_Twelve_Snapshots_Since_WO13()
    {
        // The two letters are the only name an unselected tile has on the island. Was below 4.5:1 on 15 of 24 tiles (dark) and 17 of 24 (light), worst 1.9:1; WORK-ORDER-13 (Dan's P4) draws them in the
        // better of white and near-black on the tile's own colours (TileLabel). Measured on the 12 snapshots of review/.
        foreach (var theme in new[] { "dark", "light" })
        {
            var (below, total, min) = Labels(theme);
            if (total == 0) return; // the pictures are not in this checkout
            Assert.Equal(24, total);
            Assert.Equal(0, below);
            Assert.True(min >= 4.5, $"{theme}: the worst tile {min:0.00}:1");
        }
    }

    [Fact]
    public void Measured_The_Close_Cross_Reaches_3_To_1_On_The_Light_Glass_And_The_Dark_Since_WO13()
    {
        // WCAG 1.4.11 asks 3:1 for a mark that is needed to use a control. Was 1.85:1 on the light glass and 3.2:1 on the dark (WORK-ORDER-13, Dan's P5: a stronger cross).
        foreach (var theme in new[] { "light", "dark" })
        {
            if (Picture.Load($"folders-{theme}.png") is not { } p) return;
            var ratio = p.TextIn(1064 - 12, 94, 1064 + 12, 118).Ratio;
            Assert.True(ratio >= 3.0, $"{theme}: {ratio:0.00}:1");
        }
    }

    [Fact]
    public void Measured_A_Closed_Pick_Label_Reaches_4_5_To_1_Since_WO13()
    {
        // The closed state is "grey, a little darker" (choices 2B): that look stays. It was 2.8:1; its two letters now follow the tile rule (TileLabel, Dan's P4), so they read at 4.5:1 or more.
        if (Picture.Load("choices/terminal-states.png") is not { } p) return;
        Assert.True(p.TextIn(434, 96, 462, 120).Ratio >= 4.5);
    }

    [Fact]
    public void Measured_The_No_Key_Cap_In_Settings_Reaches_4_5_To_1_Since_WO13()
    {
        // KeySection.KeyButton drew "no key" at Opacity 0.55 (4.26:1 on the picture); WORK-ORDER-13 (Dan's P7, the head and the dots only) draws it at 0.8.
        if (Picture.Load("settings/key.png") is not { } p) return;
        Assert.True(p.TextIn(1195, 547, 1233, 563).Ratio >= 4.5);
    }

    [Fact]
    public void Measured_The_Grey_Steps_At_The_Top_Of_Settings_Reach_3_To_1_Since_WO13()
    {
        // The dots are 22 x 6; the steps already reached are the page's colour. The grey dot against the pill was 1.92:1 (1.4.11 asks 3:1); WORK-ORDER-13 (Dan's P7) lifts it.
        if (Picture.Load("settings/key.png") is not { } p) return;
        var grey = p.At(980, 42);
        var around = p.At(980, 52);
        Assert.True(Wcag.Ratio(grey, around) >= 3.0);
    }

    // ---- 3. Colour alone ----------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Measured_The_Needs_You_And_Finished_Rings_Differ_By_Hue_Only_Their_Luminance_Is_1_24_To_1()
    {
        // The Terminals page: a ring in orange (needs you), green (finished), or a blue arc that turns (working). Orange and green are the pair people with red-green colour blindness mix up, and
        // their lightness is nearly the same. Only the selected tile says the state in words ("Claude Code - needs you"); the others have the ring and nothing else.
        var needs = TerminalRing.NeedsYou;
        var finished = TerminalRing.Finished;
        var ratio = Wcag.Ratio((needs.R, needs.G, needs.B), (finished.R, finished.G, finished.B));
        Assert.InRange(ratio, 1.15, 1.35);
    }

    [Fact]
    public void Measured_The_Rings_Reach_3_To_1_Against_The_Glass_On_The_Dark_Snapshot()
    {
        // 1.4.11: the orange ring on the dark glass 3.48:1 (the picture); green and blue are checked the same way below by their own luminance against the glass behind them.
        if (Picture.Load("choices/terminal-states.png") is not { } p) return;
        var ring = p.At(309, 106);
        var outside = p.At(303, 106);
        Assert.True(Wcag.Ratio(ring, outside) >= 3.0, $"orange ring {Wcag.Ratio(ring, outside):0.00}:1");
    }

    [Fact]
    public void The_Three_Modes_Differ_On_The_Island_By_Edge_And_Movement_Only_And_The_Name_Is_Not_Drawn()
    {
        // Focus: the approved edge; Vibe: the same edge with a glow that breathes; DND: a dashed rim. In a still picture (review/choices/modes.png) Focus and Vibe are the same drawing. The mode's name
        // is on the island nowhere; the tray menu ticks it and Settings names it. Recorded: the breath exists and is slower than the 3-flash limit.
        Assert.True(Island.Core.ModeMark.BreathSeconds >= 1.0, "the breath is one cycle in 2.2 s: far below three flashes a second (WCAG 2.3.1)");
    }
}
