using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App.Visuals;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.App;

/// <summary>
/// The Store's screenshots (WORK-ORDER-8 section 6), drawn by the app's own drawing from an invented picks list ("Chat", "Coder", "Tunes", "Editor", "Notes",
/// "Recorder": the names of the approved preview, each with a drawn sign tile and no real program's icon), on a plain dark background, 1920 x 1080. No capture
/// of the screen is ever made, and nothing of the owner's is read: the data is written here.
/// </summary>
internal static class ShipScreenshots
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const double Scale = 3.4;

    private static readonly (string Name, string Mark, string From, string To)[] Signs =
    [
        ("Chat", "C", "#4f8dff", "#2456d6"),
        ("Coder", "Co", "#3b3f4c", "#15171d"),
        ("Tunes", "T", "#34d17a", "#128a47"),
        ("Editor", "E", "#a66bff", "#6327d1"),
        ("Notes", "N", "#ffc84a", "#e08a00"),
        ("Recorder", "R", "#ff6a6a", "#c62b2b"),
    ];

    public static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        Save(Path.Combine(folder, "01-the-island.png"), OnBackground(TheIsland()));
        Save(Path.Combine(folder, "02-search.png"), OnBackground(Search()));
        Save(Path.Combine(folder, "03-something-is-playing.png"), OnBackground(Pill()));
        Save(Path.Combine(folder, "04-your-agent-is-done.png"), OnBackground(Notice()));
        Save(Path.Combine(folder, "05-the-modes.png"), ModesScreen());
    }

    /// <summary>
    /// The add-on's store pictures (WORK-ORDER-8 section 7), from the same invented list: two screenshots of 1280 x 800, the small promo tile of 440 x 280 and the store icon
    /// of 128 x 128 (the artwork 96 x 96 with 16 pixels of transparent padding, as the images page says), and the add-on's own icons for its manifest.
    /// </summary>
    public static void RenderAddon(string imagesFolder, string iconsFolder)
    {
        Directory.CreateDirectory(imagesFolder);
        Directory.CreateDirectory(iconsFolder);
        var media = new List<Item> { Pick(2, "2 tabs", windows: 2), Pick(0, "open"), Pick(3, "closed", closed: true), Pick(4, "open") };
        var contents = new PageContents(Pages.Get(PageIds.Media), media);
        var scene = new OffscreenScene(null);
        var tabs = scene.Render(OffscreenScene.RestFrame(contents, 0.3), contents, 2.2).Bitmap;
        Save(Path.Combine(imagesFolder, "screenshot-1-1280x800.png"), OnBackground(tabs, 1280, 800, 300));

        var pillScene = new OffscreenScene(null);
        var pill = pillScene.RenderPill(new PillContent("Sunset set", "T", Sign("T", "#34d17a", "#128a47"), IsPaused: false, CanControl: true), 0.62, ModeMark.Look.Approved, 2.2).Bitmap;
        Save(Path.Combine(imagesFolder, "screenshot-2-1280x800.png"), OnBackground(pill, 1280, 800, 300));

        Save(Path.Combine(imagesFolder, "small-promo-440x280.png"), Promo());
        Save(Path.Combine(imagesFolder, "store-icon-128x128.png"), PaddedIcon());
        foreach (var size in new[] { 16, 32, 48, 128 })
            File.WriteAllBytes(Path.Combine(iconsFolder, $"icon{size}.png"), AppIcon.Png(AppIcon.Render(AppIcon.Default, size)));
    }

    private static BitmapSource Promo()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90), null, new Rect(0, 0, 440, 280));
            dc.DrawImage(AppIcon.Render(AppIcon.Default, 128), new Rect(40, 76, 128, 128));
            var title = new FormattedText("Island tabs", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 34, Brushes.White, 1.0);
            dc.DrawText(title, new Point(184, 98));
            var line = new FormattedText("Your tabs, in the island", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 17, new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), 1.0);
            dc.DrawText(line, new Point(186, 148));
        }

        var bitmap = new RenderTargetBitmap(440, 280, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The store icon: 128 x 128, the artwork 96 x 96 in the middle and 16 pixels of transparent padding all round.</summary>
    private static BitmapSource PaddedIcon()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(AppIcon.Render(AppIcon.Default, 96), new Rect(16, 16, 96, 96));
        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ---- the pictures -----------------------------------------------------------------------------------------------------------------

    private static Item Pick(int index, string subtitle, bool closed = false, int windows = 0)
    {
        var (name, mark, from, to) = Signs[index];
        return new Item(name, subtitle, mark, 200, PickId: "program:" + name.ToLowerInvariant(), Icon: Sign(mark, from, to), IsClosed: closed, Count: windows);
    }

    private static BitmapSource TheIsland()
    {
        var items = new List<Item> { Pick(0, "open"), Pick(1, "3 windows", windows: 3), Pick(2, "closed", closed: true), Pick(3, "closed", closed: true), Pick(4, "open"), Pick(5, "open") };
        var contents = new PageContents(Pages.Get(PageIds.Apps), items);
        var scene = new OffscreenScene(null);
        return scene.Render(OffscreenScene.RestFrame(contents, 0.3), contents, Scale).Bitmap;
    }

    private static BitmapSource Search()
    {
        var data = new SearchViewData("tu",
            [Pick(2, "closed"), new Item("Tutorial clip", "tab", "Tu", 300, PickId: "site:video.example", Icon: Sign("Tu", "#a66bff", "#6327d1")), new Item("Search tu on Tunes", "Enter to open", "T", 200, Icon: Sign("T", "#34d17a", "#128a47"))],
            Selected: 0, NeedsClick: false, Title: "Tunes", Subtitle: "Enter to open", ServiceTiles: 1);
        var scene = new OffscreenScene(null);
        return scene.RenderSearch(data, Rgb.FromHex(LookConstants.AppsColor), Scale).Bitmap;
    }

    private static BitmapSource Pill()
    {
        var scene = new OffscreenScene(null);
        var content = new PillContent("Sunset set", "T", Sign("T", "#34d17a", "#128a47"), IsPaused: false, CanControl: true);
        return scene.RenderPill(content, 0.62, ModeMark.Look.Approved, Scale).Bitmap;
    }

    private static BitmapSource Notice()
    {
        var scene = new OffscreenScene(null);
        return scene.RenderNotice(new NoticeContent("island", "Agent finished — waiting for you"), ModeMark.Look.Approved, Scale).Bitmap;
    }

    private static BitmapSource ModesScreen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "island-shots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"), Path.Combine(dir, "scenes.json"));
            Settings.Defaults.Save(files.SettingsPath);
            var session = new SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null), PageStore.Load(files.PagesPath), new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null),
                new NoKeys(), () => [], () => false);
            var view = new SettingsView(session) { Section = SettingsSection.Mode };
            view.FreezeAnimations(1.0);
            var stage = new Grid { Width = Width, Height = Height, Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) };
            stage.Children.Add(view);
            stage.Measure(new Size(Width, Height));
            stage.Arrange(new Rect(0, 0, Width, Height));
            stage.UpdateLayout();
            return Snapshot.Of(stage, Width, Height, 96).Bitmap;
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // a temporary folder of invented files
            }
        }
    }

    // ---- composing --------------------------------------------------------------------------------------------------------------------

    /// <summary>The island at the top of a plain dark screen: a gradient of the approved preview's colours, no desktop, no window of any program.</summary>
    private static BitmapSource OnBackground(BitmapSource island, int width = Width, int height = Height, int top = 430)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90), null, new Rect(0, 0, width, height));
            var w = island.PixelWidth * 96.0 / island.DpiX;
            var h = island.PixelHeight * 96.0 / island.DpiY;
            dc.DrawImage(island, new Rect((width - w) / 2, top, w, h));
            var caption = new FormattedText("Island", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 30, new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1.0);
            dc.DrawText(caption, new Point((width - caption.Width) / 2, height - 120));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(string path, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>A drawn sign tile: a square of two colours with one or two letters on it. Nothing of any real program.</summary>
    private static IconImage Sign(string mark, string from, string to)
    {
        const int size = 256;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from), (Color)ColorConverter.ConvertFromString(to), 90);
            dc.DrawRectangle(brush, null, new Rect(0, 0, size, size));
            var text = new FormattedText(mark, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 120, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return new IconImage(size, size, pixels);
    }

    /// <summary>Keys are only drawn here.</summary>
    private sealed class NoKeys : IHotkeyRegistrar
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
}
