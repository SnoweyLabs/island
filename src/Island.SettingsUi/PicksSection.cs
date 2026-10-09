using System.Windows;
using System.Windows.Controls;
using Island.Core;
using Island.Core.SettingsEdit;
using CorePage = Island.Core.Page;

namespace Island.SettingsUi;

/// <summary>
/// "On the island": for each page its picks as switches (a chip that is lit when the pick is on the island), beside each
/// pick that is on the island a control that names its page and lets the person choose another (the right-click menu that
/// did this is gone, WORK-ORDER-5 §6), and "Restore starter list" per page after a yes/no question.
/// </summary>
internal static class PicksSection
{
    public static FrameworkElement Build(ISectionHost host)
    {
        var stack = new StackPanel();
        var first = true;
        foreach (var page in host.Session.Pages.Pages)
        {
            var card = PageCard(host, page);
            card.Margin = new Thickness(0, first ? 0 : 12, 0, 0);
            stack.Children.Add(card);
            first = false;
        }

        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }

    private static Border PageCard(ISectionHost host, CorePage page)
    {
        var session = host.Session;
        var body = new StackPanel();

        var head = new Grid { Margin = new Thickness(16, 6, 8, 0), MinHeight = 28 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Look.Label(page.Name, Look.SmallSize, brush: Look.Sub);
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        if (!PageIds.CanHoldPicks(page.Id))
        {
            // The page that fills itself holds no pick: one sentence, no Add..., no list (WORK-ORDER-11 section 1).
            body.Children.Add(head);
            var fills = Look.Label(SettingsText.PageFillsItself, Look.SmallSize, brush: Look.Sub);
            fills.Margin = new Thickness(16, 6, 16, 14);
            body.Children.Add(fills);
            return new Border
            {
                Background = Look.Group,
                BorderBrush = Look.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Look.GroupRadius),
                Child = body,
            };
        }

        actions.Children.Add(AddByHand.Toggle(host, page));
        if (session.AskRestore(page.Id) is { } question)
        {
            var id = page.Id;
            actions.Children.Add(Parts.Link("Restore starter list", host.Accent, () =>
                host.Ask(question.Text, "Yes, restore it", () =>
                {
                    host.Report(session.RestorePage(id));
                    host.Refresh($"restore:{id}");
                }), $"restore:{id}", $"Restore the starter list of {page.Name}"));
        }

        Grid.SetColumn(actions, 1);
        head.Children.Add(actions);
        body.Children.Add(head);
        if (host.OpenPanel == AddByHand.PanelOf(page.Id)) body.Children.Add(AddByHand.Build(host, page));

        var rows = session.PickRows(page.Id);
        if (rows.Count == 0)
        {
            var empty = Look.Label("Nothing is on this page yet. Press + on the island to add something.", Look.SmallSize, brush: Look.Sub);
            empty.Margin = new Thickness(16, 6, 16, 14);
            body.Children.Add(empty);
        }
        else
        {
            var chips = new WrapPanel { Margin = new Thickness(16, 6, 8, 6) };
            foreach (var row in rows) chips.Children.Add(WithMove(host, page, row));
            body.Children.Add(chips);
        }

        return new Border
        {
            Background = Look.Group,
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Child = body,
        };
    }

    /// <summary>A pick's switch, and for a pick that is on the island the control that moves it to another page.</summary>
    private static FrameworkElement WithMove(ISectionHost host, CorePage page, OnIslandRow row)
    {
        var chip = Chip(host, page, row);
        if (!row.On) return chip;

        var pair = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 8) };
        chip.Margin = new Thickness(0);
        pair.Children.Add(chip);
        pair.Children.Add(MoveControl(host, page, row));
        pair.Children.Add(KeyControl(host, row));
        return pair;
    }

    /// <summary>
    /// The pick's own key (WORK-ORDER-6 section 4), empty until set: pressing it waits for a combination; the small "x" beside a key
    /// takes it away. Pressing the key does what a click on the pick does.
    /// </summary>
    private static FrameworkElement KeyControl(ISectionHost host, OnIslandRow row)
    {
        var id = row.Pick.Id;
        var holder = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var cap = KeySection.KeyButton(host, id, row.Pick.Name, small: true);
        cap.Height = 32;
        holder.Children.Add(cap);
        if (host.Session.Settings.PickKeyFor(id) is not null)
        {
            var clear = Parts.Link("Clear", host.Accent, () =>
            {
                host.CancelCapture();
                host.Report(host.Session.ClearKey(id));
                host.Refresh($"key:{id}");
            }, $"key:clear:{id}", $"Clear the key of {row.Pick.Name}");
            holder.Children.Add(clear);
        }

        return holder;
    }

    /// <summary>
    /// A small button that names the pick's page ("Apps ▾"); pressing it lists the other pages and choosing one moves the pick there,
    /// saved at once. Nothing that is open is touched.
    /// </summary>
    private static Button MoveControl(ISectionHost host, CorePage page, OnIslandRow row)
    {
        var key = $"move:{row.Pick.Id}";
        var label = Look.Label($"{page.Name} ▾", Look.SmallSize, brush: Look.Sub);
        label.TextWrapping = TextWrapping.NoWrap;
        var holder = new Border { Child = label, Padding = new Thickness(8, 0, 8, 0), Height = 32 };
        label.VerticalAlignment = VerticalAlignment.Center;
        Button? button = null;
        button = Parts.Flat(holder, 16, () =>
        {
            var others = host.Session.Pages.Pages.Where(p => p.Id != page.Id && PageIds.CanHoldPicks(p.Id)).ToList();
            if (others.Count == 0) return;
            var menu = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
            };
            var list = new StackPanel { Margin = new Thickness(6) };
            foreach (var other in others)
            {
                var id = other.Id;
                var item = Parts.Flat(new Border { Child = Look.Label(other.Name, Look.SmallSize + 1), Padding = new Thickness(12, 0, 12, 0), Height = 30 }, 14, () =>
                {
                    menu.IsOpen = false;
                    host.Report(host.Session.MovePick(row.Pick.Id, id));
                    host.Refresh(key);
                }, $"move:{row.Pick.Id}:{id}", $"Move {row.Pick.Name} to {other.Name}");
                list.Children.Add(item);
            }

            menu.Child = new Border
            {
                Background = Look.DialogFill,
                BorderBrush = Look.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = list,
                Margin = new Thickness(0, 4, 0, 12),
            };
            // The keyboard goes into the list when it opens (a popup's content is not in the window's tab sequence): Up and Down move between the pages, Enter chooses, Esc closes the list only
            // and gives the keyboard back to the button.
            list.PreviewKeyDown += (_, e) =>
            {
                var entries = list.Children.OfType<UIElement>().ToList();
                var at = entries.FindIndex(x => x.IsKeyboardFocusWithin);
                switch (e.Key)
                {
                    case System.Windows.Input.Key.Down:
                        entries[(at + 1) % entries.Count].Focus();
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Up:
                        entries[(Math.Max(at, 0) + entries.Count - 1) % entries.Count].Focus();
                        e.Handled = true;
                        break;
                    case System.Windows.Input.Key.Escape:
                        menu.IsOpen = false;
                        button?.Focus();
                        e.Handled = true;
                        break;
                }
            };
            menu.IsOpen = true;
            menu.Dispatcher.BeginInvoke(() => list.Children[0].Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }, key, $"{row.Pick.Name} is on {page.Name}. Press to move it to another page.", "#12FFFFFF");
        button.Height = 32;
        button.Margin = new Thickness(2, 0, 0, 0);
        return button;
    }

    /// <summary>One switch (<c>.chip</c>): a dot and a name; faint when off, lit with the page's colour when on.</summary>
    private static Button Chip(ISectionHost host, CorePage page, OnIslandRow row)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = Look.BrushOf(page.Color),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var label = Look.Label(row.Pick.Name, 13);
        label.TextWrapping = TextWrapping.NoWrap;
        content.Children.Add(label);

        var key = $"chip:{row.Pick.Id}";
        var chip = Parts.Flat(content, 16, () =>
        {
            host.Report(host.Session.SetPick(row, !row.On));
            host.Refresh(key);
        }, key, $"{row.Pick.Name}: {(row.On ? "on the island" : "off")}. Press to switch.", "#12FFFFFF");
        chip.Height = 32;
        chip.Padding = new Thickness(12, 0, 12, 0);
        chip.Margin = new Thickness(0, 0, 8, 8);
        chip.BorderThickness = new Thickness(1);
        chip.BorderBrush = row.On ? Look.Mix(page.Color, 55) : Look.Line;
        chip.Background = row.On ? Look.Mix(page.Color, 13) : System.Windows.Media.Brushes.Transparent;
        chip.Opacity = row.On ? 1 : 0.55;
        return chip;
    }
}
