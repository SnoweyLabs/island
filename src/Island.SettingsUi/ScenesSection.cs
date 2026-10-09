using System.Windows;
using System.Windows.Controls;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>
/// "Scenes" (WORK-ORDER-7 section 5): make one, name it, tick its things from any page, give it a key, rename it, delete it after a yes/no question.
/// A scene opens what is closed and brings forward what is open, in the order ticked, so the last ticked ends on top. With no scenes the section says
/// in one sentence what a scene is: none is made ready.
/// </summary>
internal static class ScenesSection
{
    private const string NewKey = "scene:new";

    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var stack = new StackPanel();

        if (session.Scenes.Items.Count == 0)
        {
            var explain = Look.Label(SettingsSession.SceneExplanation, Look.BodySize, brush: Look.Sub);
            explain.TextWrapping = TextWrapping.Wrap;
            explain.Margin = new Thickness(4, 0, 4, 12);
            stack.Children.Add(explain);
        }

        var card = new GlassCard();
        foreach (var scene in session.Scenes.Items)
        {
            card.Add(SceneRow(host, scene), tight: false);
            if (host.OpenPanel == PanelId(scene.Id)) card.Add(ThingsPanel(host, scene), tight: true);
        }

        card.Add(NewSceneRow(host));
        stack.Children.Add(card);
        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }

    private static string PanelId(string sceneId) => KeybindEditor.SceneActionId(sceneId);

    private static UIElement SceneRow(ISectionHost host, Scene scene)
    {
        var session = host.Session;
        var id = scene.Id;
        var action = KeybindEditor.SceneActionId(id);
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Parts.NameBox(scene.Name, host.Accent, typed =>
        {
            host.Report(session.RenameScene(id, typed));
            host.Refresh();
        }, $"name:{action}");
        name.Margin = new Thickness(-10, 0, 0, 0);
        text.Children.Add(name);
        var count = scene.Things.Count;
        var open = host.OpenPanel == PanelId(id);
        var things = Parts.Link(count == 0 ? "Choose things" : $"{count} {(count == 1 ? "thing" : "things")} {(open ? "▴" : "▾")}", host.Accent, () =>
        {
            host.OpenPanel = open ? null : PanelId(id);
            host.Notice = null;
            host.Refresh($"things:{id}");
        }, $"things:{id}", $"The things of {scene.Name}. Press to {(open ? "close" : "open")} the list.");
        things.HorizontalAlignment = HorizontalAlignment.Left;
        text.Children.Add(things);
        row.Children.Add(text);

        var key = KeySection.KeyButton(host, action, scene.Name, small: true);
        key.Height = 32;
        key.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(key, 1);
        row.Children.Add(key);

        if (session.Settings.PickKeyFor(action) is not null)
        {
            var clear = Parts.Link("Clear", host.Accent, () =>
            {
                host.CancelCapture();
                host.Report(session.ClearKey(action));
                host.Refresh($"key:{action}");
            }, $"key:clear:{action}", $"Clear the key of {scene.Name}");
            Grid.SetColumn(clear, 2);
            row.Children.Add(clear);
        }

        var remove = Parts.Link("Delete", host.Accent, () => AskDelete(host, id), $"delete:{id}", $"Delete the scene {scene.Name}");
        remove.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(remove, 3);
        row.Children.Add(remove);
        return row;
    }

    private static void AskDelete(ISectionHost host, string id)
    {
        if (host.Session.AskDeleteScene(id) is not { } question) return;
        host.Ask(question, "Yes, delete it", () =>
        {
            host.OpenPanel = null;
            host.Report(host.Session.DeleteScene(id));
            host.Refresh(NewKey);
        });
    }

    /// <summary>Every pick of every page as a switch; the ones ticked carry their place in the scene's order.</summary>
    private static UIElement ThingsPanel(ISectionHost host, Scene scene)
    {
        var session = host.Session;
        var panel = new StackPanel { Margin = new Thickness(16, 4, 16, 12) };
        var order = scene.Things.Select((t, i) => (t.Id, Place: i + 1)).ToDictionary(x => x.Id, x => x.Place);

        // Things that were ticked and are no longer on the island stay in the scene, so they are listed first with their place.
        var onIsland = session.AllPicks.Select(p => p.Id).ToHashSet();
        var wrap = new WrapPanel();
        foreach (var gone in scene.Things.Where(t => !onIsland.Contains(t.Id)))
            wrap.Children.Add(Chip(host, scene, gone, "#8A90A8", order.GetValueOrDefault(gone.Id), "not on the island now"));
        foreach (var page in session.Pages.Pages)
        {
            foreach (var pick in session.AllPicks.Where(p => p.PageId == page.Id))
                wrap.Children.Add(Chip(host, scene, pick, page.Color, order.GetValueOrDefault(pick.Id), null));
        }

        if (wrap.Children.Count == 0)
            panel.Children.Add(Look.Label("Nothing is on the island yet. Add something with the + button on the island.", Look.SmallSize, brush: Look.Sub));
        else
        {
            panel.Children.Add(wrap);
            var hint = Look.Label("They open in the order you tick them; the last one ends on top.", Look.SmallSize, brush: Look.Sub);
            hint.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(hint);
        }

        return panel;
    }

    private static Button Chip(ISectionHost host, Scene scene, Pick pick, string colour, int place, string? note)
    {
        var on = place > 0;
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = Look.BrushOf(colour),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var label = Look.Label(on ? $"{place}  {pick.Name}" : pick.Name, 13);
        label.TextWrapping = TextWrapping.NoWrap;
        content.Children.Add(label);

        var key = $"tick:{scene.Id}:{pick.Id}";
        var chip = Parts.Flat(content, 16, () =>
        {
            host.Report(host.Session.SetSceneThing(scene.Id, pick, !on));
            host.Refresh(key);
        }, key, $"{pick.Name}: {(on ? "in the scene, number " + place : "not in the scene")}{(note is null ? string.Empty : ", " + note)}. Press to switch.", "#12FFFFFF");
        chip.Height = 32;
        chip.Padding = new Thickness(12, 0, 12, 0);
        chip.Margin = new Thickness(0, 0, 8, 8);
        chip.BorderThickness = new Thickness(1);
        chip.BorderBrush = on ? Look.Mix(colour, 55) : Look.Line;
        chip.Background = on ? Look.Mix(colour, 13) : System.Windows.Media.Brushes.Transparent;
        chip.Opacity = on ? 1 : 0.55;
        return chip;
    }

    private static UIElement NewSceneRow(ISectionHost host)
    {
        var session = host.Session;
        if (session.Scenes.Items.Count >= Scenes.MaxScenes) return Look.Label(SceneText.SceneLimit, Look.SmallSize, brush: Look.Sub);

        var add = Parts.Link("+ New scene", host.Accent, () =>
        {
            var before = session.Scenes.Items.Count;
            host.Report(session.CreateScene(FreeName(session.Scenes)));
            host.OpenPanel = session.Scenes.Items.Count > before ? PanelId(session.Scenes.Items[^1].Id) : null;
            host.Refresh(session.Scenes.Items.Count > before ? $"name:{PanelId(session.Scenes.Items[^1].Id)}" : NewKey);
        }, NewKey);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        return add;
    }

    private static string FreeName(SceneStore scenes)
    {
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? "New scene" : $"New scene {n}";
            if (scenes.Items.All(s => !string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }
}
