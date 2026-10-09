using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// An invented settings session (the same made-up world as the settings smoke program: invented programs and pages, an accepting key registrar, files in a folder under this test's own bin folder)
/// and a settings view laid out in a grid, never shown: Measure, Arrange and UpdateLayout only. Nothing is rendered to a picture and nothing is saved by looking.
/// </summary>
internal static class SettingsWorld
{
    public const int Width = 1920;
    public const int Height = 1080;

    public static string TempFolder()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "design-tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static SettingsSession MadeUpSession(string dir)
    {
        var installed = StarterPicks.Programs.Select(p => new InstalledProgram(p.Name, p.ExeCandidates[0], null, "launch-" + p.Name)).ToList();
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"));
        var pages = PageStore.Default.Create("Alpha games", "#7CE04A").Store;
        var custom = pages.Pages[^1].Id;
        var picks = new PickStore(StarterPicks.Build(installed))
            .Remove("program:cursor").Remove("site:twitch.tv")
            .Add(Pick.ForProgram("Alpha", custom, "alpha.exe", null), out _)
            .Add(Pick.ForSite("Example", "example.org", custom), out _);
        var settings = Settings.Defaults
            .WithPageKey(PageIds.Media, HotkeyCombo.Parse("Ctrl+Alt+1"))
            .WithPageKey(custom, HotkeyCombo.Parse("Ctrl+Alt+7"));
        return new SettingsSession(
            files,
            new SettingsLoad(settings, SettingsStatus.Loaded, null),
            new PageStoreLoad(pages, PageStoreStatus.Loaded, null),
            new PickStoreLoad(picks, PickStoreStatus.Loaded, null),
            new AcceptingRegistrar(),
            () => installed,
            () => false);
    }

    private sealed class AcceptingRegistrar : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            return true;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }

    public static Grid Stage(SettingsView view, double width = Width, double height = Height)
    {
        var stage = new Grid { Width = width, Height = height };
        stage.Children.Add(view);
        return stage;
    }

    public static void Layout(Grid stage)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            stage.Measure(new Size(stage.Width, stage.Height));
            stage.Arrange(new Rect(0, 0, stage.Width, stage.Height));
            stage.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>The box of an element in the stage's own coordinates.</summary>
    public static Rect BoxOf(FrameworkElement element, UIElement stage) =>
        element.TransformToAncestor(stage).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static readonly System.Reflection.MethodInfo FocusKeyGet =
        typeof(SettingsView).Assembly.GetType("Island.SettingsUi.FocusKey")!.GetMethod("Get")!;

    /// <summary>The name a section gave a control so that it can be found again (the internal <c>FocusKey</c>), read by reflection.</summary>
    public static string? KeyOf(DependencyObject element) => FocusKeyGet.Invoke(null, [element]) as string;

    public static bool IsShown(UIElement element)
    {
        for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: not Visibility.Visible }) return false; // not IsVisible: that is false for a view that is in no window
        return true;
    }
}
