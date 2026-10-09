using System.Windows.Controls;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>The words the app shows a person who has never seen it: every refusal says what happened, why and what to do; no word is false.</summary>
public class TextTests
{
    private static Refusal Filled(Refusal r) => r.With("{combo}", "Ctrl+Alt+J").With("{category}", "Media").With("{name}", "Alpha").With("<name>", "Alpha").With("<n>", "2").With("<scene>", "Mix").With("<names>", "Alpha, Beta");

    [Fact]
    public void Every_Registered_Refusal_Has_Three_Parts_Each_A_Full_Sentence_And_No_Placeholder_Is_Left()
    {
        Assert.True(Refusals.All.Count >= 13); // 14 until LIGHT_UNAVAILABLE went with the Moving light setting (WORK-ORDER-13)
        foreach (var raw in Refusals.All)
        {
            var r = Filled(raw);
            foreach (var part in new[] { r.WhatHappened, r.Why, r.NextAction })
            {
                Assert.False(string.IsNullOrWhiteSpace(part), $"{r.Code} has an empty part");
                Assert.EndsWith(".", part.TrimEnd());
                Assert.DoesNotContain("{", part);
                Assert.DoesNotContain("<", part);
            }
        }
    }

    [Fact]
    public void Every_Registered_Refusal_Fits_The_255_Characters_Windows_Shows_Of_A_Balloon()
    {
        foreach (var raw in Refusals.All)
            Assert.True(Filled(raw).Message.Length <= 255, $"{raw.Code} is {Filled(raw).Message.Length} characters");
    }

    [Fact]
    public void Every_Refusal_Code_Is_Unique_And_Spoken_In_Capitals()
    {
        var codes = Refusals.All.Select(r => r.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-Z_]+$", c));
    }

    [Fact]
    public void The_Settings_Screens_Own_Refusals_Have_Three_Parts_Too()
    {
        var combo = HotkeyCombo.Parse("Ctrl+Alt+J");
        foreach (var r in new[]
                 {
                     SettingsText.KeyHasWindowsKey, SettingsText.KeyUnknown, SettingsText.KeyUnsafe("It would stop Ctrl+Alt+Delete working."), SettingsText.KeyTakenInside(combo, "Media", "Apps"),
                     SettingsText.KeyTakenByAnotherProgram(combo), SettingsText.KeyRefusedByWindows(combo, 5), SettingsText.MainKeyCannotBeEmpty, SettingsText.KeyNotSaved,
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(r.WhatHappened) || string.IsNullOrWhiteSpace(r.Why) || string.IsNullOrWhiteSpace(r.NextAction), r.Code);
        }
    }

    /// <summary>
    /// ease-1-7 (MEDIUM; by the one rule's text case: "a refusal that lacks one of its three parts"). The Glass screen's note, and the refusal of choosing Blur, say only
    /// "Blur is not available on this computer right now." There is a registered refusal for the same thing, BLUR_UNAVAILABLE, with all three parts (what: not available; why: Island uses the
    /// Approved glass; what to do: turn on Transparency effects in Windows Settings and pick Blur again). The settings text gives the person no reason and no step. The Moving light card
    /// next to it already uses the register's words (LIGHT_UNAVAILABLE). Smallest repair: show <c>Refusals.BlurUnavailable.Message</c> in <c>GlassSection</c> and in <c>SettingsSession.SetGlass</c>.
    /// </summary>
    [Fact]
    public void Defect_The_Blur_Note_In_Settings_Gives_The_Reason_And_What_To_Do()
    {
        Assert.Equal(Refusals.BlurUnavailable.Message, SettingsText.BlurUnavailable);
    }

    /// <summary>
    /// ease-1-8 (LOW; a text that says something false, by the one rule). The Glass card for the default glass reads "The glass you chose: dark and clear." A person who has never opened Settings
    /// chose nothing; the words are Dan's, written for the day he chose it. Expected: a description true for everyone ("The standard glass: dark and clear."). The NAME "Approved" is the
    /// same kind of word (approved by whom?) and is a proposal to rename.
    /// </summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_The_Approved_Glass_Card_Does_Not_Say_You_Chose_It_To_Someone_Who_Did_Not()
    {
        var approved = GlassChoice.All.Single(g => g.Kind == GlassKind.Approved);
        Assert.DoesNotContain("you chose", approved.Description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ease-1-9 (LOW; the key list in Settings, "Your key"). <c>Parts.Sentence</c> turns a word that begins with [ and ends with ] into a key cap; "[Delete]," ends with a comma, so
    /// the first Delete of "[Delete], then [Delete] again, removes the selected pick" is drawn as the characters "[Delete]," (visible in review/settings/key.png, the line above "Esc closes
    /// the second row first"). Expected: a cap. Note: repairing it changes a pixel of a picture the self-test draws, which by the one rule's first case makes it a proposal.
    /// </summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_No_Raw_Square_Brackets_Are_Drawn_On_The_Key_Screen()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            var raw = Tree.Of<TextBlock>(view).Where(t => t.Text.Contains('[') || t.Text.Contains(']')).Select(t => t.Text).ToList();
            Assert.True(raw.Count == 0, "drawn with brackets: " + string.Join(" | ", raw));
        });
    }

    [Fact]
    public void The_Words_Of_The_Refusals_And_Settings_Name_Things_As_The_Screen_Does()
    {
        // Held: the Settings screen's own words for the keys, pages and modes are the ones a person sees on the island (Ctrl+Q's name, nine pages, the three modes).
        Assert.Equal("Show or hide the island", SettingsText.MainActionName);
        Assert.Contains("nine pages", SettingsText.PageLimit);
        Assert.Equal(3, Enum.GetValues<Mode>().Length);
        Assert.Equal("Ctrl+Q", Settings.Defaults.ShowHide.ToString());
    }

    /// <summary>
    /// What a person who has never seen the app meets, by word (measured here, discussed in the report; proposals, not defects): "pick" is the app's own name for a thing on the island and is
    /// used in the Settings words and the first-start words without ever being explained; "DND", "hook", "README", "extension folder", "starter list", "Approved glass" and "As before" are
    /// words that need history or a developer. The test records where the first-start steps use them, so a later change can be seen.
    /// </summary>
    [Fact]
    public void Jargon_In_The_First_Start_Steps_Is_Recorded()
    {
        var firstStart = string.Join(" ", FirstStart.Steps.SelectMany(s => new[] { s.Name, s.Title, s.Sub }));
        Assert.DoesNotContain("pick ", firstStart.Replace("A few things are picked", "")); // the first start says "picked for you" (a verb), never the noun
        Assert.Contains("Tap to switch", firstStart); // a touch word for a screen driven by mouse and keyboard
        Assert.Contains("+ button", firstStart);
        Assert.Contains("Mode", firstStart);
    }

    [Fact]
    public void The_List_Of_The_Islands_Keys_Does_Not_Say_That_Typing_A_Letter_Starts_The_Search()
    {
        // ease-1-22 (LOW, a proposal: wording). The one place that lists the island's keys (Settings, "Your key", SettingsText.IslandKeys plus WhileOpenHint) names Left, Right, Down, Up, Enter,
        // Shift+Enter, Tab, Space, Delete and Esc and the number keys; it does not say that a letter typed while the island is open starts a search (the search shows "Type to search" only once open).
        // A feature found by luck. Recorded.
        var all = string.Join(" ", SettingsText.IslandKeys) + " " + SettingsText.WhileOpenHint;
        Assert.DoesNotContain("search", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Browser_Add_On_Row_Says_How_To_Load_It_And_Has_A_Button_That_Opens_The_Folder_Since_WO13()
    {
        // ease-1-13 (MEDIUM, a proposal: wording). The only words about how to get the add-on are "Load it from the extension folder - see its README." The app has no button that opens the folder or the
        // file; a person who is not a developer is told to do something with no way to start. Recorded as a fact so a later change shows.
        // Repaired in WORK-ORDER-13 (Dan's P11): the words say how to load it and there is a button that opens the folder.
        Assert.DoesNotContain("README", AddonText.Hint);
        Assert.Contains("Load unpacked", AddonText.Hint);
        Assert.Equal("Open the add-on folder", AddonText.OpenFolderButton);
    }

    [Fact]
    public void The_Hotkey_Taken_Refusal_Sends_The_Person_To_The_Key_Screen_And_Not_To_The_Settings_File_Since_WO13()
    {
        // ease-1-14 (LOW, a proposal: wording). The register's HOTKEY_TAKEN says "tray icon -> Open settings file, change it, restart Island". Since WORK-ORDER-4 the screen changes the main key
        // and a page's key with no restart (SettingsText.KeyTakenByAnotherProgram says "Press a different combination."). The balloon at start is the one place the old advice is shown.
        // Repaired in WORK-ORDER-13 (Dan's P12): the balloon at start says the same as the screen does.
        Assert.Contains("in Settings, Key", Refusals.HotkeyTaken.NextAction);
        Assert.DoesNotContain("settings file", Refusals.HotkeyTaken.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Settings_Unreadable_Refusal_Says_Where_The_File_Is_And_Asks_For_No_Editing_Since_WO13()
    {
        // ease-1-15 (LOW, a proposal: wording). "Fix the file, or delete it to start again from the defaults." names neither the file's place nor how to open it (the tray has "Open settings file", which is
        // not said here). A person who is not a developer cannot act on it.
        // Repaired in WORK-ORDER-13 (Dan's P12): plain steps and the place of the file; nothing to fix by hand.
        Assert.Contains("AppData", Refusals.SettingsUnreadable.NextAction);
        Assert.DoesNotContain("Fix the file", Refusals.SettingsUnreadable.NextAction);
        Assert.True(Refusals.SettingsUnreadable.Message.Length <= 255);
    }
}
