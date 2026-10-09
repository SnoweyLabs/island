using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 3 on the settings screen and its session after the repairs of round 2 (dcb3af1 SetPick, cb94314/44fcfa1 steppers, e9eed51 + 410d9a2 the Blur note, 410d9a2 the name rules, 341cc71 the restore question)
/// and what round 2 did not reach (the question before a removal, the Moving light card's refusal). Built and laid out on the one STA thread, never shown; buttons are pressed through their Click event; no input
/// is made. Invented names only.
/// </summary>
public class Round3SettingsTests
{
    private static KeyPress CtrlAlt(int vk) => new(vk, HotkeyModifiers.Control | HotkeyModifiers.Alt, false);

    private static Pick AlphaOf(SettingsSession session) => session.Picks.Picks.Single(p => p.Name == "Alpha");

    private static void SwitchAlpha(SettingsSession session, bool on)
    {
        var page = session.Pages.Pages[^1].Id;
        var row = session.PickRows(page).Single(r => r.Pick.Name == "Alpha");
        session.SetPick(row, on);
    }

    // ---- ease-3-6: the question is not told ---------------------------------------------------------------------------------------------------

    private static IEnumerable<string> WhatIsToldWith(DependencyObject element)
    {
        for (DependencyObject? at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            yield return AutomationProperties.GetName(at);
            yield return AutomationProperties.GetHelpText(at);
            if (AutomationProperties.GetLabeledBy(at) is TextBlock label) yield return label.Text;
        }
    }

    /// <summary>The premise of ease-3-6: the question's words are in a TextBlock inside a ScrollViewer that never takes the keyboard, and the keyboard goes to the No button; so what a screen reader says for the focused control is that button's own name.</summary>
    [Fact]
    public void The_Keyboard_Goes_To_No_And_The_Words_Of_The_Question_Are_In_A_Text_That_Cannot_Take_It()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            Assert.True(session.PressKey(KeybindEditor.MainId, CtrlAlt(0x47)).Changed);
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "key:all");
            Assert.True(view.WantsEscape);

            var no = Tree.ButtonKeyed(view, "dialog:no");
            Assert.Equal("No, keep them", AutomationProperties.GetName(no));
            var question = Tree.Of<TextBlock>(view).Single(t => t.Text.StartsWith("Go back to the original keys?", StringComparison.Ordinal));
            Assert.False(Tree.Descendants(view).OfType<ScrollViewer>().Where(s => s.Content == question).Single().Focusable);
        });
    }

    /// <summary>
    /// ease-3-6 (MEDIUM, UNVERIFIED without a screen reader). The only guard before the steps that cannot be taken back (remove a page and its picks, bring a starter list back, restore the original keys,
    /// delete a scene, connect or disconnect a helper) is the question the overlay asks. It puts the keyboard on the No button, whose name is "No, keep them"; the question is a plain text beside it,
    /// in no dialog and with no name, so a screen reader that says what has the focus says "No, keep them" and nothing of what is being asked or what goes with it ("The 5 keys you set ...", "3 picks go
    /// with it"). Nothing is lost by accident (No is the first focus, Esc is No), but the person cannot make an informed Yes. Expected: the question's words are the help text (or the name) of the buttons,
    /// or of the card that holds them.
    /// </summary>
    [Fact]
    public void Defect_The_Words_Of_The_Question_Are_Not_Told_With_The_Button_That_Has_The_Keyboard()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            Assert.True(session.PressKey(KeybindEditor.MainId, CtrlAlt(0x47)).Changed);
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "key:all");

            var told = WhatIsToldWith(Tree.ButtonKeyed(view, "dialog:no")).Concat(WhatIsToldWith(Tree.ButtonKeyed(view, "dialog:yes")));
            Assert.Contains(told, t => t.Contains("1 key", StringComparison.Ordinal));
        });
    }

    // ---- ease-3-7: a key that was cleared comes back --------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-3-7 (LOW). The repair of ease-2-6 remembers the key of a pick that is switched off in <c>_keysOfSwitchedOff</c> and gives it back when the pick is switched on. Nothing forgets it when the
    /// person goes to Key and presses "Restore the original keys" (the question counts the keys that are set, and the off pick's key is not one of them; Yes clears every key that is set), so switching the
    /// pick on afterwards puts a key back that the person had just cleared, registered with Windows, with no word. Expected: "Restore the original keys" (and a key given to something else) forgets the kept key.
    /// </summary>
    [Fact]
    public void Defect_A_Key_Restored_Away_While_Its_Pick_Is_Off_Comes_Back_When_The_Pick_Is_Switched_On()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        SwitchAlpha(session, on: false);

        session.RestoreAllKeys();
        Assert.Equal(0, session.KeysSetByYou);

        SwitchAlpha(session, on: true);

        Assert.Null(session.Settings.PickKeyFor(id));
    }

    /// <summary>
    /// ease-3-8 (LOW). The warning the repair of ease-2-6 gives when the key cannot be given back says "is used by something else now". The editor refuses for four reasons (taken by another key of Island,
    /// taken by another program, refused by Windows with a code, the settings file not saved); the text is the first two only, and the repair throws the real reason (<c>back.Refusal</c>) away. When the
    /// settings file cannot be written the person is told a thing that is not so, and "press a key for it again" meets the same failure. Expected: the warning carries the editor's own words (or says the key
    /// could not be given back, with the real reason).
    /// </summary>
    [Fact]
    public void Defect_The_Warning_For_A_Key_That_Was_Not_Given_Back_Says_It_Is_Used_By_Something_Else_Whatever_The_Reason()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        SwitchAlpha(session, on: false);

        // the settings file cannot be written now (a folder stands where it was): the key is free, Windows would take it, only the save fails
        File.Delete(fixture.Files.SettingsPath);
        Directory.CreateDirectory(fixture.Files.SettingsPath);

        var page = session.Pages.Pages[^1].Id;
        var result = session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: true);

        Assert.True(result.Ok || result.Changed);
        Assert.Null(session.Settings.PickKeyFor(id));
        Assert.NotNull(result.Warning); // the person is told the key was not given back
        Assert.DoesNotContain("used by something else", result.Warning, StringComparison.Ordinal);
    }

    // ---- ease-3-12 and ease-3-13: two silences of the screen ------------------------------------------------------------------------------------

    /// <summary>
    /// ease-3-12 (LOW). Pressing a key's button starts the wait for a new combination: the screen draws a pulsing "Press your keys..." and "Hold Ctrl, Alt or Shift and press one key. Esc cancels.", and from
    /// then on every key is taken by the screen (<c>OnPreviewKeyDown</c>: <c>e.Handled = true</c>, Tab and the arrows included; a refusal goes to the notice line). The button that holds the keyboard is made again
    /// with the same name, "Show or hide the island: Ctrl+Q. Press to change.", so a person who cannot see the pulsing text is never told that the screen is waiting, that his keys are being read as the
    /// new key, or that Esc leaves. Nothing is lost (Esc cancels; the old key stays until a good one is pressed). Expected: while the wait is on the button's name (or help text) says so and names Esc.
    /// </summary>
    [Fact]
    public void Defect_The_Wait_For_A_New_Key_Is_Not_Told_The_Button_Keeps_The_Same_Name()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            var before = WhatIsToldWith(Tree.ButtonKeyed(view, "key:main")).Where(t => t.Length > 0).ToList();
            Tree.Click(view, "key:main");
            Assert.True(view.WantsEscape); // the premise: the screen is waiting and Esc is its own
            var during = WhatIsToldWith(Tree.ButtonKeyed(view, "key:main")).Where(t => t.Length > 0).ToList();
            Assert.Contains(during, t => t.Contains("Esc", StringComparison.Ordinal));
            Assert.NotEqual(before, during);
        });
    }

    /// <summary>
    /// ease-3-13 (MEDIUM, UNVERIFIED without a screen reader). "Continue" (and "Back", and a click on a step) builds the next section and puts the keyboard on the Continue button of the new page: a control
    /// made again with the same name ("Continue"; in the first start "Start", "Continue" and "Done"). The heading and the sentence of the new section are plain texts that never take the keyboard and are
    /// not a live region, so a screen reader says "Continue, button" and nothing about what the section is; the person who cannot see the steps at the top does not know that the section changed, which
    /// one he is in, or that his first Tab goes to the steps. Expected: after the step changes, what the focused control tells (name or help text) or a live region holds the new heading.
    /// </summary>
    [Fact]
    public void Defect_The_New_Section_Is_Not_Told_When_Continue_Builds_It()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "foot:next"); // Key -> Pages
            Assert.Equal(SettingsSection.Pages, view.Section);
            var heading = Tree.Of<TextBlock>(view).First(t => t.Text == "Your pages" && t.FontSize > 20);
            var told = WhatIsToldWith(Tree.ButtonKeyed(view, "foot:next")).Where(t => t.Length > 0);
            var live = AutomationProperties.GetLiveSetting(heading) != AutomationLiveSetting.Off;
            Assert.True(live || told.Any(t => t.Contains("Your pages", StringComparison.Ordinal)), "nothing the focused control says or a live region holds names the new section");
        });
    }

    // ---- held: the SetPick repair ----------------------------------------------------------------------------------------------------------

    /// <summary>Held: a key that was meanwhile given to another thing is not taken back; the person is told, and the pick is on without it.</summary>
    [Fact]
    public void A_Key_Taken_Meanwhile_Is_Not_Given_Back_And_The_Person_Is_Told()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        SwitchAlpha(session, on: false);
        Assert.True(session.PressKey("media", CtrlAlt(0x4A)).Changed); // the page takes the free key

        var page = session.Pages.Pages[^1].Id;
        var result = session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: true);

        Assert.Null(session.Settings.PickKeyFor(id));
        Assert.NotNull(session.Settings.KeyFor("media"));
        Assert.Contains("not given back", result.Warning);
        Assert.True(session.PickRows(page).Single(r => r.Pick.Name == "Alpha").On);
    }

    /// <summary>Held: a pick that never had a key gets none when it comes back, and a pick with a key that is free gets it back (the repair's two plain cases).</summary>
    [Fact]
    public void A_Pick_Without_A_Key_Gets_None_And_A_Pick_With_A_Free_Key_Gets_It_Back()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var id = AlphaOf(session).Id;
        SwitchAlpha(session, on: false);
        SwitchAlpha(session, on: true);
        Assert.Null(session.Settings.PickKeyFor(id));

        Assert.True(session.PressKey(id, CtrlAlt(0x4B)).Changed);
        SwitchAlpha(session, on: false);
        SwitchAlpha(session, on: true);
        Assert.NotNull(session.Settings.PickKeyFor(id));
    }

    /// <summary>Held: the page of a switched-off pick is removed and a new page gets its id: no faint chip and no key come to the new page, even when the same program is added to it again.</summary>
    [Fact]
    public void A_Removed_Pages_Chip_And_Kept_Key_Do_Not_Come_To_A_New_Page_With_The_Same_Id()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var old = session.Pages.Pages[^1];
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        SwitchAlpha(session, on: false);
        Assert.True(session.DeletePage(old.Id).Ok);
        Assert.True(session.CreatePage("Work", "#19E6B3").Ok);
        var fresh = session.Pages.Pages[^1];
        Assert.Equal(old.Id, fresh.Id);

        var added = session.Picks.Add(Pick.ForProgram("Alpha", fresh.Id, "alpha.exe", null), out _);
        Assert.Contains(added.Picks, p => p.Id == id);
        // the same pick comes back by the Add panel's own path: the session has nothing kept for it
        Assert.DoesNotContain(session.PickRows(fresh.Id), r => r.Pick.Name == "Alpha");
        Assert.Null(session.Settings.PickKeyFor(id));
    }

    /// <summary>
    /// ease-3-14 (LOW). <c>SetPick(on: true)</c> puts the pick back with <c>PickStore.Add</c>, which refuses without a word when the same thing is already on the island (<c>IsSameThing</c>: the same program
    /// file name under another id, or the same place). The refusal is not a result: <c>ApplyPicks</c> answers "nothing changed, no refusal", <c>SetPick</c> takes that for success, drops the faint chip
    /// (<c>_switchedOff.RemoveAll</c>) and, if the pick had a key, assigns that key to a pick that is not there. The person presses "off. Press to switch.", nothing happens, and the chip is gone. Reach:
    /// a program added by Browse (its own id), switched off, then added again from the list (id by file name) before the chip is pressed. Expected: the person is told ("... is already on the island, on the
    /// page ...", as the Add panel says) and the chip stays or its way back is not dropped silently.
    /// </summary>
    [Fact]
    public void Defect_Switching_On_A_Pick_Whose_Program_Was_Added_Again_Does_Nothing_Says_Nothing_And_Drops_The_Chip()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        session.Chooser = new PretendChooser(file: @"Q:\Invented\Profile\Apps\beta.exe");
        session.HandContextSource = () => new HandContext(@"Q:\Invented\Profile", []);
        var page = session.Pages.Pages[^1].Id;
        Assert.True(session.AddProgramByBrowsing(page).Added, "the premise: a program added by Browse");
        var hand = session.Picks.Picks.Single(p => p.ExeName == "beta.exe");
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Id == hand.Id), on: false);
        Assert.True(session.AddProgramFromList(page, new InstalledProgram("Beta", "beta.exe", null, "launch-Beta")).Added, "the premise: the same program added again from the list (another id)");

        var chip = session.PickRows(page).Single(r => r.Pick.Id == hand.Id);
        Assert.False(chip.On);
        var result = session.SetPick(chip, on: true);

        Assert.DoesNotContain(session.Picks.Picks, p => p.Id == hand.Id); // it cannot be on: the same program is already there
        Assert.True(result.Refusal is not null || result.Warning is not null, "nothing was said");
    }

    /// <summary>Held: a pick that is switched off cannot be moved to another page (it is not on the island: MovePick refuses it), so a faint chip never carries a page that no longer holds it; and a pick that is on and moved keeps its key.</summary>
    [Fact]
    public void A_Switched_Off_Pick_Cannot_Be_Moved_And_A_Moved_Pick_Keeps_Its_Key()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        Assert.True(session.MovePick(id, PageIds.Folders).Changed);
        Assert.NotNull(session.Settings.PickKeyFor(id));
        session.SetPick(session.PickRows(PageIds.Folders).Single(r => r.Pick.Id == id), on: false);
        Assert.Null(session.Settings.PickKeyFor(id));
        var refused = session.MovePick(id, PageIds.Apps);
        Assert.False(refused.Ok);
        Assert.DoesNotContain(session.Picks.Picks, p => p.Id == id);
    }

    // ---- the stepper's partner ------------------------------------------------------------------------------------------------------------

    /// <summary>Held: the only focus keys that end in "-less" or "-more" are the four stepper buttons of General, so the partner rule of FindByKey cannot send the keyboard anywhere else.</summary>
    [Fact]
    public void The_Only_Keys_That_End_In_Less_Or_More_Are_The_Four_Stepper_Buttons()
    {
        var found = new List<string>();
        foreach (var (path, text) in Source.All("src/Island.SettingsUi"))
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"([a-z:]+-(less|more))\""))
                found.Add(m.Groups[1].Value);
        Assert.Equal(new[] { "general:idle-less", "general:idle-more", "general:notice-less", "general:notice-more" }, found.Distinct().Order().ToArray());
    }

    /// <summary>Held: after the minus of Idle time stops at its end the keyboard is found on the plus beside it (and the reverse); a section without the pair finds neither.</summary>
    [Fact]
    public void After_The_Minus_Stops_At_Its_End_The_Keyboard_Goes_To_The_Plus_And_Back()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            var find = typeof(SettingsView).GetMethod("FindByKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.Equal("general:idle-less", Tree.FocusKeyOf((DependencyObject)find.Invoke(view, ["general:idle-less"])!));
            for (var i = 0; i < 80 && Tree.ButtonKeyed(view, "general:idle-less").IsEnabled; i++) Tree.Click(view, "general:idle-less");
            Assert.False(Tree.ButtonKeyed(view, "general:idle-less").IsEnabled);
            Assert.Equal("general:idle-more", Tree.FocusKeyOf((DependencyObject)find.Invoke(view, ["general:idle-less"])!));
            view.Section = SettingsSection.Glass;
            Tree.Layout(view, 1920, 1080);
            Assert.Null(find.Invoke(view, ["general:idle-less"]));
        });
    }

    // ---- the Blur note ----------------------------------------------------------------------------------------------------------------------

    /// <summary>The three parts of the register's refusal are what happened, why refusing is right, and what to do. Held: for Approved and Blur the note has all three; the note is built only while Blur is not available.</summary>
    [Fact]
    public void The_Blur_Note_Has_Its_Three_Parts_For_Approved_And_For_Blur_And_Is_Shown_Only_While_Blur_Is_Unavailable()
    {
        var refusal = Refusals.BlurUnavailable;
        foreach (var glass in new[] { GlassKind.Approved, GlassKind.Blur })
        {
            var text = SettingsText.BlurUnavailableFor(glass);
            Assert.Contains(refusal.WhatHappened, text);
            Assert.Contains(refusal.Why, text);
            Assert.Contains(refusal.NextAction, text);
        }

        Assert.Contains("if (!session.BlurAvailable)", Source.Read("src/Island.SettingsUi/GlassSection.cs"));
    }

    /// <summary>
    /// ease-3-9 (LOW). The repair of ease-2-8 left the middle part of the refusal out for the Darker glass ("Island uses the Approved glass instead" would be false there), as round 2 asked. By the one rule
    /// a refusal that lacks one of its three parts is a defect too: the note and the refusal given when Blur is pressed now say what happened and what to do, and not why refusing is right (what Island
    /// does instead). My own round-2 repair was incomplete: the middle part is not left out, it is made true ("Island keeps the Darker glass."). Expected: three sentences for Darker too.
    /// </summary>
    [Fact]
    public void Defect_The_Blur_Note_For_The_Darker_Glass_Has_Two_Of_The_Three_Parts_Of_Its_Refusal()
    {
        var text = SettingsText.BlurUnavailableFor(GlassKind.Darker);
        var refusal = Refusals.BlurUnavailable;
        Assert.DoesNotContain("Approved", text);
        var middle = text.Replace(refusal.WhatHappened, string.Empty, StringComparison.Ordinal).Replace(refusal.NextAction, string.Empty, StringComparison.Ordinal).Trim();
        Assert.True(middle.Length > 0, "nothing is said of what Island does instead");
    }

    // ---- the Moving light card ------------------------------------------------------------------------------------------------------------

    /// <summary>Held: every refusal of the register has three parts, each a sentence that ends with a full stop (the Moving light card is gone: WORK-ORDER-13).</summary>
    [Fact]
    public void Every_Refusal_Of_The_Register_Has_Three_Sentences()
    {
        foreach (var refusal in Refusals.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(refusal.WhatHappened), refusal.Code);
            Assert.False(string.IsNullOrWhiteSpace(refusal.Why), refusal.Code);
            Assert.False(string.IsNullOrWhiteSpace(refusal.NextAction), refusal.Code);
            Assert.EndsWith(".", refusal.WhatHappened, StringComparison.Ordinal);
            Assert.EndsWith(".", refusal.Why, StringComparison.Ordinal);
            Assert.EndsWith(".", refusal.NextAction, StringComparison.Ordinal);
        }
    }

    // ---- the name rules after 410d9a2 ----------------------------------------------------------------------------------------------------

    private static PageEdit Create(PageStore store, string name) => store.Create(name, "#7CE04A");

    /// <summary>Held: names with a joiner, a replacement character, a lone surrogate, an override and a character of the emoji planes never throw, and are accepted or refused as the rules say (a joiner between two characters that draw: accepted; a lone joiner, two, a trailing one, U+FFFD, a lone surrogate and a right-to-left override: refused).</summary>
    [Fact]
    public void Odd_Page_Names_Never_Throw_And_Are_Accepted_Or_Refused_As_The_Rules_Say()
    {
        var store = PageStore.Default;
        var accepted = new[] { "Wo‍rkshop", "\U0001F468‍\U0001F469‍\U0001F467 Family", "Café" };
        var refused = new[] { "‍", "A‍‍B", "A‍", "‍A", "A�B", "A\uD800B", "A\uDC00B", "A‮B", "A\u0000B", "AB" };
        foreach (var name in accepted) Assert.True(Create(store, name).Page is not null, $"{Escape(name)} was refused: {Create(store, name).Refusal}");
        foreach (var name in refused) Assert.True(Create(store, name).Refusal is not null, $"{Escape(name)} was accepted");

        // whatever the input, nothing throws (Normalize throws on invalid text)
        foreach (var name in new[] { "A\U0001FFFE", "A\U0001FFFF", "￾", "A﷐", "\U0001F000", "Á́", new string('́', 10) })
        {
            var edit = Create(store, name);
            Assert.NotNull(edit);
        }
    }

    /// <summary>Held: names that are one name after composition are one page (the repair of code-2-3): "é" and "e" + a combining accent are the same page.</summary>
    [Fact]
    public void A_Composed_And_A_Decomposed_Name_Are_One_Page()
    {
        var store = Create(PageStore.Default, "Café").Store;
        Assert.NotNull(Create(store, "Café").Refusal);
        Assert.NotNull(Create(store, "CAFÉ").Refusal);
    }

    /// <summary>
    /// ease-3-11 (LOW, introduced by 410d9a2). A joiner between two characters that draw is let through for every script, Latin included, where it draws nothing; the clash test compares names after
    /// composition only. "Work" and "Wo" + ZWJ + "rk" are two pages that look and read alike (a screen reader says "Work" for both), which the same-name rule exists to prevent; before the repair every
    /// joiner was refused. Reach is small (the person has to paste the joiner). Expected: names are compared without joiners and non-joiners.
    /// </summary>
    [Fact]
    public void Defect_A_Page_Named_With_A_Hidden_Joiner_Is_Not_The_Same_Page_As_The_Plain_Name()
    {
        var store = Create(PageStore.Default, "Work").Store;
        Assert.NotNull(Create(store, "Wo‍rk").Refusal);
    }

    /// <summary>Held: a pick's name keeps a real U+FFFD (saves, loads, is added again); a lone surrogate is refused when added or saved (the repair of ease-2-7 and code-2-1).</summary>
    [Fact]
    public void A_Real_Replacement_Character_Is_Kept_In_A_Pick_And_A_Lone_Surrogate_Is_Not()
    {
        Assert.True(Pick.ForProgram("Al�pha", PageIds.Apps, "alpha.exe", null).IsStorable(out _));
        Assert.False(Pick.ForProgram("Al\uD800pha", PageIds.Apps, "alpha.exe", null).IsStorable(out _));
    }

    private static string Escape(string text) => string.Concat(text.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()));

    // ---- Esc from the Move list ----------------------------------------------------------------------------------------------------------

    /// <summary>Held (ease-2-12, by reading): the screen's own Esc ignores a key whose source belongs to another presentation source (a popup), so the list's own handler closes only the list; a key from the screen itself still closes the screen.</summary>
    [Fact]
    public void The_Screens_Esc_Ignores_A_Key_That_Comes_From_A_Popup_Of_The_Screen()
    {
        var screen = Source.Read("src/Island.App/SettingsScreen.cs");
        var onKey = screen[screen.IndexOf("private void OnKey(object sender, KeyEventArgs e)", StringComparison.Ordinal)..];
        onKey = onKey[..onKey.IndexOf("e.Handled = true;", StringComparison.Ordinal)];
        Assert.Contains("PresentationSource.FromDependencyObject(source)", onKey);
        Assert.Contains("!ReferenceEquals(presentation.RootVisual, this)) return;", onKey);
        Assert.True(onKey.IndexOf("return;", StringComparison.Ordinal) < onKey.IndexOf("BeginClose()", StringComparison.Ordinal));
        var picks = Source.Read("src/Island.SettingsUi/PicksSection.cs");
        Assert.Contains("case System.Windows.Input.Key.Escape:", picks);
        Assert.Contains("menu.IsOpen = false;", picks);
    }
}
