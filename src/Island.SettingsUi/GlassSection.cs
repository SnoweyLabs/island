using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>"Glass": the glasses the app has, as cards in the manner of the reference's mode cards. Blur is offered only while it is available.</summary>
internal static class GlassSection
{
    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var options = session.GlassOptions;
        var chosen = session.EffectiveGlass;

        var grid = new UniformGrid { Columns = options.Count, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var option in options) grid.Children.Add(Card(host, option, option.Kind == chosen));
        if (grid.Children.Count > 0) ((FrameworkElement)grid.Children[^1]).Margin = new Thickness(0); // no gap after the last card

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(KeySection.NoticeLine(host));

        if (!session.BlurAvailable)
        {
            var note = Look.Label(SettingsText.BlurUnavailableFor(session.Settings.Glass), Look.HintSize, brush: Look.Sub);
            note.Margin = new Thickness(4, 0, 4, 0);
            stack.Children.Add(note);
        }

        return stack;
    }

    private static Button Card(ISectionHost host, GlassOption option, bool on)
    {
        var text = new StackPanel();
        var name = Look.Label(option.Name, 15, FontWeights.SemiBold);
        name.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(name);
        text.Children.Add(Look.Label(option.Description, Look.HintSize, brush: Look.Sub));

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

        var kind = option.Kind;
        var button = Parts.Flat(face, Look.GroupRadius, () =>
        {
            host.Report(host.Session.SetGlass(kind));
            host.Refresh($"glass:{kind}");
        }, $"glass:{kind}", $"{option.Name} glass{(on ? ", chosen" : string.Empty)}. {option.Description}", "#00000000");
        button.Margin = new Thickness(0, 0, 12, 0);
        button.VerticalContentAlignment = VerticalAlignment.Stretch;
        return button;
    }
}
