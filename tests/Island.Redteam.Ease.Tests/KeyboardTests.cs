using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>The settings screen and the first-start steps with the keyboard alone: reach, order, focus after a change, a mark of focus, Esc.</summary>
public class KeyboardTests
{
    private static IInputElement? FindByKey(SettingsView view, string key) =>
        (IInputElement?)typeof(SettingsView).GetMethod("FindByKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [key]);

    [Fact]
    public void Every_Button_And_Field_In_Every_Section_Can_Take_The_Keyboard()
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
                    foreach (var control in Tree.Descendants(view).OfType<FrameworkElement>().Where(e => e is Button or TextBox))
                    {
                        Assert.True(control.Focusable, $"{section}: {Tree.FocusKeyOf(control)} is not focusable");
                        Assert.True(KeyboardNavigation.GetIsTabStop(control), $"{section}: {Tree.FocusKeyOf(control)} is not a tab stop");
                    }
                }
            }
        });
    }

    [Fact]
    public void The_Tab_Order_Follows_The_Reading_Order_In_Every_Section()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            foreach (var section in Tree.FullSections)
            {
                view.Section = section;
                Tree.Layout(view, 1920, 6000); // tall, so that nothing is scrolled out of place
                var previous = -1.0;
                foreach (var stop in Tree.Descendants(view).OfType<FrameworkElement>().Where(e => e is Button or TextBox && e.IsEnabled && e.Focusable))
                {
                    var y = Tree.BoundsIn(stop, view).Y;
                    Assert.True(y >= previous - 20, $"{section}: Tab goes back up to {Tree.FocusKeyOf(stop)}");
                    previous = y;
                }
            }
        });
    }

    /// <summary>
    /// ease-1-5 (MEDIUM). After every change the screen is built again and the keyboard is put back on the element with the same key: <c>(FindByKey(key) ?? FindByKey("foot:next"))?.Focus()</c>.
    /// A control that disables itself by being pressed ("Restore default", "Clear", the minus of Idle time at 2 s and of Notice time at 3 s, the plus at the top) is found, and Focus() on a
    /// disabled element does nothing; the fall-back to Continue is only used when nothing is found. The keyboard is then on nothing: Tab starts again from the top of the screen and a
    /// person holding Enter on a minus loses their place. Expected: the element chosen to take the keyboard is one that can.
    /// </summary>
    [Fact]
    public void Defect_The_Keyboard_Is_Not_Sent_To_A_Control_That_Has_Just_Disabled_Itself()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            session.SetIdleSeconds(3);
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "general:idle-less"); // 3 s to 2 s: the minus is now at its limit and disabled
            Assert.Equal(2, session.Settings.IdleSeconds);

            var chosen = FindByKey(view, "general:idle-less") ?? FindByKey(view, "foot:next"); // exactly what Rebuild does
            Assert.True(chosen is UIElement { IsEnabled: true }, "the control that was pressed is disabled and is still the one the keyboard is sent to");
        });
    }

    [Fact]
    public void Defect_The_Keyboard_Is_Not_Lost_After_Restore_Default_On_The_Main_Key()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            Assert.True(session.PressKey(KeybindEditor.MainId, new KeyPress(0x4B, HotkeyModifiers.Control | HotkeyModifiers.Alt, false)).Changed);
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "key:restore"); // the main key is back to Ctrl+Q: "Restore default" is disabled again

            var chosen = FindByKey(view, "key:restore") ?? FindByKey(view, "foot:next");
            Assert.True(chosen is UIElement { IsEnabled: true });
        });
    }

    /// <summary>
    /// ease-1-6 (MEDIUM; the fourth case of the one rule: a control the keyboard reaches that shows no mark of focus gets the mark a focused control already has elsewhere in Settings).
    /// The colour field of a page, the narrowing field of the program list and the site field use <c>Parts.QuietBoxTemplate</c>, which has no trigger at all: a person who Tabs into
    /// one sees only a caret. Every button has the white ring (<c>Parts.PillTemplate</c>, trigger on IsKeyboardFocused); a page's name field is tinted by code. Expected: a trigger on
    /// IsKeyboardFocused (or IsKeyboardFocusWithin) in the template, with the ring buttons already have. A picture goes under OWNER DECISIONS.
    /// </summary>
    [Fact]
    public void Defect_The_Text_Fields_Show_No_Mark_Of_Focus_Beyond_The_Caret()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Pages;
            Tree.Layout(view, 1920, 1080);
            Tree.Click(view, "dot:media"); // opens the colour panel with its field
            var hex = Tree.Of<TextBox>(view).Single(t => Tree.FocusKeyOf(t) == "hex:media");

            var marked = hex.Template.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsKeyboardFocusedProperty || t.Property == UIElement.IsKeyboardFocusWithinProperty);
            Assert.True(marked, "the colour field has no template trigger for keyboard focus");
        });
    }

    [Fact]
    public void Every_Button_Template_Draws_A_Ring_While_It_Has_The_Keyboard()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            foreach (var button in Tree.Of<Button>(view))
                Assert.Contains(button.Template.Triggers.OfType<Trigger>(), t => t.Property == UIElement.IsKeyboardFocusedProperty);
        });
    }

    /// <summary>
    /// ease-1-2 (HIGH). "Move this pick to another page" (the small "Apps ▾" button beside each pick) opens a <c>Popup</c> and never moves the keyboard into it. A Popup's content is not in the
    /// tab sequence of the window it hangs from; with the keyboard alone (and with a screen reader) the list of pages cannot be reached: Tab goes on to the next control and the popup,
    /// which does not stay open, closes. Nothing else in the screen moves a pick, so for someone without a mouse the feature does not exist. Not run: a Popup is a window, and this attacker
    /// never shows one; the finding is from the code (no Focus call anywhere in the method that builds and opens the menu) and the behaviour of Popup is UNVERIFIED at run time.
    /// Expected: the first entry takes the keyboard when the menu opens, and Esc closes the menu only. Smallest repair: <c>list.Children[0].Focus()</c> after <c>menu.IsOpen = true</c> (through a Dispatcher.BeginInvoke) and arrow keys between the entries.
    /// </summary>
    [Fact]
    public void Defect_The_Move_To_Another_Page_Menu_Takes_The_Keyboard_When_It_Opens()
    {
        var text = Source.Read("src/Island.SettingsUi/PicksSection.cs");
        var from = text.IndexOf("private static Button MoveControl", StringComparison.Ordinal);
        var to = text.IndexOf("private static Button Chip", StringComparison.Ordinal);
        Assert.True(from > 0 && to > from);
        var method = text[from..to];
        Assert.Contains("menu.IsOpen = true", method);
        Assert.Contains(".Focus()", method);
    }

    /// <summary>
    /// ease-1-21 (MEDIUM). A yes/no question (Remove a page, Restore a starter list, Delete a scene, Connect or Disconnect a coding agent) takes the keyboard onto "No". Answering Yes rebuilds the
    /// section and puts the keyboard back on a control; answering No, or pressing Esc, only collapses the question: nothing remembers the control that had the keyboard before, and the elements
    /// that had it are gone from the keyboard's reach (the screen was disabled underneath). The keyboard is on nothing, and Tab starts again at the top of the screen (on "What goes on the island",
    /// after about 96 stops). Expected: the keyboard goes back to the control that opened the question. Smallest repair: the overlay keeps <c>Keyboard.FocusedElement</c>'s focus key when
    /// it opens and the view's <c>_overlay.Closed</c> handler calls <c>FindByKey</c> on it.
    /// </summary>
    [Fact]
    public void Defect_The_Keyboard_Goes_Back_To_The_Control_That_Opened_A_Question_When_It_Is_Answered_No()
    {
        var text = Source.Read("src/Island.SettingsUi/SettingsView.cs");
        var at = text.IndexOf("_overlay.Closed +=", StringComparison.Ordinal);
        Assert.True(at > 0);
        var handler = text[at..text.IndexOf(';', at + 30)];
        Assert.Contains("Focus", handler);
    }

    [Fact]
    public void The_Steps_At_The_Top_Are_Buttons_The_Keyboard_Reaches()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            foreach (var setup in new[] { false, true })
            {
                var view = fixture.NewView(setup);
                Tree.Layout(view, 1920, 1080);
                var steps = Tree.Of<Button>(view).Count(b => Tree.FocusKeyOf(b)?.StartsWith("step:", StringComparison.Ordinal) == true);
                Assert.Equal(setup ? 7 : 8, steps); // five until WORK-ORDER-13 added the step Try it, six until Dan's Chrome step (version 1.0.1)
            }
        });
    }

    [Fact]
    public void While_A_Key_Is_Being_Chosen_The_Screen_Swallows_Every_Key_And_Esc_Is_The_Way_Out()
    {
        // The screen's own text says so ("Esc cancels") and its PreviewKeyDown marks every key handled while capturing: a person who starts a capture by mistake is not trapped.
        Assert.Contains("Esc cancels", Island.Core.SettingsEdit.SettingsText.PressYourKeysHelp);
        var text = Source.Read("src/Island.SettingsUi/SettingsView.cs");
        Assert.Contains("e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && HandleEscape()", text);
    }
}
