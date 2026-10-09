using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>
/// "Your key": the main key changed by clicking its row and pressing the new combination, a restore-default
/// control, one row for every page that sets or clears that page's key the same way (WORK-ORDER-6 section 4), and the one honest
/// sentence about private shortcuts (EVALS K9).
/// </summary>
internal static class KeySection
{
    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var stack = new StackPanel();

        stack.Children.Add(new GlassCard().Add(MainRow(host), tight: true));
        stack.Children.Add(NoticeLine(host));

        var hint = new StackPanel { Margin = new Thickness(4, 2, 4, 0) };
        var pages = Math.Min(9, session.Pages.Pages.Count);
        hint.Children.Add(Parts.Sentence($"While the island is open: [1] to [{pages}] change page · [Esc] closes"));
        foreach (var line in SettingsText.IslandKeys) hint.Children.Add(Parts.Sentence(line));
        var warning = Look.Label(SettingsText.PrivateShortcutWarning, Look.HintSize, brush: Look.Sub);
        warning.Margin = new Thickness(0, 8, 0, 0);
        FocusKey.Set(warning, "k9");
        hint.Children.Add(warning);
        stack.Children.Add(hint);

        stack.Children.Add(PageKeys(host));
        stack.Children.Add(RestoreAll(host));
        return stack;
    }

    private static UIElement MainRow(ISectionHost host)
    {
        var session = host.Session;
        var capturing = host.Capturing == KeybindEditor.MainId;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Look.Label(SettingsText.MainActionName, Look.BodySize, FontWeights.SemiBold));
        text.Children.Add(Look.Label(
            capturing ? SettingsText.PressYourKeysHelp : "The one key you will use all day. Click it, then press the new keys.",
            Look.SmallSize, brush: Look.Sub));

        var caps = capturing ? Pulse(Parts.Cap(SettingsText.PressYourKeys + "…")) : Parts.Caps(session.Settings.ShowHide.ToString());

        var inner = new Grid { Margin = new Thickness(16, 8, 8, 8) };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.Children.Add(text);
        Grid.SetColumn(caps, 1);
        inner.Children.Add(caps);

        var change = Parts.Flat(inner, Look.GroupRadius, () =>
        {
            if (capturing) host.CancelCapture(); else host.BeginCapture(KeybindEditor.MainId);
        }, "key:main", capturing ? $"{SettingsText.MainActionName}: press the new key now. Esc cancels." : $"{SettingsText.MainActionName}: {session.Settings.ShowHide}. Press to change.");
        change.MinHeight = Look.RowMinHeight;

        var restore = Parts.Link("Restore default", host.Accent, () =>
        {
            host.CancelCapture();
            host.Report(session.RestoreKey(KeybindEditor.MainId));
            host.Refresh("key:restore");
        }, "key:restore");
        restore.VerticalAlignment = VerticalAlignment.Center;
        restore.Margin = new Thickness(0, 0, 8, 0);
        restore.IsEnabled = session.Settings.ShowHide != Settings.Defaults.ShowHide;

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(change);
        Grid.SetColumn(restore, 1);
        row.Children.Add(restore);
        return row;
    }

    /// <summary>The element that holds the keyboard after the row of this action was edited.</summary>
    internal static string FocusKeyOf(string actionId) => actionId == KeybindEditor.MainId ? "key:main" : $"key:{actionId}";

    /// <summary>
    /// The key of one action as a button: its caps (or "no key"), and while the screen waits for a press, the pulsing prompt.
    /// Pressing it starts the wait; pressing it again cancels. Used for pages here and for picks in "On the island".
    /// </summary>
    internal static Button KeyButton(ISectionHost host, string actionId, string displayName, bool small = false)
    {
        var capturing = host.Capturing == actionId;
        var combo = KeybindEditor.KeyOf(host.Session.Settings, actionId);
        UIElement caps;
        if (capturing) caps = Pulse(Parts.Cap(SettingsText.PressYourKeys + "…", small));
        else if (combo is { } held) caps = Parts.Caps(held.ToString(), small);
        else
        {
            var none = Parts.Cap("no key", small);
            none.Opacity = 0.75; // 0.55 gave 4.26:1 (Dan's P7, WORK-ORDER-13)
            caps = none;
        }

        var holder = new Border { Child = caps, Padding = new Thickness(4, 2, 4, 2) };
        return Parts.Flat(holder, small ? 14 : 16, () =>
        {
            if (capturing) host.CancelCapture(); else host.BeginCapture(actionId);
        }, FocusKeyOf(actionId), capturing ? $"{displayName}: press the new key now. Esc cancels." : $"{displayName}: {(combo is { } c ? c.ToString() : "no key")}. Press to set a key.");
    }

    private static UIElement PageKeys(ISectionHost host)
    {
        var session = host.Session;
        var card = new GlassCard("A key straight to one page");
        foreach (var page in session.Pages.Pages)
        {
            var row = new Grid { VerticalAlignment = VerticalAlignment.Center, MinHeight = Look.RowMinHeight };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = Parts.Dot(page.Color);
            dot.Margin = new Thickness(0, 0, 12, 0);
            var name = Look.Label(page.Name, Look.BodySize, FontWeights.SemiBold);
            name.VerticalAlignment = VerticalAlignment.Center;
            var id = page.Id;
            var caps = KeyButton(host, id, page.Name);
            caps.Margin = new Thickness(0, 0, 4, 0);
            var clear = Parts.Link("Clear", host.Accent, () =>
            {
                host.CancelCapture();
                host.Report(session.ClearKey(id));
                host.Refresh();
            }, $"key:clear:{id}", $"Clear the key of {page.Name}");
            clear.IsEnabled = session.Settings.KeyFor(id) is not null;
            clear.Opacity = clear.IsEnabled ? 1 : 0;

            Grid.SetColumn(name, 1);
            Grid.SetColumn(caps, 2);
            Grid.SetColumn(clear, 3);
            foreach (var part in new UIElement[] { dot, name, caps, clear }) row.Children.Add(part);
            card.Add(row);
        }

        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    private static UIElement RestoreAll(ISectionHost host)
    {
        var button = Parts.Link("Restore the original keys", host.Accent, () =>
        {
            host.CancelCapture();
            void Restore()
            {
                host.Report(host.Session.RestoreAllKeys());
                host.Refresh("key:all");
            }

            // Nothing set by the person: nothing to lose, no question.
            if (host.Session.KeysSetByYou == 0) Restore();
            else host.Ask(SettingsText.RestoreAllKeysQuestion(host.Session.KeysSetByYou), "Yes, restore them", Restore, "No, keep them");
        }, "key:all");
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 10, 0, 0);
        return button;
    }

    internal static UIElement NoticeLine(ISectionHost host)
    {
        // While an Add panel is open the words of a refusal are drawn inside it, under what was pressed (Dan's P32, WORK-ORDER-13); here, at the foot of the section, there is nothing.
        if (AddByHand.PageOf(host.OpenPanel) is not null) return new Border { Height = 0 };
        return PanelNoticeLine(host);
    }

    /// <summary>The line of words that a refusal or a warning is shown in: at the foot of a section, or inside the open Add panel.</summary>
    internal static UIElement PanelNoticeLine(ISectionHost host)
    {
        var line = Look.Label(host.Notice ?? string.Empty, Look.SmallSize, brush: Look.Warn);
        line.MinHeight = 17;
        line.Margin = new Thickness(4, 6, 4, 4);
        FocusKey.Set(line, "notice");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(line, System.Windows.Automation.AutomationLiveSetting.Polite);
        return line;
    }

    // @keyframes pulse { 50% { opacity: .45 } } over one second.
    private static UIElement Pulse(UIElement element)
    {
        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1), RepeatBehavior = RepeatBehavior.Forever };
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.5)));
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        element.BeginAnimation(UIElement.OpacityProperty, pulse);
        return element;
    }
}
