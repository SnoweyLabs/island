using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi.Smoke;

/// <summary>
/// Builds the settings screen on made-up data, lays it out at 1920 by 1080 off screen, renders each section to a
/// PNG with RenderTargetBitmap (the screen's own elements, never a capture of the real screen) and prints counts.
/// No window is created. Clicks are made through the automation "invoke" of each button, not through input.
/// </summary>
internal static class Program
{
    private const int Width = 1920;
    private const int Height = 1080;
    private static int _checks;
    private static int _failed;
    private static string _settingsPath = string.Empty;

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : Path.Combine(FindRoot(), "review", "settings");
        Directory.CreateDirectory(outDir);
        var temp = Path.Combine(Path.GetTempPath(), "island-settings-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var session = MadeUpSession(temp);
            var view = new SettingsView(session);
            view.FreezeAnimations(1.0);
            var stage = Stage(view);

            var pngs = 0;
            pngs += Snap(view, stage, SettingsSection.Key, outDir, "key.png");
            Capturing(view, stage, outDir, ref pngs);
            pngs += Snap(view, stage, SettingsSection.Pages, outDir, "pages.png");
            pngs += ColourPanel(view, stage, outDir);
            pngs += Snap(view, stage, SettingsSection.OnTheIsland, outDir, "island.png");
            pngs += Question(view, stage, session, outDir);
            pngs += Snap(view, stage, SettingsSection.Glass, outDir, "glass.png");

            TabStops(view, stage);
            Wiring(view, stage, session);
            Contrast();

            Console.WriteLine($"pngs written: {pngs}");
            Console.WriteLine($"checks: {_checks - _failed} of {_checks} passed");
            return _failed == 0 && pngs == 7 ? 0 : 1;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (IOException) { /* temporary folder */ }
        }
    }

    // ---- Made-up world ------------------------------------------------------------

    private static SettingsSession MadeUpSession(string dir)
    {
        var installed = StarterPicks.Programs
            .Select(p => new InstalledProgram(p.Name, p.ExeCandidates[0], null, "launch-" + p.Name))
            .ToList();
        _settingsPath = Path.Combine(dir, "settings.json");
        var files = new SettingsFiles(_settingsPath, Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"));

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

    // ---- The stage: a desktop-like picture behind the glass -----------------------------

    /// <summary>The reference's fake desktop (a gradient wall and one light and one dark window), so the glass has something to sit on. There is no blur here: a render of one element cannot blur what is behind it.</summary>
    private static Grid Stage(SettingsView view)
    {
        var wall = new Grid { Width = Width, Height = Height };
        wall.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) });
        wall.Children.Add(Glow(Color.FromRgb(0x2F, 0x6B, 0xFF), 0.12, 0.0, 0.70, 0.9));
        wall.Children.Add(Glow(Color.FromRgb(0xFF, 0x7A, 0x3D), 0.88, 0.08, 0.62, 0.8));
        wall.Children.Add(Glow(Color.FromRgb(0xB2, 0x3C, 0xFF), 0.55, 1.0, 0.65, 0.9));
        wall.Children.Add(Window(0.06, 0.16, 0.40, 0.60, Color.FromRgb(0xF2, 0xF3, 0xF6)));
        wall.Children.Add(Window(0.56, 0.26, 0.38, 0.58, Color.FromRgb(0x17, 0x19, 0x1E)));
        wall.Children.Add(view);
        return wall;
    }

    private static Border Glow(Color colour, double cx, double cy, double radius, double reach) => new()
    {
        Background = new RadialGradientBrush(colour, Colors.Transparent)
        {
            Center = new Point(cx, cy),
            GradientOrigin = new Point(cx, cy),
            RadiusX = radius * reach,
            RadiusY = radius * reach,
        },
    };

    private static Border Window(double left, double top, double width, double height, Color fill) => new()
    {
        Background = new SolidColorBrush(fill),
        CornerRadius = new CornerRadius(10),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(Width * left, Height * top, 0, 0),
        Width = Width * width,
        Height = Height * height,
    };

    // ---- Pictures ----------------------------------------------------------------------

    private static int Snap(SettingsView view, Grid stage, SettingsSection section, string dir, string file)
    {
        view.Section = section;
        return Render(stage, Path.Combine(dir, file));
    }

    private static void Capturing(SettingsView view, Grid stage, string dir, ref int pngs)
    {
        view.Section = SettingsSection.Key;
        ((ISectionHost)view).BeginCapture(KeybindEditor.MainId);
        pngs += Render(stage, Path.Combine(dir, "key-capturing.png"));
        Check("the capturing row says to press the keys", view.CountTextContaining(SettingsText.PressYourKeys) == 1);
        view.HandleEscape();
    }

    private static int ColourPanel(SettingsView view, Grid stage, string dir)
    {
        view.Section = SettingsSection.Pages;
        ((ISectionHost)view).OpenPanel = PageIds.Folders;
        ((ISectionHost)view).Refresh();
        var written = Render(stage, Path.Combine(dir, "pages-colour.png"));
        ((ISectionHost)view).OpenPanel = null;
        return written;
    }

    private static int Question(SettingsView view, Grid stage, SettingsSession session, string dir)
    {
        view.Section = SettingsSection.Pages;
        var custom = session.Pages.Pages[^1];
        var question = session.AskDeletePage(custom.Id)!;
        ((ISectionHost)view).Ask(question.Text, "Yes, remove it", () => { });
        var written = Render(stage, Path.Combine(dir, "dialog.png"));
        Check("the question is open and Esc answers it", view.WantsEscape && view.HandleEscape() && !view.WantsEscape);
        return written;
    }

    private static int Render(Grid stage, string path)
    {
        Layout(stage);
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return 1;
    }

    private static void Layout(Grid stage)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            stage.Measure(new Size(Width, Height));
            stage.Arrange(new Rect(0, 0, Width, Height));
            stage.UpdateLayout();
            Flush();
        }
    }

    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // ---- Checks (counts and yes/no only) ----------------------------------------------------

    private static void TabStops(SettingsView view, Grid stage)
    {
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            view.Section = section;
            Layout(stage);
            var stops = Descendants(view).OfType<UIElement>().Count(e => e is Button { IsEnabled: true, Focusable: true } or TextBox { Focusable: true });
            Console.WriteLine($"keyboard stops in {section}: {stops}");
            Check($"{section} has keyboard stops", stops >= 3);
        }

        view.Section = SettingsSection.Key;
        Layout(stage);
        Check("the private-shortcut sentence is on the Key screen once", view.CountTextContaining(SettingsText.PrivateShortcutWarning) == 1);
        Check("Esc is the host's when nothing is being edited", !view.HandleEscape());
    }

    private static void Wiring(SettingsView view, Grid stage, SettingsSession session)
    {
        // Pages: add one, open its colour panel, take a swatch, ask to remove it, say yes.
        view.Section = SettingsSection.Pages;
        var before = session.Pages.Pages.Count;
        Click(view, stage, "new");
        Check("+ New page adds a page", session.Pages.Pages.Count == before + 1);
        var added = session.Pages.Pages[^1];

        Click(view, stage, $"dot:{added.Id}");
        var swatches = Descendants(view).OfType<Button>().Count(b => FocusKey.Get(b)?.StartsWith("swatch:", StringComparison.Ordinal) == true);
        Check("the colour panel offers ten swatches", swatches == 10);
        Click(view, stage, "swatch:#3FD0FF");
        Check("a swatch recolours the page", session.Pages.ById(added.Id)!.Color == "#3FD0FF");

        Click(view, stage, $"remove:{added.Id}");
        Check("Remove asks a question first", view.WantsEscape && session.Pages.ById(added.Id) is not null);
        Click(view, stage, "dialog:no");
        Check("No keeps the page", session.Pages.ById(added.Id) is not null);
        Click(view, stage, $"remove:{added.Id}");
        Click(view, stage, "dialog:yes");
        Check("Yes removes the page", session.Pages.ById(added.Id) is null && session.Pages.Pages.Count == before);

        // On the island: a switch off and on again, and the restore question.
        view.Section = SettingsSection.OnTheIsland;
        var picksBefore = session.Picks.Picks.Count;
        Click(view, stage, "chip:program:spotify");
        Check("a switch takes the pick off the island", session.Picks.Picks.Count == picksBefore - 1);
        Click(view, stage, "chip:program:spotify");
        Check("and puts the starter pick back", session.Picks.Picks.Count == picksBefore);
        Click(view, stage, "restore:media");
        Check("Restore starter list asks first", view.WantsEscape);
        Click(view, stage, "dialog:no");
        Check("and No changes nothing", session.Picks.Picks.Count == picksBefore);

        // Glass: Blur is not offered here; Darker is.
        view.Section = SettingsSection.Glass;
        Check("Blur is not offered when it is not available", FindButton(view, "glass:Blur") is null);
        Click(view, stage, "glass:Darker");
        Check("Darker is chosen and saved", session.Settings.Glass == GlassKind.Darker && Settings.Parse(File.ReadAllText(_settingsPath)).Settings.Glass == GlassKind.Darker);

        // Keys: Restore default is off when the key is the default.
        view.Section = SettingsSection.Key;
        Layout(stage);
        Check("Restore default is disabled while the key is the default", FindButton(view, "key:restore") is { IsEnabled: false });
    }

    private static void Contrast()
    {
        foreach (var glass in new[] { GlassKind.Approved, GlassKind.Darker })
        {
            var alpha = Look.TintAlphaFor(glass);
            double Mix(double tint, double under) => tint * alpha + under * (1 - alpha);
            var onWhite = Luminance(Mix(12, 255), Mix(14, 255), Mix(22, 255));
            var ratio = 1.05 / (onWhite + 0.05);
            Console.WriteLine($"white text on {glass} glass over a pure white desktop: {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
            Check($"{glass} text reaches 4.5:1 over a white desktop", ratio >= 4.5);
        }
    }

    private static double Luminance(double r, double g, double b)
    {
        static double Lin(double c)
        {
            var s = c / 255;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static void Click(SettingsView view, Grid stage, string key)
    {
        Layout(stage);
        if (FindButton(view, key) is not { } button)
        {
            Check($"a control named {key} exists", false);
            return;
        }

        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        Flush();
        Layout(stage);
    }

    private static Button? FindButton(DependencyObject root, string key) =>
        Descendants(root).OfType<Button>().FirstOrDefault(b => FocusKey.Get(b) == key);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static void Check(string what, bool ok)
    {
        _checks++;
        if (ok) return;
        _failed++;
        Console.WriteLine($"FAILED: {what}");
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Island.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Island.sln not found above the program.");
    }
}
