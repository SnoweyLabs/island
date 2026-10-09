using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 4: the settings screen and the first start laid out at 200% and 300% of a 1080p screen (960 x 540 and 640 x 360 device-independent units), every section, with the person's own things on the
/// pages (an invented page with a long name). Built and arranged on the one STA thread, never shown. Invented names only.
/// </summary>
public class Round4LayoutTests
{
    private static IEnumerable<(string Name, SettingsSection Section, bool Setup)> Everything() =>
        Tree.FullSections.Select(s => ($"settings/{s}", s, false)).Concat(Tree.SetupSections.Select(s => ($"setup/{s}", s, true)));

    private static string Overflow(Func<SettingsView, string?> look, double width, double height)
    {
        var problems = new List<string>();
        foreach (var (name, section, setup) in Everything())
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView(setup);
            view.Section = section;
            Tree.Layout(view, width, height);
            if (look(view) is { } problem) problems.Add($"{name}: {problem}");
        }

        return string.Join("; ", problems);
    }

    private static string? HorizontalOverflow(SettingsView view)
    {
        var scroll = Tree.Of<ScrollViewer>(view).First();
        return scroll.ExtentWidth > scroll.ViewportWidth + 1 ? $"extent {scroll.ExtentWidth:0} wider than the viewport {scroll.ViewportWidth:0}" : null;
    }

    private static string? ContinueOutOfSight(SettingsView view)
    {
        var next = Tree.ButtonKeyed(view, "foot:next");
        var bounds = Tree.BoundsIn(next, view);
        return bounds.Left < -1 || bounds.Right > view.ActualWidth + 1 || bounds.Top < -1 || bounds.Bottom > view.ActualHeight + 1 ? $"Continue/Done is at {bounds}" : null;
    }

    private static string? ScrollRoom(SettingsView view)
    {
        var scroll = Tree.Of<ScrollViewer>(view).First();
        return scroll.ViewportHeight < 120 ? $"the page scrolls in {scroll.ViewportHeight:0} units" : null;
    }

    /// <summary>Held: at 200% (960 x 540) no section and no first-start step is wider than the room, the Continue/Done button is on the screen, and the page keeps a viewport to scroll in.</summary>
    [Fact]
    public void At_200_Percent_No_Section_Is_Wider_Than_The_Room_And_The_Foot_Stays_On_The_Screen()
    {
        Sta.Run(() =>
        {
            Assert.Equal(string.Empty, Overflow(HorizontalOverflow, 960, 540));
            Assert.Equal(string.Empty, Overflow(ContinueOutOfSight, 960, 540));
            Assert.Equal(string.Empty, Overflow(ScrollRoom, 960, 540));
        });
    }

    /// <summary>Held: the same at 300% (640 x 360), the largest scaling Windows offers.</summary>
    [Fact]
    public void At_300_Percent_No_Section_Is_Wider_Than_The_Room_And_The_Foot_Stays_On_The_Screen()
    {
        Sta.Run(() =>
        {
            Assert.Equal(string.Empty, Overflow(HorizontalOverflow, 640, 360));
            Assert.Equal(string.Empty, Overflow(ContinueOutOfSight, 640, 360));
        });
    }

    /// <summary>Held: every text of every section at 200% is measured at the width it is given: nothing that wraps is cut off at its own height (its desired height is no more than what it was given).</summary>
    [Fact]
    public void At_200_Percent_No_Wrapping_Text_Is_Cut_At_Its_Own_Height()
    {
        Sta.Run(() =>
        {
            var cut = Overflow(view =>
            {
                var scroll = Tree.Of<ScrollViewer>(view).First();
                var bad = Tree.Of<TextBlock>(scroll).Where(t => t.IsVisible && t.TextWrapping != TextWrapping.NoWrap && t.ActualWidth > 0 && t.ActualHeight + 1 < t.DesiredSize.Height).Select(t => t.Text).Take(3).ToList();
                return bad.Count == 0 ? null : "cut: " + string.Join(" | ", bad.Select(b => b.Length > 30 ? b[..30] : b));
            }, 960, 540);
            Assert.Equal(string.Empty, cut);
        });
    }

    /// <summary>
    /// Premise of ease-4-1 (UNVERIFIED without a screen reader): the section's heading is a new element at every build, and WPF makes a peer for an element only when something asks for it
    /// (<c>CreatePeerForElement</c>; Learn: FromElement gives null when the peer was not created that way). Nothing here asks, so <c>FromElement</c> is null when the posted event is meant to
    /// be raised, and nothing is raised. Whether a screen reader that is attached has asked by then is the open question.
    /// </summary>
    [Fact]
    public void A_Heading_Built_By_Continue_Has_No_Peer_Until_Something_Asks_For_One()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView(setup: true);
            Tree.Layout(view, 1920, 1080);
            view.PressContinue();
            Tree.Layout(view, 1920, 1080);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            var heading = Tree.Of<TextBlock>(view).First(t => AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite && string.IsNullOrEmpty(Tree.FocusKeyOf(t)));
            Assert.Null(UIElementAutomationPeer.FromElement(heading));
            Assert.NotNull(UIElementAutomationPeer.CreatePeerForElement(heading));
            Assert.NotNull(UIElementAutomationPeer.FromElement(heading));
        });
    }
}

/// <summary>Round 4: the steps at the top of the screen are buttons the keyboard reaches; at the largest scalings every one of them must still be on the screen when it takes the keyboard.</summary>
public class Round4StepsTests
{
    private static string Outside(double width, double height)
    {
        var lost = new List<string>();
        foreach (var setup in new[] { false, true })
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView(setup);
            Tree.Layout(view, width, height);
            foreach (var button in Tree.Of<Button>(view).Where(b => b.IsVisible && b.Focusable && b.IsEnabled))
            {
                var scroll = Tree.Of<ScrollViewer>(view).First();
                if (Tree.Descendants(scroll).Contains(button)) continue; // the page scrolls: a focus brings its control into view
                var bounds = Tree.BoundsIn(button, view);
                if (bounds.Left < -1 || bounds.Right > width + 1 || bounds.Top < -1 || bounds.Bottom > height + 1) lost.Add($"{(setup ? "setup" : "settings")}: {AutomationProperties.GetName(button)} at {bounds.Left:0}..{bounds.Right:0}");
            }
        }

        return string.Join("; ", lost);
    }

    [Fact]
    public void At_200_And_300_Percent_Every_Button_Outside_The_Scrolling_Page_Is_On_The_Screen()
    {
        Sta.Run(() =>
        {
            Assert.Equal(string.Empty, Outside(960, 540));
            Assert.Equal(string.Empty, Outside(640, 360));
        });
    }
}
