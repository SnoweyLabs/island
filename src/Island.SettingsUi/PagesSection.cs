using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Island.Core;
using Island.Core.SettingsEdit;
using CorePage = Island.Core.Page;

namespace Island.SettingsUi;

/// <summary>
/// "Your pages": rename in place, recolour from the swatches or any #RRGGBB (with a live capsule), add a page,
/// remove one of Dan's own after a yes/no question that says how many picks go with it.
/// </summary>
internal static class PagesSection
{
    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var stack = new StackPanel();
        var card = new GlassCard();

        foreach (var page in session.Pages.Pages)
        {
            card.Add(PageRow(host, page), tight: false);
            if (host.OpenPanel == page.Id) card.Add(ColourPanel(host, page), tight: true);
        }

        card.Add(NewPageRow(host));
        stack.Children.Add(card);
        if (session.TerminalsNoRoom)
        {
            var noRoom = Look.Label(TerminalsPageRefusals.NoRoom.Message, Look.SmallSize, brush: Look.Sub);
            noRoom.Margin = new Thickness(16, 10, 16, 0);
            noRoom.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(noRoom);
        }

        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }

    private static UIElement PageRow(ISectionHost host, CorePage page)
    {
        var session = host.Session;
        var id = page.Id;
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = Parts.Flat(Parts.Dot(page.Color), 12, () =>
        {
            host.OpenPanel = host.OpenPanel == id ? null : id;
            host.Notice = null;
            host.Refresh($"dot:{id}");
        }, $"dot:{id}", $"Colour of {page.Name}: {page.Color}. Press to change.");
        dot.Margin = new Thickness(0, 0, 12, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Parts.NameBox(page.Name, page.Color, typed =>
        {
            host.Report(session.RenamePage(id, typed));
            host.Refresh();
        }, $"name:{id}");
        name.Margin = new Thickness(-10, 0, 0, 0);
        text.Children.Add(name);
        text.Children.Add(Look.Label(SubText(session, page), Look.SmallSize, brush: Look.Sub));

        Grid.SetColumn(text, 1);
        row.Children.Add(dot);
        row.Children.Add(text);

        if (!page.IsBuiltIn)
        {
            var remove = Parts.Link("Remove", host.Accent, () => AskRemove(host, id), $"remove:{id}", $"Remove the page {page.Name}");
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
        }

        return row;
    }

    private static string SubText(SettingsSession session, CorePage page) =>
        session.Pages.DigitFor(page.Id) is { } digit
            ? $"Key {digit} while the island is open"
            : "No number key: only nine pages have one";

    private static void AskRemove(ISectionHost host, string id)
    {
        if (host.Session.AskDeletePage(id) is not { } question) return;
        host.Ask(question.Text, "Yes, remove it", () =>
        {
            host.OpenPanel = null;
            host.Report(host.Session.DeletePage(id));
            host.Refresh("new");
        });
    }

    // ---- Colour -----------------------------------------------------------

    private static UIElement ColourPanel(ISectionHost host, CorePage page)
    {
        var session = host.Session;
        var id = page.Id;
        var panel = new StackPanel { Margin = new Thickness(44, 4, 16, 12) };

        var swatches = new WrapPanel();
        foreach (var colour in Look.Swatches)
        {
            var hex = colour;
            swatches.Children.Add(Parts.Swatch(hex, string.Equals(hex, page.Color, StringComparison.OrdinalIgnoreCase), () =>
            {
                host.Report(session.RecolourPage(id, hex));
                host.Refresh($"dot:{id}");
            }, $"swatch:{hex}"));
        }

        panel.Children.Add(swatches);
        panel.Children.Add(AnyColourRow(host, page));
        return panel;
    }

    /// <summary>Any #RRGGBB, with a small live capsule that wears the colour as its rim before it is saved (EVALS P2).</summary>
    private static UIElement AnyColourRow(ISectionHost host, CorePage page)
    {
        var id = page.Id;
        var preview = new Border { Width = 132, Height = 36, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox
        {
            Text = page.Color,
            Width = 104,
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
            MaxLength = 9,
            Margin = new Thickness(0, 0, 8, 0),
            Template = Parts.RingBoxTemplate(),
        };
        FocusKey.Set(box, $"hex:{id}");
        EditHooks.Set(box, () =>
        {
            box.Text = page.Color;
            Keyboard.ClearFocus();
        });
        System.Windows.Automation.AutomationProperties.SetName(box, "Any colour as six hex digits");
        Preview(preview, page.Color);

        void Commit()
        {
            host.Report(host.Session.RecolourPage(id, box.Text));
            host.Refresh($"dot:{id}");
        }

        box.TextChanged += (_, _) =>
        {
            if (ParseColour(box.Text) is { } hex) Preview(preview, hex);
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        row.Children.Add(preview);
        row.Children.Add(box);
        row.Children.Add(Parts.Link("Use this colour", host.Accent, Commit, $"use:{id}"));
        return row;
    }

    private static string? ParseColour(string text)
    {
        var candidate = text.Trim();
        if (candidate.Length == 6) candidate = "#" + candidate;
        return candidate.Length == 7 && candidate[0] == '#' && candidate[1..].All(Uri.IsHexDigit) ? candidate.ToUpperInvariant() : null;
    }

    /// <summary>The capsule in miniature: dark glass, a rim and a glow in the chosen colour.</summary>
    private static void Preview(Border capsule, string hex)
    {
        capsule.CornerRadius = new CornerRadius(18);
        capsule.Background = Look.Solid(20, 22, 32, 0.8);
        capsule.BorderBrush = Look.BrushOf(hex, 0.9);
        capsule.BorderThickness = new Thickness(1.6);
        capsule.Effect = new DropShadowEffect { Color = Look.ColorOf(hex), Opacity = 0.55, ShadowDepth = 0, BlurRadius = 16 };
        capsule.Child = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Look.BrushOf(hex),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 0, 0, 0),
        };
    }

    // ---- New page -----------------------------------------------------------

    private static UIElement NewPageRow(ISectionHost host)
    {
        var pages = host.Session.Pages;
        if (pages.Pages.Count >= PageStore.MaxPages)
            return Look.Label(SettingsText.PageLimit, Look.SmallSize, brush: Look.Sub);

        var add = Parts.Link("+ New page", host.Accent, () =>
        {
            var edit = host.Session.CreatePage(FreeName(pages), FreeColour(pages));
            host.Report(edit);
            host.OpenPanel = null;
            host.Refresh(host.Session.Pages.Pages.Count > pages.Pages.Count ? $"name:{host.Session.Pages.Pages[^1].Id}" : "new");
        }, "new");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        return add;
    }

    private static string FreeName(PageStore pages)
    {
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? "New page" : $"New page {n}";
            if (pages.Pages.All(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }

    /// <summary>The first swatch no page wears yet, else the next one round the set.</summary>
    private static string FreeColour(PageStore pages) =>
        Look.Swatches.FirstOrDefault(s => pages.Pages.All(p => !string.Equals(p.Color, s, StringComparison.OrdinalIgnoreCase)))
        ?? Look.Swatches[pages.Pages.Count % Look.Swatches.Length];
}
