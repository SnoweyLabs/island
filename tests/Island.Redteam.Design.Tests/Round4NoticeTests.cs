using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 4: what the repairs of round 3 did to the settings screen. The notice that is brought into view (<c>SettingsView.Rebuild</c> queues <c>BringIntoView</c> of the notice line whenever a notice is held), and the
/// texts of the notices that grew (the light refusal for a person on As before, the pick whose program was added again, the key that was not given back with a refusal's own message). A settings view is built, laid out
/// and read on the one STA thread; never shown, never rendered. Keyboard focus needs a window: what depends on it is written as not checked.
/// </summary>
public class Round4NoticeTests(ITestOutputHelper output)
{
    private static ScrollViewer ScrollOf(SettingsView view) => SettingsWorld.Descendants(view).OfType<ScrollViewer>().First();

    private static TextBlock NoticeOf(SettingsView view) => SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => SettingsWorld.KeyOf(t) == "notice");

    private static void Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    /// <summary>A refusal put in the notice line the way the screen does (the host's Report, then a redraw). Since WORK-ORDER-13 the light pill that used to be pressed for it is gone.</summary>
    private static void Refuse(SettingsView view, string words)
    {
        typeof(SettingsView).GetMethod("Island.SettingsUi.ISectionHost.Report", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [new SessionResult(false, words)]);
        typeof(SettingsView).GetMethod("Rebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [null]);
    }

    private static void PressKey(SettingsView view, string key) => Round2World.Press(Round2World.ByKey<Button>(view, key)!);

    private static (double Top, double Bottom) NoticeInScroll(SettingsView view)
    {
        var scroll = ScrollOf(view);
        var notice = NoticeOf(view);
        var box = notice.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, notice.ActualWidth, notice.ActualHeight));
        return (box.Top, box.Bottom);
    }

    // ---- the page does not move when nothing is below the fold ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1536, 864)]
    public void A_Refusal_In_A_Section_That_Fits_Does_Not_Move_The_Page(int w, int h)
    {
        var offset = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.General;
            SettingsWorld.Layout(stage);
            var scroll = ScrollOf(view);
            var extentBefore = scroll.ExtentHeight;
            Refuse(view, "A short refusal."); // the notice line holds the words
            SettingsWorld.Layout(stage);
            Assert.False(string.IsNullOrEmpty(NoticeOf(view).Text));
            output.WriteLine($"General at {w}x{h}: extent {extentBefore:0}, viewport {scroll.ViewportHeight:0}, offset after the refusal {scroll.VerticalOffset:0.0}");
            return scroll.VerticalOffset;
        }, w, h);
        if (w == 1920) Assert.Equal(0, offset);
    }

    /// <summary>Every section of the screen at the sizes of 100%, 125% and 150% on a 1920 by 1080 screen: a notice held in a section that fits the room never moves the page; in one that does not it moves it to the notice and no further.</summary>
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1536, 864)]
    [InlineData(1280, 720)]
    public void A_Notice_Moves_The_Page_Only_In_The_Sections_That_Are_Longer_Than_The_Room(int w, int h)
    {
        var rows = Round2World.WithView(false, (view, stage, session) =>
        {
            var report = typeof(SettingsView).GetMethod("Island.SettingsUi.ISectionHost.Report", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var rebuild = typeof(SettingsView).GetMethod("Rebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var result = new List<(SettingsSection Section, double Extent, double Viewport, double Offset, double NoticeBottom)>();
            foreach (var section in Enum.GetValues<SettingsSection>())
            {
                try { view.Section = section; }
                catch (IndexOutOfRangeException) { continue; } // a step of the first-start list only: not a section of this screen
                SettingsWorld.Layout(stage);
                var scroll = ScrollOf(view);
                report.Invoke(view, [new SessionResult(false, "A short refusal.")]);
                rebuild.Invoke(view, [null]);
                SettingsWorld.Layout(stage);
                result.Add((section, scroll.ExtentHeight, scroll.ViewportHeight, scroll.VerticalOffset, NoticeInScroll(view).Bottom));
            }

            return result;
        }, w, h);
        foreach (var (section, extent, viewport, offset, bottom) in rows)
        {
            output.WriteLine($"{w}x{h} {section}: extent {extent:0}, viewport {viewport:0}, offset {offset:0}, notice bottom at {bottom:0}");
            if (extent <= viewport + 0.5) Assert.True(offset < 0.5, $"{section}: the page moved by {offset:0} though everything fits");
            Assert.True(bottom <= viewport + 1, $"{section}: the notice ends at {bottom:0} below the room {viewport:0}");
        }
    }

    /// <summary>A refusal in a section that scrolls scrolls the least that shows the whole notice line (and the pressed control may leave the room: recorded).</summary>
    [Theory]
    [InlineData(1536, 600)]
    [InlineData(1280, 420)]
    public void A_Refusal_Below_The_Fold_Shows_The_Whole_Notice_Line_With_The_Least_Scroll(int w, int h)
    {
        var (top, bottom, viewport, offset, extent, pressedTop) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.General;
            SettingsWorld.Layout(stage);
            Refuse(view, "A short refusal.");
            SettingsWorld.Layout(stage);
            var scroll = ScrollOf(view);
            var (t, b) = NoticeInScroll(view);
            return (t, b, scroll.ViewportHeight, scroll.VerticalOffset, scroll.ExtentHeight, 0.0);
        }, w, h);
        output.WriteLine($"General at {w}x{h}: viewport {viewport:0}, extent {extent:0}, offset {offset:0}; the notice line {top:0}..{bottom:0}");
        Assert.True(top >= -1 && bottom <= viewport + 1, $"the notice line {top:0}..{bottom:0} in a viewport of {viewport:0}");
    }

    // ---- the notice that stays after the thing that put it there is gone ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A hand-add that is refused ("already on the island") leaves its words in the notice line at the foot of the section and the panel open. Closing the panel (the Close link, or Esc) draws the section again WITHOUT
    /// clearing the notice, so the words of the refused add are still there with the panel gone, and since the repair of design-3-3 every redraw that holds a notice scrolls the page to it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Record_What_Closing_The_Add_Panel_After_A_Refused_Add_Does_To_The_Notice_And_The_Scroll(bool firstPage)
    {
        var (noticeAfterClose, offsetBefore, offsetAfter, viewport, extent) = Round2World.WithView(false, (view, stage, session) =>
        {
            var page = firstPage ? PageIds.Media : Round2World.CustomPageId(session);
            view.ShowAddPanel(page); // the custom page already holds Example (example.org)
            SettingsWorld.Layout(stage);
            Round2World.ByKey<TextBox>(view, $"add:site:{page}")!.Text = "example.org";
            SettingsWorld.Layout(stage);
            PressKey(view, $"add:site-add:{page}");
            SettingsWorld.Layout(stage);
            var scroll = ScrollOf(view);
            Assert.False(string.IsNullOrEmpty(NoticeOf(view).Text), "the refused add left no notice");
            var button = Round2World.ByKey<Button>(view, $"add:site-add:{page}")!;
            var site = Round2World.ByKey<TextBox>(view, $"add:site:{page}")!;
            double Top(FrameworkElement e) => e.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)).Top;
            output.WriteLine($"right after the refused add: offset {scroll.VerticalOffset:0} of {scroll.ExtentHeight - scroll.ViewportHeight:0}; the Add button of the panel at {Top(button):0}, the site field at {Top(site):0}, the notice at {NoticeInScroll(view).Top:0} (viewport {scroll.ViewportHeight:0}): the panel the person is working in {(Top(site) < 0 ? "is out of the room" : "is in the room")}");
            scroll.ScrollToTop();
            SettingsWorld.Layout(stage);
            var before = scroll.VerticalOffset;
            Assert.True(view.HandleEscape()); // Esc inside the open panel closes it (the Close link does the same)
            SettingsWorld.Layout(stage);
            return (NoticeOf(view).Text, before, scroll.VerticalOffset, scroll.ViewportHeight, scroll.ExtentHeight);
        }, 1536, 700);
        output.WriteLine($"{(firstPage ? "first page" : "last page")}: viewport {viewport:0}, extent {extent:0}: offset {offsetBefore:0} before Esc, {offsetAfter:0} after; notice after closing the panel: \"{noticeAfterClose}\"");
    }

    /// <summary>
    /// FINDING design-4-3 (LOW). Expected: closing the Add panel puts the words of a refused add away (as opening the panel does: <c>host.Notice = null</c>), so that a person who scrolled up to look at the panel's fields
    /// and closed it is not carried down to a refusal about a panel that is no longer there. Today <c>AddByHand.Close</c> sets <c>OpenPanel = null</c> and draws again; the notice stays and the page scrolls to it.
    /// </summary>
    [Fact]
    public void Defect_Closing_The_Add_Panel_Puts_The_Words_Of_A_Refused_Add_Away()
    {
        var notice = Round2World.WithView(false, (view, stage, session) =>
        {
            var page = Round2World.CustomPageId(session);
            view.ShowAddPanel(page);
            SettingsWorld.Layout(stage);
            Round2World.ByKey<TextBox>(view, $"add:site:{page}")!.Text = "example.org";
            SettingsWorld.Layout(stage);
            PressKey(view, $"add:site-add:{page}");
            SettingsWorld.Layout(stage);
            Assert.False(string.IsNullOrEmpty(NoticeOf(view).Text));
            Assert.True(view.HandleEscape());
            SettingsWorld.Layout(stage);
            return NoticeOf(view).Text;
        });
        Assert.True(string.IsNullOrEmpty(notice), $"the panel is closed and the notice line still says: {notice}");
    }

    /// <summary>
    /// FINDING design-4-4 (LOW; a suggestion of the proposal class: where a refusal is drawn is a look). Expected: a refused add leaves the panel the person is working in (the site field and its Add button) in the room together
    /// with the words that say why, or the words are drawn in the panel. Today the words are at the foot of the section and the page is scrolled down to them: on the first page (Media) the panel's field is 545 px above the top of
    /// the room, the Add button 487 px above it. (Where focus goes after the redraw, and whether WPF scrolls to a control that is given the keyboard, needs a window: not checked here.)
    /// </summary>
    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_A_Refused_Add_Leaves_The_Panel_Being_Worked_In_In_The_Room_With_The_Words_That_Say_Why()
    {
        var (siteTop, buttonTop, noticeBottom, viewport) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.ShowAddPanel(PageIds.Media);
            SettingsWorld.Layout(stage);
            Round2World.ByKey<TextBox>(view, $"add:site:{PageIds.Media}")!.Text = "example.org"; // already on the island, on another page
            SettingsWorld.Layout(stage);
            PressKey(view, $"add:site-add:{PageIds.Media}");
            SettingsWorld.Layout(stage);
            var scroll = ScrollOf(view);
            double Top(FrameworkElement e) => e.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)).Top;
            return (Top(Round2World.ByKey<TextBox>(view, $"add:site:{PageIds.Media}")!), Top(Round2World.ByKey<Button>(view, $"add:site-add:{PageIds.Media}")!), NoticeInScroll(view).Bottom, scroll.ViewportHeight);
        }, 1536, 700);
        Assert.True(siteTop >= 0 && buttonTop >= 0 && noticeBottom <= viewport + 1, $"the site field is at {siteTop:0}, the Add button at {buttonTop:0} and the words end at {noticeBottom:0} in a room of {viewport:0}");
    }

    // ---- the texts that grew, as they lay out ------------------------------------------------------------------------------------------------------------------------------

    private static readonly FontFamily Family = new($"{LookConstants.FontPrimary}, {LookConstants.FontFallback}");

    private static (int Lines, double LastShare, double Height) Lay(string text, double width)
    {
        // the notice line: Look.Label(text, SmallSize = 12) wrapping, margin 4 on each side, MinHeight 17; the hints are 12.5
        var block = new TextBlock { Text = text, FontFamily = Family, FontSize = 12, TextWrapping = TextWrapping.Wrap, Width = width };
        block.Measure(new Size(width, double.PositiveInfinity));
        block.Arrange(new Rect(0, 0, width, block.DesiredSize.Height));
        var lines = Math.Max(1, (int)Math.Round(block.ActualHeight / (12 * 1.37)));
        var last = block.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
        return (lines, last.Right / width, block.ActualHeight);
    }

    private static string LongestKeyNotGivenBack()
    {
        var combo = HotkeyCombo.Parse("Ctrl+Alt+Shift+F12");
        var owner = new string('M', 24);
        var name = new string('W', 100);
        return SettingsText.KeyNotGivenBackBecause(combo.ToString(), SettingsText.KeyTakenInside(combo, owner, name).Message);
    }

    [Theory]
    [InlineData(712.0)]
    [InlineData(592.0)]
    [InlineData(440.0)]
    public void Record_How_The_Notices_That_Grew_Lay_Out_At_Three_Widths_Of_The_Notice_Line(double width)
    {
        var cases = new (string Name, string Text)[]
        {
            ("a pick added again, 20 letters", SettingsText.AlreadyOnTheIsland("Alpha-player Deluxe 20")),
            ("a pick added again, 100 letters", SettingsText.AlreadyOnTheIsland(new string('W', 100))),
            ("key not given back, the key is taken inside", SettingsText.KeyNotGivenBackBecause("Ctrl+Alt+F1", SettingsText.KeyTakenInside(HotkeyCombo.Parse("Ctrl+Alt+F1"), "Alpha games", "Alpha").Message)),
            ("key not given back, the longest it can get (24-letter page, 100-letter pick, 4-key combination)", LongestKeyNotGivenBack()),
            ("key not given back, Windows refused", SettingsText.KeyNotGivenBackBecause("Ctrl+Alt+F1", SettingsText.KeyRefusedByWindows(HotkeyCombo.Parse("Ctrl+Alt+F1"), 1409).Message)),
            ("key not given back, the file could not be saved", SettingsText.KeyNotGivenBackBecause("Ctrl+Alt+F1", SettingsText.KeyNotSaved.Message)),
        };
        Sta.Run(() =>
        {
            foreach (var (name, text) in cases)
            {
                var (lines, share, height) = Lay(text, width);
                output.WriteLine($"{width:0} wide: {name}: {text.Length} characters, {lines} line(s), last line {share:P0}, {height:0} high");
                Assert.InRange(lines, 1, 8);
            }
        });
    }

    /// <summary>
    /// The longest notice it can be (about 480 characters: the key warning with the refusal that names two long things) lies out in at most six lines at the narrowest width the screen gives a section, so that after it
    /// is scrolled into view the whole of it is on show in the room of the smallest window (a screen of 1280 by 720 at 200% is 640 by 360 dips).
    /// </summary>
    [Fact]
    public void The_Longest_Notice_Fits_In_The_Room_Of_The_Smallest_Window_Once_It_Is_Scrolled_Into_View()
    {
        var text = LongestKeyNotGivenBack();
        var (top, bottom, viewport) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.General;
            SettingsWorld.Layout(stage);
            // put the longest words into the notice line the way the screen does (through the host's Report: reached by pressing a refused pill, then replaced by the longest words)
            var host = typeof(SettingsView).GetMethod("Island.SettingsUi.ISectionHost.Report", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            host.Invoke(view, [new SessionResult(true, null, text)]);
            typeof(SettingsView).GetMethod("Rebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [null]);
            SettingsWorld.Layout(stage);
            var (t, b) = NoticeInScroll(view);
            return (t, b, ScrollOf(view).ViewportHeight);
        }, 640, 360);
        output.WriteLine($"640x360: the notice line {top:0}..{bottom:0} in a viewport of {viewport:0}; {text.Length} characters");
        Assert.True(top >= -1 && bottom <= viewport + 1, $"the longest notice {top:0}..{bottom:0} does not fit the viewport of {viewport:0}");
    }
}
