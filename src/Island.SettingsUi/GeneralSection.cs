using System.Windows;
using System.Windows.Controls;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>
/// "General": everyday things: whether the island starts with Windows (WORK-ORDER-6 §3) and the idle time (§4). The first shows what Windows holds
/// at this moment, not what the settings file remembers; it is off until the person switches it on, and nothing else ever switches it.
/// </summary>
internal static class GeneralSection
{
    public const string StartupLine = "When you sign in to Windows the island starts by itself, quietly: its icon appears near the clock and your key works, but it does not open until you call it.";

    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var stack = new StackPanel();
        stack.Children.Add(StartupCard(host, session));
        var idle = IdleCard(host, session);
        idle.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(idle);
        var notice = NoticeCard(host, session);
        notice.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(notice);
        var pill = PillCard(host, session);
        pill.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(pill);
        var addon = AddonCard(host, session);
        addon.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(addon);
        var setup = SetupCard(host);
        setup.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(setup);
        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }

    /// <summary>
    /// "Browser add-on" and beside it "Connected" (with the number when more than one browser) or "Not connected" (WORK-ORDER-9 section 2). It follows the listener
    /// while the screen is open; a session with no listener reads "Not connected". It shows a number and nothing else about the add-on.
    /// </summary>
    internal static Border AddonCard(ISectionHost host, SettingsSession session)
    {
        var status = session.Addon;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Look.Label(Island.Core.AddonText.Title, 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(Island.Core.AddonText.Hint, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);
        var open = Parts.Link(Island.Core.AddonText.OpenFolderButton, host.Accent, () => host.Report(session.OpenAddonFolder()), Island.Core.AddonText.OpenFolderId, "Open the folder of the browser add-on");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(-8, 4, 0, 0);
        text.Children.Add(open);

        var value = Look.Label(Island.Core.AddonText.Status(status?.Browsers ?? 0), Look.BodySize, FontWeights.SemiBold);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(16, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(value, Island.Core.AddonText.StatusId);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);

        var card = new Border
        {
            Background = Look.Group,
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(16),
            Child = row,
        };

        void Apply(int browsers)
        {
            value.Text = Island.Core.AddonText.Status(browsers);
            System.Windows.Automation.AutomationProperties.SetName(value, $"{Island.Core.AddonText.Title}: {value.Text}");
            var on = browsers > 0;
            card.BorderBrush = on ? Look.BrushOf(host.Accent) : Look.Line;
            card.BorderThickness = new Thickness(on ? 2 : 1);
            card.Padding = new Thickness(on ? 15 : 16);
        }

        Apply(status?.Browsers ?? 0);
        if (status is not null)
        {
            // The listener raises its event on a pool thread; the screen is changed on its own thread. Only while the card is on screen.
            void OnChanged() => card.Dispatcher.BeginInvoke(() => Apply(status.Browsers));
            card.Loaded += (_, _) =>
            {
                status.Changed += OnChanged;
                Apply(status.Browsers);
            };
            card.Unloaded += (_, _) => status.Changed -= OnChanged;
        }

        return card;
    }

    public const string SetupLine = "The steps you saw when Island first started, with everything as it is now. Only what you touch changes.";

    private static Border SetupCard(ISectionHost host)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Look.Label("Run the setup again", 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(SetupLine, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        var run = Parts.Pill("Run the setup again", false, host.RequestSetup, "general:setup");
        run.Margin = new Thickness(16, 0, 0, 0);
        run.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(run, 1);
        row.Children.Add(run);

        return new Border
        {
            Background = Look.Group,
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(16),
            Child = row,
        };
    }

    public const string NoticeLine = "How long the notice that your coding agent has finished stays on the screen, in seconds (3 to 30). It stays longer while the pointer is on it.";

    private static Border NoticeCard(ISectionHost host, SettingsSession session)
    {
        var seconds = (int)session.Settings.NoticeSeconds;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Look.Label("Notice time", 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(NoticeLine, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        Button Step(string sign, int by, string key, string label)
        {
            var button = Parts.Pill(sign, false, () =>
            {
                host.Report(session.SetNoticeSeconds(session.Settings.NoticeSeconds + by));
                host.Refresh(key);
            }, key);
            button.MinWidth = 40;
            button.Padding = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(button, $"{label}, now {seconds} seconds"); // the person who cannot see the value next to it hears it
            button.IsEnabled = by < 0 ? seconds > Island.Core.NoticeQueue.MinSeconds : seconds < Island.Core.NoticeQueue.MaxSeconds;
            return button;
        }

        var value = Look.Label($"{seconds} s", Look.BodySize, FontWeights.SemiBold);
        value.MinWidth = 48;
        value.TextAlignment = TextAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        FocusKey.Set(value, "general:notice-value");
        System.Windows.Automation.AutomationProperties.SetName(value, $"Notice time: {seconds} seconds");

        var stepper = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        stepper.Children.Add(Step("−", -1, "general:notice-less", "Shorter notice time"));
        stepper.Children.Add(value);
        stepper.Children.Add(Step("+", 1, "general:notice-more", "Longer notice time"));

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        Grid.SetColumn(stepper, 1);
        row.Children.Add(stepper);

        return new Border
        {
            Background = Look.Group,
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(16),
            Child = row,
        };
    }

    public const string PillLine = "While music or a video plays, a small pill sits at the top of the screen with its name, the buttons, and its edge showing how much is left. It never takes your keyboard.";

    private static Border PillCard(ISectionHost host, SettingsSession session)
    {
        var on = session.Settings.ShowPill;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Look.Label("Show the pill while something plays", 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(PillLine, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        var toggle = Parts.Pill(on ? "On" : "Off", on, () =>
        {
            host.Report(session.SetShowPill(!on));
            host.Refresh("general:pill");
        }, "general:pill");
        toggle.Margin = new Thickness(16, 0, 0, 0);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(toggle, $"The pill is {(on ? "on" : "off")}. Press to switch it {(on ? "off" : "on")}.");
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        return new Border
        {
            Background = Look.Group,
            BorderBrush = on ? Look.BrushOf(host.Accent) : Look.Line,
            BorderThickness = new Thickness(on ? 2 : 1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(on ? 15 : 16),
            Child = row,
        };
    }

    public const string IdleLine = "How long the island stays on the screen when you do not use it, before it leaves by itself. From\u00A02\u00A0to\u00A060\u00A0seconds.";

    /// <summary>The idle time (WORK-ORDER-6 section 4): a minus and a plus with the seconds between them; one second a press, kept between 2 and 60, saved at once.</summary>
    private static Border IdleCard(ISectionHost host, SettingsSession session)
    {
        var seconds = (int)session.Settings.IdleSeconds;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Look.Label("Idle time", 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(IdleLine, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        Button Step(string sign, int by, string key, string label)
        {
            var button = Parts.Pill(sign, false, () =>
            {
                host.Report(session.SetIdleSeconds(session.Settings.IdleSeconds + by));
                host.Refresh(key);
            }, key);
            button.MinWidth = 40;
            button.Padding = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(button, $"{label}, now {seconds} seconds"); // the person who cannot see the value next to it hears it
            button.IsEnabled = by < 0 ? seconds > Island.Core.Settings.MinIdleSeconds : seconds < Island.Core.Settings.MaxSetIdleSeconds;
            return button;
        }

        var value = Look.Label($"{seconds} s", Look.BodySize, FontWeights.SemiBold);
        value.MinWidth = 48;
        value.TextAlignment = TextAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center;
        FocusKey.Set(value, "general:idle-value");
        System.Windows.Automation.AutomationProperties.SetName(value, $"Idle time: {seconds} seconds");

        var stepper = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        stepper.Children.Add(Step("−", -1, "general:idle-less", "Shorter idle time"));
        stepper.Children.Add(value);
        stepper.Children.Add(Step("+", 1, "general:idle-more", "Longer idle time"));

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        Grid.SetColumn(stepper, 1);
        row.Children.Add(stepper);

        return new Border
        {
            Background = Look.Group,
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(16),
            Child = row,
        };
    }

    private static Border StartupCard(ISectionHost host, SettingsSession session)
    {
        var on = session.StartWithWindowsOn;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Look.Label("Start with Windows", 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(session.StartupAvailable ? session.StartupExplanation ?? StartupLine : SettingsText.StartupNotAvailable, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);

        if (session.StartupAvailable)
        {
            var toggle = Parts.Pill(on ? "On" : "Off", on, () =>
            {
                host.Report(session.SetStartWithWindows(!on));
                host.Refresh("general:startup");
            }, "general:startup");
            toggle.Margin = new Thickness(16, 0, 0, 0);
            toggle.VerticalAlignment = VerticalAlignment.Center;
            System.Windows.Automation.AutomationProperties.SetName(toggle, $"Start with Windows is {(on ? "on" : "off")}. Press to switch it {(on ? "off" : "on")}.");
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
        }

        return new Border
        {
            Background = Look.Group,
            BorderBrush = on ? Look.BrushOf(host.Accent) : Look.Line,
            BorderThickness = new Thickness(on ? 2 : 1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(on ? 15 : 16),
            Child = row,
        };
    }
}
