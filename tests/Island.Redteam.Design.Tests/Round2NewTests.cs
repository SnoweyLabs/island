using System.Text.RegularExpressions;
using Island.Core;
using Island.Core.SettingsEdit;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 2, part 3: what is new in the design area that round 1 did not look at, read from the code and the constants (the states round 1 asked pictures for cannot be drawn by an attacker).</summary>
public class Round2NewTests(ITestOutputHelper output)
{
    // ---- the search field's tail (SearchView.TailThatFits) ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>SearchView.TailThatFits</c> shows the end of a typed text that does not fit (<c>text[^take..]</c> with an ellipsis before it), cutting between UTF-16 units. The rest of the island never cuts half a character
    /// (<c>SearchKeys</c> deletes a whole pair, <c>HandPicks</c>: "never half a character"); here a text whose cut falls inside an emoji starts with a lone low surrogate, which the screen draws as a box or a
    /// question mark. Finding DESIGN-2-3.
    /// </summary>
    [Fact]
    public void Defect_The_End_Of_A_Long_Search_Text_Can_Start_With_Half_Of_An_Emoji()
    {
        var text = Between(Src.Read("Island.App/Visuals/SearchView.cs"), "private static string TailThatFits", "private void Place");
        Assert.True(text.Contains("IsLowSurrogate") || text.Contains("IsSurrogate") || text.Contains("StringInfo") || text.Contains("EnumerateRunes"),
            "TailThatFits cuts the text between UTF-16 units: " + Regex.Match(text, @"var candidate = .*").Value.Trim());
    }

    /// <summary>The same slice on an invented query: every <c>take</c> that starts inside the emoji gives a text that begins with a lone low surrogate (what the loop in TailThatFits tries one after the other, until the width fits).</summary>
    [Fact]
    public void Cutting_A_Text_From_Its_End_By_Units_Starts_With_A_Lone_Low_Surrogate_When_The_Cut_Falls_Inside_An_Emoji()
    {
        var text = "alpha \U0001F600 beta gamma delta"; // an invented query with one emoji
        var halves = new List<int>();
        for (var take = 1; take <= text.Length; take++)
            if (char.IsLowSurrogate(text[^take..][0])) halves.Add(take);
        output.WriteLine($"of {text.Length} possible cuts {halves.Count} start with half of the emoji (take = {string.Join(", ", halves)})");
        Assert.Single(halves);
    }

    // ---- the texts the repairs wrote -----------------------------------------------------------------------------------------------------------------------------

    private static SettingsSession SessionWithMainKeyChanged(string dir)
    {
        var made = SettingsWorld.MadeUpSession(dir);
        var settings = Settings.Defaults with { ShowHide = HotkeyCombo.Parse("Ctrl+Alt+Q") };
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"));
        return new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(made.Pages, PageStoreStatus.Loaded, null),
            new PickStoreLoad(made.Picks, PickStoreStatus.Loaded, null), new Accepting(), () => [], () => true);
    }

    private sealed class Accepting : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            return true;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }

    /// <summary>The question says the keys you set "will be taken away" and counts the main key among them; restoring puts the original main key back, it does not take it away (a main key must exist). Recorded.</summary>
    [Fact]
    public void The_Restore_Keys_Question_Counts_The_Main_Key_As_Taken_Away_But_Restoring_Puts_The_Original_One_Back()
    {
        var dir = SettingsWorld.TempFolder();
        try
        {
            var (keys, question, after, before) = Sta.Run(() =>
            {
                var s = SessionWithMainKeyChanged(dir);
                var k = s.KeysSetByYou;
                var q = SettingsText.RestoreAllKeysQuestion(k);
                var was = s.Settings.ShowHide;
                s.RestoreAllKeys();
                return (k, q, s.Settings.ShowHide, was);
            });
            output.WriteLine($"{keys} key(s) counted; the question: {question}");
            output.WriteLine($"main key before {before}, after {after} (the original is {Settings.Defaults.ShowHide})");
            Assert.Equal(1, keys);
            Assert.Contains("taken away", question);
            Assert.Equal(Settings.Defaults.ShowHide, after);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { /* under this test's own bin folder */ }
        }
    }

    // ---- every second line a pick can have fits the text block --------------------------------------------------------------------------------------------------

    /// <summary>The second line of a pick in every state it can be in (<c>PickItems.Subtitle</c>): "program", "website", "file", "closed", "open", "not found", "N windows", "N tabs" up to four digits, at 11.5 in the 130 wide block.</summary>
    [Fact]
    public void Every_State_Word_Of_A_Pick_Fits_The_Text_Block()
    {
        var lines = new List<string> { "program", "website", "file", "closed", "open", "not found", "9999 windows", "9999 tabs" };
        var worst = Sta.Run(() =>
        {
            var worstWidth = 0.0;
            foreach (var line in lines)
            {
                var w = TextWidth.Of(line, 11.5, System.Windows.FontWeights.Normal);
                worstWidth = Math.Max(worstWidth, w);
                output.WriteLine($"{line,-14} {w,6:0.0} of {LookConstants.WidthTextBlock}");
            }

            return worstWidth;
        });

        Assert.True(worst <= LookConstants.WidthTextBlock - 20);
    }

    // ---- the dots of a pick's windows against the rings around the tile ----------------------------------------------------------------------------------------

    /// <summary>The dots (2 to 5 windows) lie under the tile: their top is 3 below the lowest point of the selected tile's white ring, and 1.75 below the state ring's; the dots' own glow (4 blur, so sigma 2) reaches into both. Recorded in numbers.</summary>
    [Fact]
    public void The_Dots_Of_A_Picks_Windows_Clear_The_Rings_Around_The_Tile_By_A_Few_Dp()
    {
        double tile = LookConstants.ItemSize;
        var dotTop = tile + ChoiceConstants.DotCentreBelowTile - ChoiceConstants.DotSize / 2;
        var selectedRingBottom = tile / 2 + (tile / 2 + LookConstants.SelectedRingWidth); // the white ring's outer edge: 44 across (round 1 measured 87 to 88 px at 2 px to a dp)
        var stateRingBottom = tile / 2 + tile / 2 + 0.75 + 2.5; // WORK-ORDER-11 section 3: a ring just outside the disc, inner edge 0.75 beyond it, 2.5 thick
        output.WriteLine($"state ring ends {stateRingBottom - tile:0.##} below the tile, gap to the dots {dotTop - stateRingBottom:0.##}");
        output.WriteLine($"dots start {dotTop - tile:0.##} below the tile; the selected ring ends {selectedRingBottom - tile:0.##} below it: gap {dotTop - selectedRingBottom:0.##}; five dots are {5 * ChoiceConstants.DotSize + 4 * ChoiceConstants.DotGap} wide in a {tile} tile");
        Assert.True(dotTop - selectedRingBottom is > 0 and < 6);
        Assert.True(5 * ChoiceConstants.DotSize + 4 * ChoiceConstants.DotGap <= tile);
        const double rowCentre = 39; // ContentsLayer.RowCentre (round 1: SourceScanTests)
        var glowBottom = rowCentre - tile / 2 + dotTop + ChoiceConstants.DotSize + ChoiceConstants.DotGlow * 1.5;
        output.WriteLine($"the lowest dot ends at {rowCentre - tile / 2 + dotTop + ChoiceConstants.DotSize:0.#} of the capsule's {LookConstants.CapsuleHeight}, its glow (three sigma) at {glowBottom:0.#}");
        Assert.True(glowBottom < LookConstants.CapsuleHeight, "the dots and their glow are inside the capsule");
    }

    private static string Between(string text, string from, string to)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, "not found: " + from);
        var j = text.IndexOf(to, i, StringComparison.Ordinal);
        return j < 0 ? text[i..] : text[i..j];
    }
}
