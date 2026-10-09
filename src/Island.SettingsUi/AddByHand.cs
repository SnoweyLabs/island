using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Island.Core;
using Island.Core.SettingsEdit;
using CorePage = Island.Core.Page;

namespace Island.SettingsUi;

/// <summary>
/// The panel that "Add…" opens inside a page's card in "What goes on the island" (WORK-ORDER-10 section 3): a program (a list of the
/// installed ones, by name, narrowed by a field, and "Browse…"), a folder, a website and a file. It shows names only: no path is ever drawn here,
/// and what is typed in the website field lives in that field alone (the screen shows the host the session understood, never the text back).
/// Everything is added through <see cref="SettingsSession"/>; Windows' own choosing windows are reached by the session, never from here.
/// </summary>
internal static class AddByHand
{
    /// <summary><see cref="ISectionHost.OpenPanel"/> is "add:" and the page's id while a page's panel is open (the Pages and Scenes sections keep their own values there).</summary>
    public const string PanelPrefix = "add:";

    /// <summary>(Claude) The list shows about this many rows before it scrolls.</summary>
    public const int RowsShown = 8;

    /// <summary>(Claude) One row of the list.</summary>
    public const double RowHeight = 30;

    /// <summary>(Claude) At most this many rows are drawn at once, so that a long list does not slow a keystroke; the rest say "type to narrow the list".</summary>
    public const int MaxRowsBuilt = 100;

    private const double LabelWidth = 112;

    public static string PanelOf(string pageId) => PanelPrefix + pageId;

    /// <summary>The page whose panel <paramref name="openPanel"/> names, or null when it is not an Add panel.</summary>
    public static string? PageOf(string? openPanel) =>
        openPanel is not null && openPanel.StartsWith(PanelPrefix, StringComparison.Ordinal) ? openPanel[PanelPrefix.Length..] : null;

    /// <summary>The "Add…" button of a page's head row: opens the panel, or closes it when it is open.</summary>
    public static Button Toggle(ISectionHost host, CorePage page)
    {
        var id = page.Id;
        return Parts.Link("Add…", host.Accent, () =>
        {
            var open = host.OpenPanel == PanelOf(id);
            host.OpenPanel = open ? null : PanelOf(id);
            host.Notice = null;
            host.Refresh(open ? $"add:{id}" : $"add:filter:{id}");
        }, $"add:{id}", $"Add something to {page.Name}: a program, a folder, a website or a file");
    }

    /// <summary>The open panel, with a hairline above and below it.</summary>
    public static UIElement Build(ISectionHost host, CorePage page)
    {
        var id = page.Id;
        var panel = new StackPanel();
        panel.Children.Add(Section("A program", ProgramPart(host, page)));
        panel.Children.Add(Section("A folder", Choose("Choose a folder…", $"add:folder:{id}", $"Choose a folder to add to {page.Name}", () => host.Session.AddFolder(id), host, id)));
        panel.Children.Add(Section("A website", SitePart(host, page)));
        panel.Children.Add(Section("A file", Choose("Choose a file…", $"add:file:{id}", $"Choose a file to add to {page.Name}", () => host.Session.AddFile(id), host, id)));

        panel.Children.Add(KeySection.PanelNoticeLine(host)); // a refused add says why here, next to the field that was used, and the page does not run down to the foot
        var close = Parts.Link("Close", host.Accent, () => Close(host, id), $"add:close:{id}", $"Close the choices for adding to {page.Name}");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 6, -8, 0);
        panel.Children.Add(close);

        return new Border
        {
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(16, 6, 16, 8),
            Margin = new Thickness(0, 6, 0, 0),
            Child = panel,
        };
    }

    /// <summary>Closes the panel and puts the keyboard back on the page's "Add…" button.</summary>
    public static void Close(ISectionHost host, string pageId)
    {
        host.OpenPanel = null;
        host.Notice = null; // the words of a refused add belong to the panel that is closed
        host.Refresh($"add:{pageId}");
    }

    // ---- The four parts -----------------------------------------------------------

    private static FrameworkElement ProgramPart(ISectionHost host, CorePage page)
    {
        var id = page.Id;
        var filter = Field("Type to narrow the list of programs", $"add:filter:{id}", 64);
        var list = new StackPanel();
        KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Once);
        var scroll = new ScrollViewer
        {
            Content = list,
            MaxHeight = RowsShown * RowHeight + 8,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Margin = new Thickness(0, 6, 0, 6),
            Padding = new Thickness(4),
        };
        PassWheelOn(scroll);

        void Fill()
        {
            list.Children.Clear();
            var found = host.Session.ProgramsToChoose(filter.Text);
            foreach (var program in found.Take(MaxRowsBuilt))
            {
                var chosen = program;
                list.Children.Add(Row(chosen.Name, $"Add {chosen.Name} to {page.Name}", () => Apply(host, id, host.Session.AddProgramFromList(id, chosen))));
            }

            if (found.Count == 0) list.Children.Add(Note("No program matches."));
            else if (found.Count > MaxRowsBuilt) list.Children.Add(Note($"{found.Count - MaxRowsBuilt} more. Type to narrow the list."));
        }

        filter.TextChanged += (_, _) => Fill();
        filter.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Down or Key.Enter)) return;
            var rows = list.Children.OfType<Button>().ToList();
            if (rows.Count == 0) return;
            e.Handled = true;
            if (e.Key == Key.Enter && rows.Count == 1) rows[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else rows[0].Focus();
        };
        Fill();

        var browse = Parts.Pill("Browse…", false, () => Apply(host, id, host.Session.AddProgramByBrowsing(id)), $"add:browse:{id}");
        System.Windows.Automation.AutomationProperties.SetName(browse, $"Browse for a program or shortcut that is not in the list, to add to {page.Name}");
        browse.HorizontalAlignment = HorizontalAlignment.Left;

        var part = new StackPanel();
        part.Children.Add(filter);
        part.Children.Add(scroll);
        part.Children.Add(browse);
        return part;
    }

    private static FrameworkElement SitePart(ISectionHost host, CorePage page)
    {
        var id = page.Id;
        const string Hint = "Type a site's name, for example example.org.";
        var box = Field("Address of the website", $"add:site:{id}", TypedSite.MaxTypedChars);
        var line = Look.Label(Hint, Look.SmallSize, brush: Look.Sub);
        line.Margin = new Thickness(2, 6, 0, 6);
        var add = Parts.Pill("Add", true, () => { }, $"add:site-add:{id}");
        System.Windows.Automation.AutomationProperties.SetName(add, $"Add the website to {page.Name}");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.IsEnabled = false;

        // The field's text is read here and handed to the session; what is shown is the hint, the host the session understood, or the refusal's reason.
        void Commit()
        {
            if (host.Session.UnderstandSite(box.Text).Ok) Apply(host, id, host.Session.AddSite(id, box.Text));
        }

        add.Click += (_, _) => Commit();
        box.TextChanged += (_, _) =>
        {
            var understood = host.Session.UnderstandSite(box.Text);
            add.IsEnabled = understood.Ok;
            line.Text = box.Text.Length == 0 ? Hint
                : understood.Ok ? $"Island will keep: {understood.Host}"
                : $"{understood.Refusal!.Why} {understood.Refusal.NextAction}";
            line.Foreground = box.Text.Length == 0 ? Look.Sub : understood.Ok ? Look.Text : Look.Warn;
            System.Windows.Automation.AutomationProperties.SetHelpText(box, line.Text);
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Commit();
        };
        System.Windows.Automation.AutomationProperties.SetHelpText(box, Hint);

        var part = new StackPanel();
        part.Children.Add(box);
        part.Children.Add(line);
        part.Children.Add(add);
        return part;
    }

    /// <summary>A folder or a file: one button that asks Windows' own window (through the session) and adds the answer.</summary>
    private static FrameworkElement Choose(string text, string focusKey, string name, Func<HandAddResult> ask, ISectionHost host, string pageId)
    {
        var button = Parts.Pill(text, false, () => Apply(host, pageId, ask()), focusKey);
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        return button;
    }

    /// <summary>
    /// What an add did. Added: the panel closes and the section is drawn again (the new pick is a chip, and the keyboard goes to it); a message, a refusal or
    /// "already on the island" with its page, becomes the section's notice; a cancelled window does nothing.
    /// </summary>
    private static void Apply(ISectionHost host, string pageId, HandAddResult result)
    {
        if (result.Cancelled) return;
        if (result.Added) host.OpenPanel = null;
        host.Report(result.Added ? new SessionResult(true, null, result.Message) : new SessionResult(false, result.Message));
        host.Refresh(result.Added && result.Pick is { } pick ? $"chip:{pick.Id}" : result.Added ? $"add:{pageId}" : null);
    }

    // ---- Small pieces -------------------------------------------------------------

    private static UIElement Section(string label, FrameworkElement content)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var title = Look.Label(label, Look.SmallSize, FontWeights.SemiBold, Look.Sub);
        title.Margin = new Thickness(0, 8, 8, 0);
        title.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(content, 1);
        grid.Children.Add(title);
        grid.Children.Add(content);
        return grid;
    }

    private static Button Row(string name, string accessibleName, Action onClick)
    {
        var label = Look.Label(name, Look.BodySize);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = Parts.Flat(new Border { Child = label, Padding = new Thickness(12, 0, 12, 0), Height = RowHeight }, 14, onClick, null, accessibleName);
        row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        return row;
    }

    private static TextBlock Note(string text)
    {
        var note = Look.Label(text, Look.SmallSize, brush: Look.Sub);
        note.Margin = new Thickness(12, 6, 12, 6);
        return note;
    }

    /// <summary>A text field in the screen's own look (the same as the colour field of the Pages section).</summary>
    private static TextBox Field(string accessibleName, string focusKey, int maxLength)
    {
        var box = new TextBox
        {
            Height = 30,
            FontFamily = Look.Font,
            FontSize = Look.Scaled(Look.HintSize),
            FontWeight = FontWeights.SemiBold,
            Foreground = Look.Text,
            CaretBrush = Look.Text,
            Background = Look.CapFill,
            BorderBrush = Look.CapLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = maxLength,
            Template = Parts.RingBoxTemplate(),
        };
        FocusKey.Set(box, focusKey);
        System.Windows.Automation.AutomationProperties.SetName(box, accessibleName);
        return box;
    }

    /// <summary>
    /// The list scrolls by itself, but at its top or bottom the wheel belongs to the page behind it (a nested scroll viewer would otherwise swallow it).
    /// </summary>
    private static void PassWheelOn(ScrollViewer scroll)
    {
        scroll.PreviewMouseWheel += (_, e) =>
        {
            var atEdge = e.Delta > 0 ? scroll.VerticalOffset <= 0 : scroll.VerticalOffset >= scroll.ScrollableHeight;
            if (!atEdge || scroll.Parent is not UIElement parent) return;
            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = scroll });
        };
    }
}
