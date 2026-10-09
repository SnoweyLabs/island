using System.IO;

namespace Island.App;

/// <summary>
/// Draws the pictures that go into <c>ship/</c> (WORK-ORDER-8): from the app's own drawing, into the folder it is given (the repository's root). Draws only;
/// nothing outside the folder is touched, nothing is started. Run as <c>Island.App.exe --render-assets &lt;root&gt;</c>.
/// </summary>
internal static class ShipAssets
{
    // The sizes come from the pages of Microsoft Learn recorded in ship/SOURCES.md: Square44x44Logo and Square150x150Logo at 100, 200 and 400 percent, the
    // target sizes of the app list (with the "unplated" variants, so that the icon gets no backplate), StoreLogo at 100, 125, 150, 200 and 400 percent, and the
    // Store listing's 300 x 300 app tile icon.
    private static readonly int[] TargetSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];
    private static readonly (int Scale, int Square44, int Square150, int Store)[] Scales = [(100, 44, 150, 50), (125, 55, 188, 63), (150, 66, 225, 75), (200, 88, 300, 100), (400, 176, 600, 200)];
    private static readonly int[] IcoSizes = [16, 24, 32, 48, 64, 256];

    public static void Render(string root)
    {
        var choices = Path.Combine(root, "review", "choices");
        Directory.CreateDirectory(choices);
        File.WriteAllBytes(Path.Combine(choices, "app-icon.png"), AppIcon.Png(AppIcon.Sheet()));

        var assets = Path.Combine(root, "ship", "package", "Assets");
        Directory.CreateDirectory(assets);
        void Png(string name, int size) => File.WriteAllBytes(Path.Combine(assets, name), AppIcon.Png(AppIcon.Render(AppIcon.Default, size)));

        // The unqualified names the manifest points at, then every scale and target size the Store and Windows look for.
        Png("Square44x44Logo.png", 44);
        Png("Square150x150Logo.png", 150);
        Png("StoreLogo.png", 50);
        foreach (var (scale, s44, s150, store) in Scales)
        {
            if (scale is 100 or 200 or 400)
            {
                Png($"Square44x44Logo.scale-{scale}.png", s44);
                Png($"Square150x150Logo.scale-{scale}.png", s150);
            }

            Png($"StoreLogo.scale-{scale}.png", store);
        }

        foreach (var size in TargetSizes)
        {
            Png($"Square44x44Logo.targetsize-{size}.png", size);
            Png($"Square44x44Logo.targetsize-{size}_altform-unplated.png", size);
        }

        File.WriteAllBytes(Path.Combine(assets, "Island.ico"), AppIcon.Ico(AppIcon.Default, IcoSizes));

        var store300 = Path.Combine(root, "ship", "store", "images");
        Directory.CreateDirectory(store300);
        File.WriteAllBytes(Path.Combine(store300, "app-tile-300x300.png"), AppIcon.Png(AppIcon.Render(AppIcon.Default, 300)));

        ShipScreenshots.Render(Path.Combine(root, "ship", "store", "screenshots"));
        ShipScreenshots.RenderAddon(Path.Combine(root, "ship", "addon", "images"), Path.Combine(root, "extension", "icons"));
    }
}
