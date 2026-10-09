using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>What the settings screen and the first-start steps draw, read from their laid-out visual tree (never shown, never rendered).</summary>
public class SettingsDefectTests(ITestOutputHelper output)
{
    private static T WithView<T>(bool setup, Func<SettingsView, Grid, SettingsSession, T> body)
    {
        var dir = SettingsWorld.TempFolder();
        try
        {
            return Sta.Run(() =>
            {
                var session = SettingsWorld.MadeUpSession(dir);
                var view = new SettingsView(session, setup);
                view.FreezeAnimations(1.0);
                var stage = SettingsWorld.Stage(view);
                return body(view, stage, session);
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { /* under this test's own bin folder */ }
        }
    }

    /// <summary>
    /// <c>Parts.Sentence</c> turns a piece that starts with "[" and ends with "]" into a key cap; it splits on spaces, so "[Delete]," (the first word of the line "[Delete], then [Delete] again, removes the
    /// selected pick", <c>SettingsText.IslandKeys</c>) is not a cap: the person reads the brackets. Seen in review/settings/key.png and review/setup/key.png ("[Delete], then (Delete) again") on the Key step of
    /// the settings and of the first start. Finding DESIGN-1-01. (The repair changes those two pictures, so by the one rule's first case the class is the main session's call.)
    /// </summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_A_Key_Name_Followed_By_A_Comma_Is_Drawn_With_Its_Brackets()
    {
        foreach (var setup in new[] { false, true })
        {
            var leaked = WithView(setup, (view, stage, _) =>
            {
                view.Section = SettingsSection.Key;
                SettingsWorld.Layout(stage);
                return SettingsWorld.Descendants(view).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Contains('[') || t.Contains(']')).ToList();
            });
            Assert.True(leaked.Count == 0, $"{(setup ? "first start" : "settings")}: text drawn with its key-cap markers: " + string.Join(" | ", leaked));
        }
    }

    /// <summary>Every single-line piece of text of every step is as wide as the box it is drawn in needs: none is cut by its box.</summary>
    [Fact]
    public void No_Single_Line_Text_Of_Any_Step_Is_Wider_Than_Its_Box()
    {
        var cut = new List<string>();
        var looked = 0;
        foreach (var setup in new[] { false, true })
            foreach (var section in setup
                         ? new[] { SettingsSection.Welcome, SettingsSection.Key, SettingsSection.Practice, SettingsSection.Pages, SettingsSection.OnTheIsland, SettingsSection.Mode }
                         : Enum.GetValues<SettingsSection>().Where(x => x is not (SettingsSection.Welcome or SettingsSection.Practice)))
            {
                var found = WithView(setup, (view, stage, _) =>
                {
                    view.Section = section;
                    SettingsWorld.Layout(stage);
                    var list = new List<string>();
                    foreach (var t in SettingsWorld.Descendants(view).OfType<TextBlock>())
                    {
                        if (t.Text.Length == 0 || t.TextWrapping != TextWrapping.NoWrap || !SettingsWorld.IsShown(t)) continue;
                        list.Add("looked");
                        var wanted = TextWidth.Of(t.Text, t.FontSize, t.FontWeight, t.FontFamily.Source);
                        if (t.ActualWidth + 1 < wanted) list.Add($"{(setup ? "setup" : "settings")}/{section}: \"{t.Text}\" needs {wanted:0.0}, box {t.ActualWidth:0.0}");
                    }

                    return list;
                });
                looked += found.Count(f => f == "looked");
                cut.AddRange(found.Where(f => f != "looked"));
            }

        output.WriteLine($"{looked} single-line texts looked at");
        Assert.True(looked > 100, "the test looked at too few texts to mean anything");
        foreach (var c in cut) output.WriteLine(c);
        Assert.True(cut.Count == 0, "cut texts: " + string.Join("; ", cut));
    }

    /// <summary>
    /// In the open "Add…" panel of a page the "Close" link sits in the panel's own 16 of padding (and the link's own 8), while "Add…" and "Restore starter list" in the head row above sit in a margin of 8 (and the
    /// link's 8): the right edges of the words are 8 apart (review/settings/add-panel.png: 1287 against 1294). Finding DESIGN-1-07; a repair moves the "Close" link and so changes the picture.
    /// </summary>
    [Fact]
    public void The_Close_Link_Of_The_Add_Panel_Is_Eight_Pixels_Inside_The_Other_Links()
    {
        var (close, restore) = WithView(false, (view, stage, _) =>
        {
            view.ShowAddPanel(PageIds.Media);
            SettingsWorld.Layout(stage);
            double RightOfWords(string key)
            {
                var button = SettingsWorld.Descendants(view).OfType<Button>().First(b => SettingsWorld.KeyOf(b) == key);
                var words = SettingsWorld.Descendants(button).OfType<TextBlock>().First();
                return SettingsWorld.BoxOf(words, stage).Right;
            }

            return (RightOfWords("add:close:" + PageIds.Media), RightOfWords("restore:" + PageIds.Media));
        });
        output.WriteLine($"right edge of the words: Close {close:0.0}, Restore starter list {restore:0.0}");
        Assert.InRange(restore - close, -1, 1); // repaired in WORK-ORDER-13 (Dan's P22): the Close link is flush right with the other links
    }

    /// <summary>
    /// A wrapped sentence whose last line is one or two short words ("... never makes a helper / wait.", "... see its / README.", "back here.", "From 2 / to 60 seconds."): the width of the last line is read from the
    /// position of the text's last character after layout. Recorded for every step of the settings and of the first start; the finding (DESIGN-1-08) is about the ones under 15% of the box.
    /// </summary>
    [Fact]
    public void Wrapped_Sentences_That_End_On_A_Line_Under_Fifteen_Percent_Of_The_Box_Are_Counted()
    {
        var widows = new List<string>();
        var wrapped = 0;
        foreach (var setup in new[] { false, true })
            foreach (var section in setup
                         ? new[] { SettingsSection.Welcome, SettingsSection.Key, SettingsSection.Practice, SettingsSection.Pages, SettingsSection.OnTheIsland, SettingsSection.Mode }
                         : Enum.GetValues<SettingsSection>().Where(x => x is not (SettingsSection.Welcome or SettingsSection.Practice)))
            {
                var found = WithView(setup, (view, stage, _) =>
                {
                    view.Section = section;
                    SettingsWorld.Layout(stage);
                    var list = new List<(bool Wrapped, string? Widow)>();
                    foreach (var t in SettingsWorld.Descendants(view).OfType<TextBlock>())
                    {
                        if (t.Text.Length < 20 || t.TextWrapping != TextWrapping.Wrap || !SettingsWorld.IsShown(t) || t.ActualWidth < 100) continue;
                        var lineHeight = t.FontSize * 1.37; // Segoe UI's own line height is about 1.33 to 1.37 of the size
                        if (t.ActualHeight < lineHeight * 1.6) continue; // one line
                        var last = t.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                        var share = last.Right / t.ActualWidth;
                        list.Add((true, share < 0.15 ? $"{(setup ? "setup" : "settings")}/{section}: \"{(t.Text.Length > 50 ? "..." + t.Text[^30..] : t.Text)}\" last line {share:P0} of the box" : null));
                    }

                    return list;
                });
                wrapped += found.Count;
                widows.AddRange(found.Where(f => f.Widow is not null).Select(f => f.Widow!));
            }

        foreach (var w in widows) output.WriteLine(w);
        output.WriteLine($"{widows.Count} of {wrapped} wrapped sentences end on a line under 15% of the box");
        Assert.True(wrapped >= 15, "the test found too few wrapped sentences to mean anything");
    }
}
