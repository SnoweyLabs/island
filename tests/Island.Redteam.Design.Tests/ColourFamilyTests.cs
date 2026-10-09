using Island.Core;
using Island.Core.Terminals;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// Colours that stand for different things must be told apart: the three state rings of the Terminals page against the discs they surround, and the page colours against each other and against the ten
/// swatches the settings offer for a page. The distance is OKLab's (Bjorn Ottosson, the same space <see cref="ColorMath"/> mixes in): about 0.02 is the least a person sees, about 0.10 is clearly another colour.
/// </summary>
public class ColourFamilyTests(ITestOutputHelper output)
{
    private static double Delta(Rgb a, Rgb b)
    {
        var (l1, a1, b1) = ColorMath.ToOkLab(a);
        var (l2, a2, b2) = ColorMath.ToOkLab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    private static Rgb Of(HelperColor c) => new(c.R, c.G, c.B);

    private static readonly string[] Swatches = ["#FF4055", "#FFB81C", "#1F6FFF", "#19E6B3", "#E9A0FF", "#FF7AB6", "#FF8A3D", "#3FD0FF", "#8F6BFF", "#F2F2F2"]; // the swatches since WORK-ORDER-13 (Dan's P22): #7CE04A and #B9A7FF were too close to the Terminals lime and to the Browser pink

    [Fact]
    public void The_Working_Ring_Is_The_Colour_Of_The_Antigravity_Disc()
    {
        // A tile of a helper whose own colour is the ring's colour: the ring and the disc inside it are one colour, set apart only by 0.75 of glass.
        var ring = Of(TerminalRing.Working);
        var disc = Of(TerminalConstants.AntigravityColor);
        var d = Delta(ring, disc);
        output.WriteLine($"working ring {ring.ToHex()} against the Antigravity disc {disc.ToHex()}: {d:0.000}");
        Assert.True(d < 0.01, "the working ring and the Antigravity disc are not the same colour any more: the finding has to be looked at again");
    }

    [Fact]
    public void Ring_Against_Disc_Distances_Are_Recorded_For_Every_Helper_And_State()
    {
        var discs = new (string Name, HelperColor Colour)[]
        {
            (TerminalConstants.ClaudeCode, TerminalConstants.ClaudeCodeColor),
            (TerminalConstants.Codex, TerminalConstants.CodexColor),
            (TerminalConstants.Antigravity, TerminalConstants.AntigravityColor),
            (TerminalConstants.Gemini, TerminalConstants.GeminiColor),
            ("unknown helper", TerminalConstants.UnknownHelperColor),
        };
        var rings = new (string Name, HelperColor Colour)[] { ("working", TerminalRing.Working), ("needs you", TerminalRing.NeedsYou), ("finished", TerminalRing.Finished) };
        var below = new List<string>();
        foreach (var disc in discs)
            foreach (var ring in rings)
            {
                var d = Delta(Of(ring.Colour), Of(disc.Colour));
                output.WriteLine($"{disc.Name,-15} disc {Of(disc.Colour).ToHex()}  {ring.Name,-9} ring {Of(ring.Colour).ToHex()}  distance {d:0.000}{(d < 0.10 ? "  CLOSE" : "")}");
                if (d < 0.10) below.Add($"{disc.Name} + {ring.Name}");
            }

        output.WriteLine($"{below.Count} of {discs.Length * rings.Length} pairs are closer than 0.10: {string.Join("; ", below)}");
        Assert.True(below.Count >= 1);
    }

    [Fact]
    public void The_Three_Ring_Colours_Differ_By_Hue_Only_Two_Of_Them_In_Lightness()
    {
        // The three states a full ring can show. Their lightness (OKLab L) is recorded: a reader who cannot tell green from orange has only the arc of "working" to go by.
        foreach (var (name, c) in new[] { ("working", TerminalRing.Working), ("needs you", TerminalRing.NeedsYou), ("finished", TerminalRing.Finished) })
            output.WriteLine($"{name,-10} L {ColorMath.ToOkLab(Of(c)).L:0.000}");
        var needs = ColorMath.ToOkLab(Of(TerminalRing.NeedsYou)).L;
        var finished = ColorMath.ToOkLab(Of(TerminalRing.Finished)).L;
        Assert.InRange(Math.Abs(needs - finished), 0, 0.2);
    }

    [Fact]
    public void The_Terminals_Page_Colour_Against_The_Other_Page_Colours_And_The_Swatches()
    {
        var pages = new[] { LookConstants.MediaColor, LookConstants.FoldersColor, LookConstants.AppsColor, LookConstants.VibeColor, LookConstants.BrowserColor, LookConstants.TerminalsColor };
        var all = pages.Concat(Swatches).Select(h => (Hex: h, Colour: Rgb.FromHex(h))).DistinctBy(x => x.Hex).ToList();
        (string A, string B, double D)? closest = null;
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
            {
                var d = Delta(all[i].Colour, all[j].Colour);
                if (closest is null || d < closest.Value.D) closest = (all[i].Hex, all[j].Hex, d);
                if (d < 0.12) output.WriteLine($"close: {all[i].Hex} and {all[j].Hex}: {d:0.000}");
            }

        var terminals = Rgb.FromHex(LookConstants.TerminalsColor);
        foreach (var s in Swatches.OrderBy(s => Delta(terminals, Rgb.FromHex(s))).Take(2))
            output.WriteLine($"the Terminals lime {LookConstants.TerminalsColor} against the swatch {s}: {Delta(terminals, Rgb.FromHex(s)):0.000}");
        output.WriteLine($"closest pair of all: {closest!.Value.A} and {closest.Value.B}: {closest.Value.D:0.000}");
        Assert.True(closest.Value.D > 0);
    }

    [Fact]
    public void No_Swatch_Offered_For_A_New_Page_Is_Close_To_A_Page_Colour_Or_To_Another_Swatch()
    {
        // Repaired in WORK-ORDER-13 (Dan's P22, design-1-12): every colour a page can have from the settings is at least 0.10 from every other swatch and from every colour a page has.
        var pages = new[] { LookConstants.MediaColor, LookConstants.FoldersColor, LookConstants.AppsColor, LookConstants.VibeColor, LookConstants.BrowserColor, LookConstants.TerminalsColor };
        var all = pages.Concat(Swatches).Distinct().Select(h => (Hex: h, Colour: Rgb.FromHex(h))).ToList();
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                Assert.True(Delta(all[i].Colour, all[j].Colour) >= 0.10, $"{all[i].Hex} and {all[j].Hex}: {Delta(all[i].Colour, all[j].Colour):0.000}");
        var look = typeof(Island.SettingsUi.SettingsView).Assembly.GetType("Island.SettingsUi.Look")!;
        Assert.Equal(Swatches, (string[])look.GetField("Swatches")!.GetValue(null)!);
    }
}
