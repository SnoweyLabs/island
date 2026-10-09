using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 2 on the settings screen (the repairs of ease-1-4, 1-5, 1-6, 1-7, 1-21 and what they did to the keyboard and the words). Built and laid out on the one STA thread, never shown; buttons are
/// pressed through their Click event; no input is made and no popup is opened. Invented names only.
/// </summary>
public class Round2SettingsTests
{
    private static KeyPress CtrlAlt(int vk) => new(vk, HotkeyModifiers.Control | HotkeyModifiers.Alt, false);

    private static object? FindByKey(SettingsView view, string key) =>
        typeof(SettingsView).GetMethod("FindByKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [key]);

    private static string? KeyOf(object? element) => element is DependencyObject d ? Tree.FocusKeyOf(d) : null;

    // ---- the question before "Restore the original keys" ------------------------------------------------------------------------------------

    private static void SetFiveKeys(SettingsSession session)
    {
        Assert.True(session.PressKey(KeybindEditor.MainId, CtrlAlt(0x47)).Changed); // the main key changed
        Assert.True(session.PressKey("media", CtrlAlt(0x31)).Changed); // a page key
        var alpha = session.Picks.Picks.First(p => p.Name == "Alpha");
        Assert.True(session.PressKey(alpha.Id, CtrlAlt(0x32)).Changed); // a pick key
        Assert.True(session.CreateScene("Mix").Ok);
        Assert.True(session.PressKey(KeybindEditor.SceneActionId(session.Scenes.Items.Single().Id), CtrlAlt(0x33)).Changed); // a scene key
        Assert.True(session.PressKey(KeybindEditor.ModeNextId, CtrlAlt(0x34)).Changed); // the mode key
    }

    /// <summary>Held: the number the question states is the number of keys "Restore the original keys" clears (main, page, pick, scene and mode key: five), and it is zero afterwards.</summary>
    [Fact]
    public void The_Count_The_Question_States_Is_The_Number_Of_Keys_That_Are_Cleared()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        SetFiveKeys(session);
        Assert.Equal(5, session.KeysSetByYou);
        Assert.Contains("5 keys", SettingsText.RestoreAllKeysQuestion(session.KeysSetByYou));

        session.RestoreAllKeys();

        Assert.Equal(0, session.KeysSetByYou);
        Assert.Equal(Settings.Defaults.ShowHide, session.Settings.ShowHide);
        Assert.Empty(session.Settings.PickKeys);
        Assert.Null(session.Settings.ModeKey);
        Assert.All(session.Settings.PageKeys, k => Assert.Null(k.Combo));
    }

    /// <summary>Held, the first start's variant of the Key step: the same link asks the same question, Esc is No and leaves every key, No leaves every key, Yes clears them.</summary>
    [Fact]
    public void In_The_First_Start_The_Restore_Link_Asks_Esc_And_No_Keep_Every_Key_And_Yes_Clears_Them()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            SetFiveKeys(session);
            var view = fixture.NewView(setup: true);
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);

            Tree.Click(view, "key:all");
            Assert.True(view.WantsEscape);
            Assert.True(view.CountTextContaining("5 keys") > 0);
            Assert.Equal(5, session.KeysSetByYou);

            Assert.True(view.HandleEscape()); // Esc is No
            Assert.False(view.WantsEscape);
            Assert.Equal(5, session.KeysSetByYou);

            Tree.Click(view, "key:all");
            Tree.Click(view, "dialog:no");
            Assert.Equal(5, session.KeysSetByYou);

            Tree.Click(view, "key:all");
            Tree.Click(view, "dialog:yes");
            Assert.Equal(0, session.KeysSetByYou);
        });
    }

    /// <summary>Held: with nothing set the link asks nothing (nothing to lose) and does nothing.</summary>
    [Fact]
    public void With_No_Key_Set_The_Restore_Link_Asks_Nothing()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "key:all");
            Assert.False(view.WantsEscape);
        });
    }

    // ---- FindByKey and its callers --------------------------------------------------------------------------------------------------------------

    /// <summary>Held: FindByKey returns only an enabled control, and every caller that has somewhere else to go falls through to Continue (the only one without a fall-through is FocusTheAsker, whose asker is
    /// a control that was enabled when it asked and is not rebuilt by No or Esc).</summary>
    [Fact]
    public void Every_Caller_Of_FindByKey_Falls_Through_Or_Is_Held_By_The_Question()
    {
        var src = Source.Read("src/Island.SettingsUi/SettingsView.cs");
        Assert.Contains("(FindByKey(\"key:main\") ?? FindByKey(\"foot:next\"))?.Focus()", src);
        Assert.Contains("(FindByKey(keep) ?? FindByKey(\"foot:next\"))?.Focus()", src);
        Assert.Contains("FindByKey(from)?.Focus()", src);
        Assert.Contains("IsEnabled: true", src);
    }

    /// <summary>Held: a disabled control is not found, and the fall-through is Continue (or Done on the last section).</summary>
    [Fact]
    public void A_Disabled_Control_Is_Not_Found_And_Continue_Is()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Assert.Null(FindByKey(view, "key:restore")); // the main key is still the default: "Restore default" is disabled
            Assert.Equal("foot:next", KeyOf(FindByKey(view, "foot:next")));
        });
    }

    /// <summary>
    /// ease-2-10 (LOW). The repair of ease-1-5 sends the keyboard to Continue when the control that was pressed has disabled itself. On the last section (General) Continue is "Done", which closes the
    /// whole screen: a person who presses the minus of Idle time until it stops at 2 s (or holds Enter on it, key repeat) finds the keyboard on Done, and the next press closes Settings. In
    /// Key, Continue is the next section. Expected: the keyboard stays in the card (the plus beside the minus), and only when nothing there can take it goes to Continue.
    /// </summary>
    [Fact]
    public void Defect_The_Fall_Through_After_The_Minus_Of_Idle_Time_Stops_At_Two_Seconds_Is_The_Done_Button_That_Closes_Settings()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            while (fixture.Session.Settings.IdleSeconds > Settings.MinIdleSeconds) Tree.Click(view, "general:idle-less");

            var target = FindByKey(view, "general:idle-less") ?? FindByKey(view, "foot:next");

            Assert.NotEqual("foot:next", KeyOf(target));
        });
    }

    /// <summary>The premise of ease-2-10: the last section's Continue is Done.</summary>
    [Fact]
    public void On_The_Last_Section_Continue_Is_Done()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            var done = Tree.ButtonKeyed(view, "foot:next");
            Assert.Equal("Done", Tree.AccessibleName(done));
        });
    }

    /// <summary>
    /// ease-2-9 (LOW). The plus and the minus of Idle time and of Notice time are named "Shorter idle time" and "Longer idle time"; the value ("5 s") is a label that cannot take the keyboard and is built
    /// again after every press. A person with a screen reader presses the button, the keyboard returns to the same button and its name is read again: the new value is not told anywhere unless
    /// they go and read the label. No pixel needs to change. Expected: the button's name carries the value ("Shorter idle time, now 4 seconds").
    /// </summary>
    [Fact]
    public void Defect_The_Idle_Time_Buttons_Do_Not_Say_The_Value_They_Have_Just_Set()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            var seconds = ((int)fixture.Session.Settings.IdleSeconds).ToString();
            Assert.Contains(seconds, Tree.AccessibleName(Tree.ButtonKeyed(view, "general:idle-less")));
        });
    }

    // ---- the ring on the three fields ----------------------------------------------------------------------------------------------------------

    /// <summary>Held: the colour field, the program-narrowing field and the site field use the ring template; its ring is the whole size of the box (inside, so it cannot be clipped by the card), turns on
    /// with IsKeyboardFocusWithin, and is the white of the buttons' ring (#E6FFFFFF).</summary>
    [Fact]
    public void The_Ring_On_The_Three_Fields_Is_As_Big_As_The_Box_And_Shows_With_The_Focus()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            var page = fixture.Session.Pages.Pages[^1].Id;
            view.ShowAddPanel(page, "al", "example.org");
            Tree.Layout(view, 1920, 1080);
            var hexView = fixture.NewView();
            hexView.Section = SettingsSection.Pages;
            Tree.Layout(hexView, 1920, 1080);
            Tree.Click(hexView, $"dot:{page}"); // opens the colour panel of the page, where the field is

            var boxes = Tree.Of<TextBox>(view).Where(b => Tree.FocusKeyOf(b) is { } k && (k.StartsWith("add:filter:") || k.StartsWith("add:site:"))).Concat(
                Tree.Of<TextBox>(hexView).Where(b => Tree.FocusKeyOf(b) is { } k && k.StartsWith("hex:"))).ToList();
            Assert.True(boxes.Count >= 3, $"{boxes.Count} of the three kinds of field were found");
            foreach (var box in boxes)
            {
                box.ApplyTemplate();
                var ring = (Border?)box.Template.FindName("Ring", box);
                Assert.NotNull(ring);
                Assert.True(Math.Abs(ring!.ActualWidth - box.ActualWidth) < 0.5 && Math.Abs(ring.ActualHeight - box.ActualHeight) < 0.5, $"{Tree.FocusKeyOf(box)}: ring {ring.ActualWidth}x{ring.ActualHeight}, box {box.ActualWidth}x{box.ActualHeight}");
                var trigger = box.Template.Triggers.OfType<Trigger>().Single();
                Assert.Equal(UIElement.IsKeyboardFocusWithinProperty, trigger.Property);
                Assert.Equal("#E6FFFFFF", ((System.Windows.Media.SolidColorBrush)trigger.Setters.OfType<Setter>().Single().Value).Color.ToString());
            }
        });
    }

    // ---- the Blur note ------------------------------------------------------------------------------------------------------------------------

    private static TextBlock BlurNote(SettingsView view) =>
        Tree.Of<TextBlock>(view).First(t => t.Text.StartsWith("Blur glass is not available", StringComparison.Ordinal));

    /// <summary>Held: the Blur note wraps inside the viewport at 100%, 150%, 200% and 300% (the viewport in device-independent units at 1920x1080 is 1920, 1280, 960 and 640 wide) and keeps all three parts.</summary>
    [Fact]
    public void The_Blur_Note_Wraps_Inside_The_Screen_At_150_200_And_300_Percent()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Glass;
            foreach (var (w, h) in new[] { (1920.0, 1080.0), (1280.0, 720.0), (960.0, 540.0), (640.0, 360.0) })
            {
                Tree.Layout(view, w, h);
                var note = BlurNote(view);
                var box = Tree.BoundsIn(note, view);
                Assert.True(box.Left >= 0 && box.Right <= w, $"{w}: the note runs from {box.Left} to {box.Right}");
                Assert.True(note.ActualHeight >= note.FontSize, $"{w}: the note has no height");
                Assert.Contains("Turn on Transparency effects", note.Text);
            }
        });
    }

    /// <summary>
    /// ease-2-8 (LOW). The register's BLUR_UNAVAILABLE says "Island uses the Approved glass instead." (that is true when Blur was the chosen glass and fell back). The Glass section shows the note whenever
    /// Blur is not available, whatever is chosen: a person who chose Darker reads that Island uses the Approved glass, and it uses Darker. A text that says something false is a defect by the one rule.
    /// Expected: the note in Settings does not name a glass that is not the one in use (say it only when the saved choice is Blur, or leave that sentence out here).
    /// </summary>
    [Fact]
    public void Defect_The_Blur_Note_Says_The_Approved_Glass_Is_Used_While_Darker_Is_In_Use()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            Assert.True(fixture.Session.SetGlass(GlassKind.Darker).Ok);
            Assert.Equal(GlassKind.Darker, fixture.Session.EffectiveGlass);
            var view = fixture.NewView();
            view.Section = SettingsSection.Glass;
            Tree.Layout(view, 1920, 1080);

            Assert.DoesNotContain("uses the Approved glass instead", BlurNote(view).Text);
        });
    }

    // ---- the Move list --------------------------------------------------------------------------------------------------------------------------

    /// <summary>Held, by reading: the list's keys are Down and Up (wrapping, the first Down from nothing lands on the first entry), Esc closes the list and gives the keyboard back to the Move button, a
    /// choice refreshes with the Move button's own key (which still exists on the pick's new page). Not run: a popup is a window, and this attacker never opens one.</summary>
    [Fact]
    public void The_Move_List_Handles_Down_Up_Esc_And_Gives_The_Keyboard_Back_To_Its_Button()
    {
        var src = Source.Read("src/Island.SettingsUi/PicksSection.cs");
        Assert.Contains("entries[(at + 1) % entries.Count].Focus();", src);
        Assert.Contains("entries[(Math.Max(at, 0) + entries.Count - 1) % entries.Count].Focus();", src);
        Assert.Contains("menu.IsOpen = false;\n                        button?.Focus();", src.Replace("\r\n", "\n"));
        Assert.Contains("host.Refresh(key);", src);
        Assert.Contains("DispatcherPriority.Input", src);
    }

    /// <summary>
    /// A fact the main session should know about the Move list (UNVERIFIED at run time, so no Defect_ test): the popup is made in code with only a PlacementTarget and is never added to the logical tree, and
    /// the settings window handles Esc in PreviewKeyDown (SettingsScreen.OnKey: HandleEscape, else BeginClose) on the way DOWN. If WPF routes a key from a popup's content through the owner window, the window
    /// sees Esc before the list's own handler and closes the whole screen; if it stops at the popup (no logical parent), the list's handler is the only one that sees it. Not decided by the pages read.
    /// </summary>
    [Fact]
    public void The_Move_Popup_Has_No_Logical_Parent_And_The_Window_Handles_Esc_Before_Anything_Below_It()
    {
        var picks = Source.Read("src/Island.SettingsUi/PicksSection.cs");
        var menuStart = picks.IndexOf("var menu = new System.Windows.Controls.Primitives.Popup", StringComparison.Ordinal);
        var menuEnd = picks.IndexOf("menu.IsOpen = true;", menuStart, StringComparison.Ordinal);
        var made = picks[menuStart..menuEnd];
        Assert.DoesNotContain("AddLogicalChild", made);
        Assert.DoesNotContain(".Children.Add(menu)", made);
        Assert.Contains("PreviewKeyDown += OnKey;", Source.Read("src/Island.App/SettingsScreen.cs"));
    }
}
