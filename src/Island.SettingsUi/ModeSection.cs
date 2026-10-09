using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>
/// "Mode" (WORK-ORDER-7 section 1): three cards, one is always on, each with one sentence; a key that goes to the next mode, empty until set;
/// and the list of programs the island never appears over, which the person fills from what is running now (a name and an executable file name,
/// never a path).
/// </summary>
internal static class ModeSection
{
    private static readonly (Mode Mode, string Sentence)[] Choices =
    [
        (Mode.Focus, "Works without interruptions, but the notice that your coding agent has finished may still show over a fullscreen program."),
        (Mode.Vibe, "The default. Nothing appears by itself over a fullscreen program or a presentation; your key still brings the island."),
        (Mode.DND, "Nothing appears by itself, ever, and the key brings the island only when nothing is\u00A0fullscreen."), // a no-break space: the sentence does not end a line on one word (Dan's P22, WORK-ORDER-13)
    ];

    public const string NeverOverLine = "The island never appears over these programs, not even when you press the key, as if each were a game in exclusive fullscreen.";

    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var stack = new StackPanel();

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Choices.Length, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (mode, sentence) in Choices) grid.Children.Add(Card(host, mode, sentence, session.Settings.Mode == mode));
        if (grid.Children.Count > 0) ((FrameworkElement)grid.Children[^1]).Margin = new Thickness(0);
        stack.Children.Add(grid);

        stack.Children.Add(KeySection.NoticeLine(host));
        stack.Children.Add(NextModeKey(host));
        var list = NeverOverCard(host);
        list.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(list);
        return stack;
    }

    private static Button Card(ISectionHost host, Mode mode, string sentence, bool on)
    {
        var text = new StackPanel();
        var name = Look.Label(mode.ToString(), 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        var line = Look.Label(sentence, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        var face = new Border
        {
            Background = Look.Group,
            BorderBrush = on ? Look.BrushOf(host.Accent) : Look.Line,
            BorderThickness = new Thickness(on ? 2 : 1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(on ? 15 : 16),
            Child = text,
            Effect = on ? new DropShadowEffect { Color = Look.ColorOf(host.Accent), Opacity = 0.45, ShadowDepth = 0, BlurRadius = 22 } : null,
        };

        var button = Parts.Flat(face, Look.GroupRadius, () =>
        {
            host.Report(host.Session.SetMode(mode));
            host.Refresh($"mode:{mode}");
        }, $"mode:{mode}", $"{mode} mode{(on ? ", on" : string.Empty)}. {sentence}", "#00000000");
        button.Margin = new Thickness(0, 0, 12, 0);
        button.VerticalContentAlignment = VerticalAlignment.Stretch;
        return button;
    }

    private static FrameworkElement NextModeKey(ISectionHost host)
    {
        var id = KeybindEditor.ModeNextId;
        var row = new Grid { MinHeight = Look.RowMinHeight, Margin = new Thickness(0, 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        text.Children.Add(Look.Label(SettingsText.ModeNextActionName, Look.BodySize, FontWeights.SemiBold));
        text.Children.Add(Look.Label("A key that goes to the next mode: Focus, Vibe, DND, Focus again. Empty until you set one.", Look.SmallSize, brush: Look.Sub));
        var cap = KeySection.KeyButton(host, id, SettingsText.ModeNextActionName);
        var clear = Parts.Link("Clear", host.Accent, () =>
        {
            host.CancelCapture();
            host.Report(host.Session.ClearKey(id));
            host.Refresh();
        }, $"key:clear:{id}", "Clear the key for the next mode");
        clear.IsEnabled = host.Session.Settings.ModeKey is not null;
        clear.Opacity = clear.IsEnabled ? 1 : 0;
        Grid.SetColumn(cap, 1);
        Grid.SetColumn(clear, 2);
        row.Children.Add(text);
        row.Children.Add(cap);
        row.Children.Add(clear);
        return row;
    }

    private static Border NeverOverCard(ISectionHost host)
    {
        var session = host.Session;
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        body.Children.Add(Look.Label("Never over these", 15, FontWeights.SemiBold));
        var line = Look.Label(NeverOverLine, Look.HintSize, brush: Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        line.Margin = new Thickness(0, 4, 0, 10);
        body.Children.Add(line);

        var entries = session.Settings.NeverOver.Entries;
        var held = new WrapPanel();
        foreach (var entry in entries)
        {
            var exe = entry.ExeFileName;
            var chip = Parts.Flat(new Border { Child = Look.Label($"{entry.Name}  ✕", 13), Padding = new Thickness(12, 0, 12, 0), Height = 32 }, 16, () =>
            {
                host.Report(session.RemoveNeverOver(exe));
                host.Refresh($"never:remove:{exe}");
            }, $"never:remove:{exe}", $"Remove {entry.Name} from the list", "#12FFFFFF");
            chip.Margin = new Thickness(0, 0, 8, 8);
            held.Children.Add(chip);
        }

        if (entries.Count == 0) body.Children.Add(Look.Label("Nothing on the list yet.", Look.SmallSize, brush: Look.Sub));
        else body.Children.Add(held);

        var running = session.RunningNotOnList();
        var add = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        add.Children.Add(Look.Label(running.Count == 0 ? "No other program is running that could be added." : "Add one of the programs that are running now:", Look.SmallSize, brush: Look.Sub));
        var offered = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var program in running)
        {
            var candidate = program;
            var chip = Parts.Flat(new Border { Child = Look.Label($"+  {program.Name}", 13), Padding = new Thickness(12, 0, 12, 0), Height = 32 }, 16, () =>
            {
                host.Report(session.AddNeverOver(new NeverOverEntry(candidate.Name, candidate.ExeFileName)));
                host.Refresh($"never:add:{candidate.ExeFileName}");
            }, $"never:add:{candidate.ExeFileName}", $"Add {program.Name} to the list", "#12FFFFFF");
            chip.Margin = new Thickness(0, 0, 8, 8);
            offered.Children.Add(chip);
        }

        add.Children.Add(offered);
        body.Children.Add(add);

        return new Border
        {
            Background = Look.Group,
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Child = body,
        };
    }
}
