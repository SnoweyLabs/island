using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App.Visuals;
using Island.Core;
using Page = Island.Core.Page;

namespace Island.App;

/// <summary>
/// WORK-ORDER-3 section 4: the two glasses with the contrast of the title on a light background (the three pictures of
/// provisional variants of section 3 were replaced by the looks Dan chose, WORK-ORDER-5 sections 1 to 3, built in LooksStage;
/// their pictures in review/choices stay as records). The subject is the icon of explorer.exe, never Dan's programs.
/// </summary>
internal static class ChoicesStage
{
    private const double Scale = 2;
    private static readonly Rgb Light = new(242, 243, 246);

    public static void Run(SelfTestReport report, string folder)
    {
        var icon = ExplorerIcon();
        report.Check("the icon of explorer.exe could be read for the choice pictures", icon is not null, icon is null ? "no icon" : $"{icon.Width} x {icon.Height}");
        // (How an icon sits in the tile was decided: it fills the circle, WORK-ORDER-5 §1. review/choices/icon-in-tile.png stays as a record.)

        // (How a closed pick looks was decided: grey, WORK-ORDER-5 §2. review/choices/closed-pick.png stays as a record.)

        // (How the number of windows is shown was decided: one dot per window, WORK-ORDER-5 §3. review/choices/count-badge.png stays as a record.)

        Glasses(report, folder);
    }

    private static void Glasses(SelfTestReport report, string folder)
    {
        var media = Pages.Placeholder(PageIds.Media);
        var rows = new List<object>();
        double approved = 0, darker = 0;
        foreach (var (name, alpha) in new[] { ("approved", LookConstants.GlassBaseAlpha), ("darker", LookConstants.GlassDarkerAlpha) })
        {
            var scene = new OffscreenScene(Light, alpha);
            scene.Render(OffscreenScene.RestFrame(media, 0.3), media, Scale).SavePng(Path.Combine(folder, $"glass-{name}.png"));
            var ratio = CapsuleStage.TitleContrast(media, alpha, out var glass);
            rows.Add(new { glass = name, tintAlpha = alpha, titleContrast = Math.Round(ratio, 2), behindTitle = glass.ToHex() });
            if (name == "approved") approved = ratio; else darker = ratio;
        }

        report.Info["glassContrastOnLight"] = rows;
        report.Check("the two glasses were drawn and the title contrast of each was measured", File.Exists(Path.Combine(folder, "glass-approved.png")) && File.Exists(Path.Combine(folder, "glass-darker.png")),
            $"approved {approved.ToString("0.00", CultureInfo.InvariantCulture)}:1, darker {darker.ToString("0.00", CultureInfo.InvariantCulture)}:1 on the light background");
        report.Check("the darker glass reads better than the approved one", darker > approved, $"{darker:0.00} against {approved:0.00}");

        // Switching the glass while the island is drawn changes the picture, to exactly what a fresh view of that glass draws.
        var live = new OffscreenScene(Light);
        var frame = OffscreenScene.RestFrame(media, 0.3);
        var before = live.Render(frame, media, Scale);
        live.SetGlassAlpha(LookConstants.GlassDarkerAlpha);
        var after = live.Render(frame, media, Scale);
        var fresh = new OffscreenScene(Light, LookConstants.GlassDarkerAlpha).Render(frame, media, Scale);
        report.Check("switching the glass in a running view changes the picture to what the darker glass draws", !before.SameAs(after) && after.SameAs(fresh), "same pixels as a fresh darker view");
    }

    /// <summary>The icon of explorer.exe, as 32-bit pixels.</summary>
    private static IconImage? ExplorerIcon()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var bitmap = icon.ToBitmap();
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                return new IconImage(bitmap.Width, bitmap.Height, bytes);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
