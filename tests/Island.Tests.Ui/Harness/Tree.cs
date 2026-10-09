using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Island.SettingsUi;

namespace Island.Tests.Ui.Harness;

/// <summary>Walking a built (never shown) WPF view: its elements, the names a screen reader would get, the layout at a given size.</summary>
internal static class Tree
{
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    public static void Layout(FrameworkElement view, double width, double height)
    {
        view.Width = width;
        view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
    }

    /// <summary>The text a screen reader would read for a control: the AutomationProperties.Name when set, else the text of the TextBlocks inside it (what WPF's button peer reads from its content).</summary>
    public static string AccessibleName(DependencyObject control)
    {
        var name = AutomationProperties.GetName(control);
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return string.Join(" ", Descendants(control).OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    public static IEnumerable<T> Of<T>(FrameworkElement view) where T : DependencyObject => Descendants(view).OfType<T>();

    /// <summary>The key the view gave an element so that it can be found after a rebuild (the internal FocusKey), read by reflection because the assembly's internals are not visible here.</summary>
    public static string? FocusKeyOf(DependencyObject element)
    {
        var type = typeof(SettingsView).Assembly.GetType("Island.SettingsUi.FocusKey")!;
        var get = type.GetMethod("Get")!;
        return get.Invoke(null, [element]) as string;
    }

    public static Rect BoundsIn(UIElement element, UIElement ancestor)
    {
        var transform = element.TransformToAncestor(ancestor);
        return transform.TransformBounds(new Rect(0, 0, element.RenderSize.Width, element.RenderSize.Height));
    }

    public static Button ButtonKeyed(FrameworkElement view, string key) =>
        Of<Button>(view).FirstOrDefault(b => FocusKeyOf(b) == key) ?? throw new InvalidOperationException($"no button keyed {key}");

    /// <summary>Presses a button the way a click does (the Click event, synchronously); no input is made.</summary>
    public static void Click(FrameworkElement view, string key)
    {
        ButtonKeyed(view, key).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Layout(view, view.Width, view.Height);
    }

    public static IEnumerable<SettingsSection> FullSections => [SettingsSection.Key, SettingsSection.Pages, SettingsSection.OnTheIsland, SettingsSection.Scenes, SettingsSection.Mode, SettingsSection.Glass, SettingsSection.CodingAgents, SettingsSection.General];

    public static IEnumerable<SettingsSection> SetupSections => [SettingsSection.Welcome, SettingsSection.Key, SettingsSection.Pages, SettingsSection.OnTheIsland, SettingsSection.Mode];
}
