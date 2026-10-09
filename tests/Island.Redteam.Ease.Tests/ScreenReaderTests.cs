using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>What a screen reader gets from the settings screen (the names and roles WPF's automation peers give) and from the island itself.</summary>
public class ScreenReaderTests
{
    [Fact]
    public void Every_Button_And_Field_Of_Settings_And_The_First_Start_Has_A_Name()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            foreach (var setup in new[] { false, true })
            {
                var view = fixture.NewView(setup);
                foreach (var section in setup ? Tree.SetupSections : Tree.FullSections)
                {
                    view.Section = section;
                    Tree.Layout(view, 1920, 1080);
                    foreach (var button in Tree.Of<Button>(view))
                        Assert.False(string.IsNullOrWhiteSpace(Tree.AccessibleName(button)), $"{section}: button {Tree.FocusKeyOf(button)} has no name");
                    foreach (var box in Tree.Of<TextBox>(view))
                        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(box)), $"{section}: field {Tree.FocusKeyOf(box)} has no name");
                }
            }
        });
    }

    [Fact]
    public void No_Two_Buttons_On_One_Screen_Share_A_Name()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            foreach (var section in Tree.FullSections)
            {
                view.Section = section;
                Tree.Layout(view, 1920, 1080);
                var names = Tree.Of<Button>(view).Select(Tree.AccessibleName).ToList();
                Assert.True(names.Count == names.Distinct().Count(), $"{section}: " + string.Join(" | ", names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
            }
        });
    }

    [Fact]
    public void A_State_That_Looks_Like_A_Colour_Or_A_Fill_Is_Also_In_The_Name_Of_Its_Control()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Mode;
            Tree.Layout(view, 1920, 1080);
            Assert.Contains("on", Tree.AccessibleName(Tree.ButtonKeyed(view, "mode:Vibe")).Split('.')[0]);
            view.Section = SettingsSection.Glass;
            Tree.Layout(view, 1920, 1080);
            Assert.Contains("chosen", Tree.AccessibleName(Tree.ButtonKeyed(view, "glass:Approved")));
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            Assert.Contains("is on", Tree.AccessibleName(Tree.ButtonKeyed(view, "general:pill")));
            Assert.Contains("is off", Tree.AccessibleName(Tree.ButtonKeyed(view, "general:startup")));
            view.Section = SettingsSection.OnTheIsland;
            Tree.Layout(view, 1920, 1080);
            Assert.Contains("on the island", Tree.AccessibleName(Tree.ButtonKeyed(view, "chip:program:discord")));
        });
    }

    /// <summary>
    /// ease-1-10 (LOW, a safe improvement). The steps at the top of the screen are buttons named "Step 3 of 8: On the island"; the step the person is on is told only by the colour of
    /// the dots (the ones up to it are coloured, the rest grey). A screen reader says the same words for the current step and for the others. Expected: the current step's name says so
    /// ("Step 3 of 8: On the island, current").
    /// </summary>
    [Fact]
    public void Defect_The_Current_Step_Is_Named_As_The_Current_One()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.OnTheIsland;
            Tree.Layout(view, 1920, 1080);
            var current = Tree.AccessibleName(Tree.ButtonKeyed(view, "step:2"));
            Assert.Contains("current", current, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// ease-1-11 (LOW, a safe improvement). <c>Parts.Sentence</c> builds a line of text word by word: one TextBlock for each word and one Border for each key cap, in a WrapPanel. The "Your key"
    /// screen (nine lines of this) holds 114 one-word text elements (measured); a screen reader that walks the content reads each word as its own item, and the lines are never read as sentences.
    /// Expected: one element for a line (a TextBlock with Runs and InlineUIContainers for the caps), or an AutomationProperties.Name for the line on a container that has a peer.
    /// </summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_A_Line_Of_Key_Help_Is_One_Element_And_Not_One_For_Every_Word()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            var oneWord = Tree.Of<TextBlock>(view).Count(t => t.Text.Length > 0 && !t.Text.Contains(' '));
            Assert.True(oneWord < 40, $"{oneWord} one-word text elements on the Your key screen");
        });
    }

    /// <summary>
    /// ease-1-1 (HIGH, a safe improvement: nothing seen changes). The island itself (the capsule, its tiles, the pill, the search, the notice, the second row) is drawn by custom elements
    /// (<c>Layer</c>, <c>TileView</c>, <c>PillView</c>, ... all Canvas or FrameworkElement) and the app has no AutomationProperties, no AutomationPeer and no window title anywhere outside the
    /// settings screen. A screen reader is shown an unnamed window with no content: it cannot tell which tile is selected, what is playing, that a notice has appeared, or that the island
    /// is there at all. The island's own keys (Left, Right, Enter, Tab, Delete) work, so a keyboard-only sighted person can use it; a blind person has no island.
    /// Smallest repair: an <c>OnCreateAutomationPeer</c> on <c>IslandView</c>'s root giving the window a name ("Island") and, for the selected tile, a name and its page ("Spotify, Media, selected");
    /// a live-region peer text for the notice. Expected here: some automation in the island's own drawing code.
    /// </summary>
    [Fact]
    public void Defect_The_Island_Exposes_Something_To_A_Screen_Reader()
    {
        var hits = Source.All("src/Island.App")
            .Where(f => f.Text.Contains("AutomationProperties.") || f.Text.Contains("OnCreateAutomationPeer") || f.Text.Contains("AutomationPeer"))
            .Where(f => !f.Path.EndsWith("SettingsStage.cs", StringComparison.Ordinal)) // the self-test's reader of the settings screen, not the island
            .Select(f => f.Path)
            .ToList();
        Assert.True(hits.Count > 0, "no file of Island.App names anything for a screen reader");
    }

    /// <summary>
    /// ease-1-12 (LOW, a safe improvement). The settings screen's window (<c>ScreenWindow</c>) and the island's (<c>OverlayWindow</c>) set no Title and no AutomationProperties.Name. When
    /// the screen opens and takes the keyboard a screen reader announces the window by its name and has none to say.
    /// </summary>
    [Fact]
    public void Defect_The_Settings_Window_Has_A_Name()
    {
        var text = Source.Read("src/Island.App/SettingsScreen.cs");
        Assert.True(text.Contains("Title =") || text.Contains("AutomationProperties.SetName") || text.Contains("Title="), "ScreenWindow sets no Title");
    }

    [Fact]
    public void The_Notice_Line_Is_A_Live_Region_But_Is_A_New_Element_After_Every_Change()
    {
        // ease-1-16 (LOW, UNVERIFIED at run time). Refusals ("That combination uses the Windows key...") are written into a TextBlock marked LiveSetting=Polite, but every change rebuilds the whole
        // section, so the TextBlock is created anew with its text already in it. Whether Narrator announces a live region that is created with its text, and whether WPF raises LiveRegionChanged
        // for a TextBlock at all, is not stated on the Microsoft Learn pages for AutomationProperties.LiveSetting (read 2026-10-07: they give only the field's definition): UNVERIFIED.
        // After a refused key the keyboard is also put back on the key's button, whose name is read instead. This test records the fact that makes the doubt: a different element each time.
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            TextBlock Notice() => Tree.Of<TextBlock>(view).Single(t => Tree.FocusKeyOf(t) == "notice");
            var before = Notice();
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(before));
            Tree.Click(view, "key:all");
            Assert.NotSame(before, Notice());
        });
    }

    [Fact]
    public void The_Page_Name_Fields_All_Share_One_Name_And_The_Page_Is_In_Their_Value()
    {
        // Held, with a note: each page's name box is named "Name of the page" (7 of them with the same name); the page's own name is the box's text, which a screen reader reads as the value.
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Pages;
            Tree.Layout(view, 1920, 1080);
            var boxes = Tree.Of<TextBox>(view).ToList();
            Assert.Equal(fixture.Session.Pages.Pages.Count, boxes.Count);
            Assert.All(boxes, b => Assert.Equal("Name of the page", AutomationProperties.GetName(b)));
            Assert.Equal(boxes.Select(b => b.Text), fixture.Session.Pages.Pages.Select(p => p.Name));
        });
    }
}
