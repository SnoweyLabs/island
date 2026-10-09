using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 3: the texts the repairs of round 2 changed, as they lay out (a settings view built, laid out and read on one STA thread; never shown, never rendered), and the states of the new Moving light card that round 2 did not read.</summary>
public class Round3SettingsTests(ITestOutputHelper output)
{
    // ---- the Blur note without its middle sentence -----------------------------------------------------------------------------------------------------------------------

    private sealed record Note(string Text, int Lines, double LastShare, double Width, double Top, double CardsBottom, double HintBottom);

    private static Note BlurNote(GlassKind glass, int w = 1920, int h = 1080)
    {
        return Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.Glass;
            SettingsWorld.Layout(stage);
            var note = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => t.Text.StartsWith("Blur glass is not available", StringComparison.Ordinal));
            var (lines, share) = Round2World.LinesOf(note);
            var cards = SettingsWorld.Descendants(view).OfType<Button>().Where(b => SettingsWorld.KeyOf(b) is { } k && k.StartsWith("glass:", StringComparison.Ordinal)).Select(b => SettingsWorld.BoxOf(b, stage).Bottom).Max();
            return new Note(note.Text, lines, share, note.ActualWidth, SettingsWorld.BoxOf(note, stage).Top, cards, SettingsWorld.BoxOf(note, stage).Bottom);
        }, w, h, dir => Round2World.SessionWithKeyOnAlpha(dir, glass));
    }

    [Theory]
    [InlineData(GlassKind.Approved, 1920, 1080)]
    [InlineData(GlassKind.Darker, 1920, 1080)]
    [InlineData(GlassKind.Darker, 1366, 768)]
    [InlineData(GlassKind.Darker, 1280, 720)]
    public void Record_The_Blur_Note_For_Each_Glass_In_Use(GlassKind glass, int w, int h)
    {
        var n = BlurNote(glass, w, h);
        output.WriteLine($"{glass} {w}x{h}: {n.Lines} line(s), last {n.LastShare:P0} of {n.Width:0} wide, top {n.Top:0.0} (cards end {n.CardsBottom:0.0}); {n.Text}");
        Assert.InRange(n.Lines, 1, 3);
        Assert.True(n.Top > n.CardsBottom);
    }

    /// <summary>The Darker note: the first sentence and the third, joined by one space: it ends on the tail of the third sentence, not between two sentences.</summary>
    [Fact]
    public void The_Darker_Note_Is_Three_Sentences_Since_Ease_3_9_And_Keeps_The_Glow_In_Use()
    {
        var n = BlurNote(GlassKind.Darker);
        Assert.DoesNotContain("Approved glass", n.Text);
        Assert.Equal(3, n.Text.Split(". ", StringSplitOptions.RemoveEmptyEntries).Length); // repaired in WORK-ORDER-12 (ease-3-9): the middle part says what Island does instead
        Assert.Contains("Island keeps the Darker glass you chose", n.Text);
        Assert.StartsWith("Blur glass is not available", n.Text);
        Assert.EndsWith("then pick Blur again.", n.Text);
    }

    // ---- the Moving light card, in each of its states ---------------------------------------------------------------------------------------------------------------------

    private static T? FindAncestor<T>(DependencyObject start, Func<T, bool> where) where T : DependencyObject
    {
        for (var d = VisualTreeHelper.GetParent(start); d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T t && where(t)) return t;
        return null;
    }
}

/// <summary>ROUND 3: where a refusal or warning is drawn on the sections that scroll (the notice line is the last child of each section).</summary>
public class Round3NoticeTests(ITestOutputHelper output)
{
    /// <summary>
    /// The notice line (refusals, and since round 2 the warning "the key was not given back") is the last child of the section. Where the section is longer than the room, it is under the controls
    /// that were pressed, out of sight unless the person scrolls. Recorded for the sections at three window sizes (1920 by 1080 at 100%, 1536 by 864 at 125%, 1280 by 720 at 150%).
    /// </summary>
    [Theory]
    [InlineData(SettingsSection.OnTheIsland, 1920, 1080)]
    [InlineData(SettingsSection.OnTheIsland, 1536, 864)]
    [InlineData(SettingsSection.OnTheIsland, 1280, 720)]
    [InlineData(SettingsSection.Pages, 1280, 720)]
    [InlineData(SettingsSection.Key, 1280, 720)]
    [InlineData(SettingsSection.General, 1280, 720)]
    public void Record_How_Far_Below_The_Pressed_Controls_The_Notice_Line_Is(SettingsSection section, int w, int h)
    {
        var (extent, viewport, noticeTop, firstChipTop, lastChipBottom) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = section;
            SettingsWorld.Layout(stage);
            var scroll = SettingsWorld.Descendants(view).OfType<ScrollViewer>().First();
            var notice = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => SettingsWorld.KeyOf(t) == "notice");
            var buttons = SettingsWorld.Descendants(view).OfType<Button>().Where(b => b.ActualWidth > 0 && SettingsWorld.KeyOf(b) is { } k && !k.StartsWith("foot:", StringComparison.Ordinal) && !k.StartsWith("step", StringComparison.Ordinal)).ToList();
            var noticeBox = notice.TransformToAncestor(scroll.Content as Visual ?? scroll).TransformBounds(new Rect(0, 0, notice.ActualWidth, notice.ActualHeight));
            double Top(Button b) => b.TransformToAncestor(scroll.Content as Visual ?? scroll).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight)).Top;
            double Bottom(Button b) => b.TransformToAncestor(scroll.Content as Visual ?? scroll).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight)).Bottom;
            return (scroll.ExtentHeight, scroll.ViewportHeight, noticeBox.Top, buttons.Count == 0 ? 0 : buttons.Min(Top), buttons.Count == 0 ? 0 : buttons.Max(Bottom));
        }, w, h);
        output.WriteLine($"{section} at {w}x{h}: the scrolling part holds {extent:0} and shows {viewport:0}; the notice line starts at {noticeTop:0}; the first control at {firstChipTop:0}, the last ends at {lastChipBottom:0}; the notice is {(noticeTop > viewport ? "OUT OF SIGHT until the person scrolls" : "in sight")} from the top");
        Assert.True(extent > 0);
    }
}
