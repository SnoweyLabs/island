using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 2, part 1: what the repairs of round 1 draw in the settings screen, measured in a laid-out view that is never shown: the white ring inside the three text fields (ease-1-6), the faint chip of a
/// hand-added pick switched off (ease-1-3), the Blur note (ease-1-7), the question before "Restore the original keys" (ease-1-4). Every test passes: they are the coverage statement; what is not right is in
/// the report as a finding.
/// </summary>
public class Round2SettingsLookTests(ITestOutputHelper output)
{
    // ---- the white ring inside the fields (5ba6320) -------------------------------------------------------------------------------------------------------------------

    private sealed record RingNumbers(
        string Field, double Width, double Height, double RingWidth, double RingHeight, Thickness RingThickness, CornerRadius RingRadius, CornerRadius EdgeRadius, Thickness Hairline,
        byte RingAtRestAlpha, byte RingFocusedAlpha, double TextInset);

    private static RingNumbers Numbers(string name, TextBox box, UIElement stage)
    {
        box.ApplyTemplate();
        var ring = (Border)box.Template.FindName("Ring", box);
        var grid = (Grid)VisualTreeHelper.GetChild(box, 0);
        var edge = (Border)grid.Children[0];
        var trigger = box.Template.Triggers.OfType<Trigger>().Single();
        var setter = (Setter)trigger.Setters.Single();
        var focused = (SolidColorBrush)setter.Value;
        Assert.Equal(nameof(UIElement.IsKeyboardFocusWithin), trigger.Property.Name);
        Assert.Equal("Ring", setter.TargetName);
        var atRest = ((SolidColorBrush)ring.BorderBrush).Color.A;
        var content = SettingsWorld.BoxOf((FrameworkElement)edge.Child, box);
        return new RingNumbers(name, box.ActualWidth, box.ActualHeight, ring.ActualWidth, ring.ActualHeight, ring.BorderThickness, ring.CornerRadius, edge.CornerRadius, box.BorderThickness, atRest, focused.Color.A, content.Left);
    }

    private static List<RingNumbers> ThreeFields() => Round2World.WithView(false, (view, stage, session) =>
    {
        var list = new List<RingNumbers>();
        var custom = Round2World.CustomPageId(session);
        view.Section = SettingsSection.Pages;
        SettingsWorld.Layout(stage);
        Round2World.Press(Round2World.ByKey<Button>(view, "dot:" + custom)!); // the colour panel of the page opens when its dot is pressed
        SettingsWorld.Layout(stage);
        list.Add(Numbers("colour field (Pages)", Round2World.ByKey<TextBox>(view, "hex:" + custom)!, stage));
        view.ShowAddPanel(PageIds.Media, "nar", "example.org");
        SettingsWorld.Layout(stage);
        foreach (var box in SettingsWorld.Descendants(view).OfType<TextBox>().Where(b => SettingsWorld.KeyOf(b) is { } k && k.StartsWith("add:", StringComparison.Ordinal)))
            list.Add(Numbers("Add panel field " + SettingsWorld.KeyOf(box), box, stage));
        return list;
    });

    [Fact]
    public void The_Ring_Of_The_Text_Fields_Is_As_Large_As_The_Field_Draws_Its_Own_Corner_And_Is_Clear_At_Rest()
    {
        var fields = ThreeFields();
        foreach (var f in fields)
            output.WriteLine($"{f.Field}: field {f.Width:0.#} by {f.Height:0.#}, ring {f.RingWidth:0.#} by {f.RingHeight:0.#}, ring {f.RingThickness.Left} thick, ring radius {f.RingRadius.TopLeft}, edge radius {f.EdgeRadius.TopLeft}, hairline {f.Hairline.Left}, alpha at rest {f.RingAtRestAlpha}, focused {f.RingFocusedAlpha}, text starts {f.TextInset:0.#} in");
        Assert.True(fields.Count >= 3, "the colour field and the two fields of the Add panel were expected");
        foreach (var f in fields)
        {
            Assert.Equal(f.Width, f.RingWidth, 0.5); // the ring is exactly the field: inside its edge, not outside it as the buttons' rings are
            Assert.Equal(f.Height, f.RingHeight, 0.5);
            Assert.Equal(2.0, f.RingThickness.Left);
            Assert.Equal(f.EdgeRadius.TopLeft, f.RingRadius.TopLeft); // the same corner as the field's own edge: the two lines are concentric
            Assert.Equal(0, f.RingAtRestAlpha); // nothing is drawn until the field holds the keyboard: no picture of the self-test can have changed
            Assert.Equal(0xE6, f.RingFocusedAlpha); // the same #E6FFFFFF as the buttons' ring
            Assert.True(f.TextInset >= f.RingThickness.Left + 6, "the ring would touch the text");
        }
    }

    /// <summary>The ring of a button is drawn 3 outside the button, 2 thick, with a gap of 1 between it and the button; the ring of a field is drawn on the field's own edge and covers the field's 1 hairline. The two are told apart in numbers.</summary>
    [Fact]
    public void The_Ring_Of_A_Field_Covers_Its_Hairline_Where_The_Ring_Of_A_Button_Stands_Clear_Of_The_Button()
    {
        var (button, field) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.Pages;
            SettingsWorld.Layout(stage);
            Round2World.Press(Round2World.ByKey<Button>(view, "dot:" + Round2World.CustomPageId(session))!);
            SettingsWorld.Layout(stage);
            var next = Round2World.ByKey<Button>(view, "foot:next")!;
            next.ApplyTemplate();
            var ring = (Border)next.Template.FindName("Ring", next);
            var box = Round2World.ByKey<TextBox>(view, "hex:" + Round2World.CustomPageId(session))!;
            return (ring.Margin, Numbers("colour", box, stage));
        });
        output.WriteLine($"button ring margin {button.Left} (outside by {-button.Left}), thickness 2: gap to the button {-button.Left - 2}; field ring on the edge, thickness {field.RingThickness.Left}, hairline {field.Hairline.Left}");
        Assert.Equal(-3, button.Left);
        Assert.True(field.RingThickness.Left > field.Hairline.Left, "the ring is thicker than the hairline and so hides it while the field holds the keyboard");
    }

    /// <summary>The ring (white at 90%) against the field's own fill on the dark wall of the pictures, and over a white window behind the Settings screen (Approved glass and Darker), by WCAG's relative luminance.</summary>
    [Theory]
    [InlineData(20, 22, 32, 0.80, "dark wall, Approved")]
    [InlineData(255, 255, 255, 0.80, "white window behind, Approved")]
    [InlineData(255, 255, 255, 0.86, "white window behind, Darker")]
    public void The_Ring_Of_A_Field_Against_The_Fill_Of_The_Field_Is_Recorded(double r, double g, double b, double glassAlpha, string where)
    {
        (double R, double G, double B) Over((double R, double G, double B) bg, double a, double v) => (bg.R + (v - bg.R) * a, bg.G + (v - bg.G) * a, bg.B + (v - bg.B) * a);
        double L((double R, double G, double B) c) => 0.2126 * Pic.Lin((byte)Math.Round(c.R)) + 0.7152 * Pic.Lin((byte)Math.Round(c.G)) + 0.0722 * Pic.Lin((byte)Math.Round(c.B));
        var glass = (R: 20.0, G: 22.0, B: 32.0); // LookConstants.GlassBaseColor
        var screen = (R: r + (glass.R - r) * glassAlpha, G: g + (glass.G - g) * glassAlpha, B: b + (glass.B - b) * glassAlpha);
        var card = Over(screen, 0.08, 255); // Look.Group
        var fill = Over(card, 0.14, 255); // Look.CapFill
        var ring = Over(fill, 0.90, 255); // #E6FFFFFF
        var ratio = Pic.Ratio(L(ring), L(fill));
        output.WriteLine($"{where}: ring {ratio:0.00}:1 against the field's fill (WCAG 2.2 1.4.11 asks 3:1 for a mark that must be seen)");
        Assert.True(ratio > 1);
        Assert.True(ratio >= 3, $"{where}: {ratio:0.00}"); // WORK-ORDER-13 (Dan's P3 and P29): the tint is 0.80 and 0.86, so the ring reaches 3:1 over a white window too (it was 2.88 and 3.62 at 0.60 and 0.70)
    }

    // ---- the faint chip of a hand-added pick (19a61f0) ---------------------------------------------------------------------------------------------------------------

    private sealed record ChipNumbers(string Id, double Opacity, double Height, Thickness Margin, Color Border, Color Fill, double Top, double Left, double Width);

    [Fact]
    public void The_Faint_Chip_Of_A_Hand_Added_Pick_Is_Drawn_Exactly_As_The_Faint_Chip_Of_A_Starter_Pick_And_Sits_On_The_Same_Line_As_Its_Neighbours()
    {
        var (kept, starter, others) = Round2World.WithView(false, (view, stage, session) =>
        {
            var custom = Round2World.CustomPageId(session);
            var row = session.PickRows(custom).First(r => r.Pick.Id == Round2World.AlphaId);
            Assert.True(session.SetPick(row, false).Ok);
            view.Section = SettingsSection.OnTheIsland;
            SettingsWorld.Layout(stage);

            ChipNumbers Read(string id)
            {
                var b = Round2World.ByKey<Button>(view, "chip:" + id)!;
                var box = SettingsWorld.BoxOf(b, stage);
                Color C(Brush x) => x is SolidColorBrush s ? s.Color : Colors.Transparent;
                return new ChipNumbers(id, b.Opacity, b.ActualHeight, b.Margin, C(b.BorderBrush), C(b.Background), box.Top, box.Left, box.Width);
            }

            var keptChip = Read(Round2World.AlphaId);
            // A starter pick that is off in this invented world (its program is not installed there and it is not on the island): the first off chip of the Media page.
            var offStarter = SettingsWorld.Descendants(view).OfType<Button>().Where(b => SettingsWorld.KeyOf(b) is { } k && k.StartsWith("chip:", StringComparison.Ordinal) && b.Opacity < 1 && SettingsWorld.KeyOf(b) != "chip:" + Round2World.AlphaId).ToList();
            var starterChip = Read(SettingsWorld.KeyOf(offStarter[0])!["chip:".Length..]);
            var exampleChip = Read("site:example.org"); // the pick that is still on, on the same page: its chip is the first of a pair
            return (keptChip, starterChip, exampleChip);
        });
        output.WriteLine($"kept chip {kept}");
        output.WriteLine($"starter-off chip {starter}");
        output.WriteLine($"neighbour on the same page {others}");
        Assert.Equal(starter.Opacity, kept.Opacity);
        Assert.Equal(0.55, kept.Opacity);
        Assert.Equal(starter.Height, kept.Height);
        Assert.Equal(starter.Margin, kept.Margin);
        Assert.Equal(starter.Border, kept.Border);
        Assert.Equal(starter.Fill, kept.Fill);
        Assert.Equal(32, kept.Height);
        Assert.Equal(others.Height, kept.Height); // one height with the neighbour it sits beside
        Assert.True(kept.Left > others.Left || kept.Top > others.Top, "the faint chip comes after the chip that is still on");
    }

    /// <summary>The faint chip brings the pick back last on its page and, since ease-2-6 was repaired (WORK-ORDER-12 section 4; this test recorded "without its key" before), with the key it had.</summary>
    [Fact]
    public void A_Hand_Added_Pick_Switched_Off_And_On_Again_Comes_Back_At_The_End_Of_Its_Page_With_Its_Key()
    {
        var (before, after, keyBefore, keyAfter) = Round2World.WithView(false, (view, stage, session) =>
        {
            var custom = Round2World.CustomPageId(session);
            var order = () => session.Picks.ForPage(custom).Select(p => p.Id).ToList();
            var first = order();
            var keyThen = session.Settings.PickKeyFor(Round2World.AlphaId);
            var off = session.PickRows(custom).First(r => r.Pick.Id == Round2World.AlphaId);
            Assert.True(session.SetPick(off, false).Ok);
            var kept = session.PickRows(custom).First(r => r.Pick.Id == Round2World.AlphaId && !r.On);
            Assert.True(session.SetPick(kept, true).Ok);
            return (first, order(), keyThen, session.Settings.PickKeyFor(Round2World.AlphaId));
        }, makeSession: dir => Round2World.SessionWithKeyOnAlpha(dir));
        output.WriteLine($"order before {string.Join(", ", before)}; after {string.Join(", ", after)}; key before {keyBefore}; key after {keyAfter?.ToString() ?? "none"}");
        Assert.NotNull(keyBefore);
        Assert.Equal(Round2World.AlphaId, before[0]);
        Assert.Equal(Round2World.AlphaId, after[^1]);
        Assert.Equal(keyBefore, keyAfter);
    }

    // ---- the Blur note (e458a67) ----------------------------------------------------------------------------------------------------------------------------------------

    private sealed record NoteNumbers(int Lines, double LastShare, double Width, double Top, double CardsBottom, string Text);

    private static NoteNumbers Note(double width, double height)
    {
        return Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.Glass;
            SettingsWorld.Layout(stage);
            var note = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => t.Text == SettingsText.BlurUnavailable);
            var (lines, share) = Round2World.LinesOf(note);
            var cards = SettingsWorld.Descendants(view).OfType<Button>().Where(b => SettingsWorld.KeyOf(b) is { } k && k.StartsWith("glass:", StringComparison.Ordinal)).Select(b => SettingsWorld.BoxOf(b, stage).Bottom).Max();
            return new NoteNumbers(lines, share, note.ActualWidth, SettingsWorld.BoxOf(note, stage).Top, cards, note.Text);
        }, width, height);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1366, 768)]
    [InlineData(1280, 720)]
    public void The_Blur_Note_Is_Two_Lines_Under_The_Glass_Cards_On_Every_Screen_Size_And_Breaks_Between_Its_Sentences_Or_Near_The_End(int w, int h)
    {
        var n = Note(w, h);
        output.WriteLine($"{w}x{h}: {n.Lines} lines, last line {n.LastShare:P0} of {n.Width:0} wide, note top {n.Top:0.0}, cards end {n.CardsBottom:0.0} (gap {n.Top - n.CardsBottom:0.0})");
        output.WriteLine(n.Text);
        Assert.Equal(2, n.Lines);
        Assert.True(n.Top > n.CardsBottom, "the note lies under the glass cards");
        Assert.InRange(n.Top - n.CardsBottom, 8, 60);
        Assert.True(n.LastShare > 0.4, $"the last line is only {n.LastShare:P0} of the box");
    }

    /// <summary>The note's first line holds the first two sentences; how much room is left on it decides whether "instead." stays there or wraps alone to the second line (a different font, a text setting or a longer word would do it).</summary>
    [Fact]
    public void The_First_Line_Of_The_Blur_Note_Has_Only_A_Few_Pixels_To_Spare_Before_The_Last_Word_Of_The_Second_Sentence_Wraps()
    {
        var spare = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.Glass;
            SettingsWorld.Layout(stage);
            var note = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => t.Text == SettingsText.BlurUnavailable);
            var firstTwo = note.Text[..(note.Text.IndexOf(" Turn on", StringComparison.Ordinal))];
            var need = TextWidth.Of(firstTwo, note.FontSize, note.FontWeight, note.FontFamily.Source);
            return note.ActualWidth - need;
        });
        output.WriteLine($"room left on the first line after 'instead.': {spare:0.0} px");
        Assert.InRange(spare, 0, 40);
    }

    /// <summary>Repaired (ease-2-8 / design-2-4, WORK-ORDER-12 section 4; this test recorded that the note said "Island uses the Approved glass instead" whatever glass is in use): on the Darker glass the sentence is left out.</summary>
    [Fact]
    public void The_Blur_Note_Leaves_Out_The_Approved_Glass_When_The_Darker_Glass_Is_In_Use()
    {
        var (note, effective) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.Glass;
            SettingsWorld.Layout(stage);
            return (SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => t.Text.StartsWith("Blur glass is not available", StringComparison.Ordinal)).Text, session.EffectiveGlass);
        }, makeSession: dir => Round2World.SessionWithKeyOnAlpha(dir, GlassKind.Darker));
        output.WriteLine($"glass in use: {effective}; the note: {note}");
        Assert.Equal(GlassKind.Darker, effective);
        Assert.DoesNotContain("Approved glass", note);
    }

    // ---- the question before "Restore the original keys" (85452c5) ---------------------------------------------------------------------------------------------------

    private sealed record QuestionNumbers(string Text, int Lines, double LastShare, double CardWidth, double CardHeight, double TextWidth, double TextHeight, double Scroll, double YesWidth, double NoWidth, double Top);

    private static QuestionNumbers? Ask(string pressKey, SettingsSection section, bool setup = false)
    {
        return Round2World.WithView(setup, (view, stage, session) =>
        {
            view.Section = section;
            SettingsWorld.Layout(stage);
            var press = Round2World.ByKey<Button>(view, pressKey);
            if (press is null) return null;
            Round2World.Press(press);
            SettingsWorld.Layout(stage);
            var overlay = Round2World.OpenQuestion(view);
            if (overlay is null) return null;
            var frame = (Border)overlay.Children[0];
            var card = (StackPanel)frame.Child;
            var scroll = (ScrollViewer)card.Children[0];
            var question = (TextBlock)scroll.Content;
            var (lines, share) = Round2World.LinesOf(question);
            var buttons = (StackPanel)card.Children[1];
            var no = (FrameworkElement)buttons.Children[0];
            var yes = (FrameworkElement)buttons.Children[1];
            return new QuestionNumbers(question.Text, lines, share, frame.ActualWidth, frame.ActualHeight, question.ActualWidth, question.ActualHeight, scroll.ScrollableHeight, yes.ActualWidth, no.ActualWidth, SettingsWorld.BoxOf(frame, stage).Top);
        });
    }

    [Fact]
    public void The_Question_Before_Restoring_The_Original_Keys_Fits_The_Card_In_Three_Lines_Without_Scrolling()
    {
        var q = Ask("key:all", SettingsSection.Key);
        Assert.NotNull(q);
        output.WriteLine($"{q!.Lines} lines, last line {q.LastShare:P0}; card {q.CardWidth:0} by {q.CardHeight:0}; question {q.TextWidth:0} by {q.TextHeight:0}; scroll {q.Scroll:0}; buttons No {q.NoWidth:0}, Yes {q.YesWidth:0}");
        output.WriteLine(q.Text);
        Assert.InRange(q.CardWidth, 300, 480 + 2 * 28 + 2); // the card holds 480 of text, 28 of margin each side and a hairline
        Assert.Equal(0, q.Scroll, 0.5); // nothing is cut or scrolled
        Assert.InRange(q.Lines, 2, 4);
    }

    [Fact]
    public void The_Other_Questions_Of_The_Screen_Have_Cards_Of_The_Same_Kind_As_The_New_One()
    {
        var restoreList = Ask("restore:" + PageIds.Media, SettingsSection.OnTheIsland);
        var keys = Ask("key:all", SettingsSection.Key);
        Assert.NotNull(keys);
        if (restoreList is null) return; // the invented world's Media page may have nothing to restore
        output.WriteLine($"restore the starter list: card {restoreList.CardWidth:0} by {restoreList.CardHeight:0}, {restoreList.Lines} lines, last {restoreList.LastShare:P0}");
        output.WriteLine($"restore the keys:         card {keys!.CardWidth:0} by {keys.CardHeight:0}, {keys.Lines} lines, last {keys.LastShare:P0}");
        Assert.InRange(Math.Abs(restoreList.CardWidth - keys.CardWidth), 0, 80);
    }

    // ---- the Moving light card (WORK-ORDER-12 section 2; round 1 did not look at it) ---------------------------------------------------------------------------------

    private static T? FindAncestor<T>(DependencyObject start, Func<T, bool> where) where T : DependencyObject
    {
        for (var d = VisualTreeHelper.GetParent(start); d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T t && where(t)) return t;
        return null;
    }
}
