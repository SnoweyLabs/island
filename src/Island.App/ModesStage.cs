using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-7 section 1, with the island's clock frozen at one moment for all three modes: the Focus capsule is pixel for pixel the one
/// drawn with the mode mark left out altogether; the DND capsule has no pixel of the page colour on its outer rim and no moving light;
/// Vibe's glow breathes with the clock and nothing else; the three go side by side, labelled, into review/choices/modes.png. The sound is
/// refused by the gate and counted.
/// </summary>
internal sealed class ModesStage(SelfTestReport report, string folder)
{
    private const double Scale = 2;
    private const double FrozenSeconds = 0.8;
    private static readonly Rgb Backdrop = new(27, 31, 58);

    public void Run()
    {
        var page = Pages.Placeholder(PageIds.Media);
        var pageColour = Rgb.FromHex(page.Page.Color);
        var frame = OffscreenScene.RestFrame(page, 0.3);

        Snapshot Draw(ModeMark.Look? look, double arcHead = 0.3)
        {
            var scene = new OffscreenScene(Backdrop);
            if (look is { } l) scene.SetModeLook(l);
            return scene.Render(OffscreenScene.RestFrame(page, arcHead), page, Scale);
        }

        var left = Draw(null); // the mark left out altogether
        var focus = Draw(ModeMark.LookOf(Mode.Focus, FrozenSeconds));
        var vibe = Draw(ModeMark.LookOf(Mode.Vibe, FrozenSeconds));
        var dnd = Draw(ModeMark.LookOf(Mode.DND, FrozenSeconds));

        report.Check("the Focus capsule is identical, pixel for pixel, to one drawn with the mode mark left out", focus.SameAs(left), "same pixels");
        report.Check("Vibe differs from Focus (the glow behind the edge is at the low end of its breath)", !vibe.SameAs(focus), "different pixels");
        report.Check("DND differs from Focus", !dnd.SameAs(focus), "different pixels");

        // The outer rim: the strip along the top edge between the corners, 1.5 device-independent pixels deep.
        var focusRim = PageColoured(focus, frame, pageColour);
        var vibeRim = PageColoured(vibe, frame, pageColour);
        var dndRim = PageColoured(dnd, frame, pageColour);
        report.Check("the Focus and Vibe edge carry the page colour (so the test can see it)", focusRim > 0 && vibeRim > 0, $"{focusRim} and {vibeRim} pixels");
        report.Check("the DND capsule has no pixel of the page colour on its outer rim", dndRim == 0, $"{dndRim} pixel(s) of the page colour");

        // The moving light: it moves in Focus and Vibe and is absent in DND.
        var dndMoved = Draw(ModeMark.LookOf(Mode.DND, FrozenSeconds), arcHead: 0.7);
        var focusMoved = Draw(ModeMark.LookOf(Mode.Focus, FrozenSeconds), arcHead: 0.7);
        report.Check("the moving light moves in Focus and does not exist in DND", !focus.SameAs(focusMoved) && dnd.SameAs(dndMoved), "DND is the same wherever the light would be");

        // Breathing depends on time only: the same moment gives the same picture, half a period later it is different, a whole period later the same.
        var vibeAgain = Draw(ModeMark.LookOf(Mode.Vibe, FrozenSeconds));
        var vibeHalf = Draw(ModeMark.LookOf(Mode.Vibe, FrozenSeconds + ModeMark.BreathSeconds / 2));
        var vibeWhole = Draw(ModeMark.LookOf(Mode.Vibe, FrozenSeconds + ModeMark.BreathSeconds));
        report.Check("Vibe's glow breathes with the clock alone", vibe.SameAs(vibeAgain) && !vibe.SameAs(vibeHalf) && NearlySame(vibe, vibeWhole), "same moment same picture; half a period later another; a whole period later the same");

        // The sound that goes with a notice that may not show: refused by the gate, counted.
        var refusedBefore = OutsideGate.Current.Refused(OutsideKind.PlaySound);
        var played = OutsideSound.PlayNotification();
        report.Check("the notification sound is refused by the gate under the self-test and counted", !played && OutsideGate.Current.Refused(OutsideKind.PlaySound) == refusedBefore + 1, "nothing was played");

        Compose(focus, vibe, dnd, frame);
        report.Check("the three modes side by side were drawn into review/choices/modes.png", File.Exists(Path.Combine(folder, "choices", "modes.png")), "a snapshot proves it draws, not that it looks right");
    }

    /// <summary>The picture of a whole breath period later differs by rounding only (the clock is a floating-point number).</summary>
    private static bool NearlySame(Snapshot a, Snapshot b) => a.DiffBounds(b) is null;

    private static int PageColoured(Snapshot snap, ShapeFrame frame, Rgb page)
    {
        var y0 = (int)Math.Floor(frame.Top * Scale);
        var y1 = (int)Math.Ceiling((frame.Top + 1.5) * Scale);
        var x0 = (int)((frame.Left + frame.Radius) * Scale);
        var x1 = (int)((frame.Left + frame.Width - frame.Radius) * Scale);
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var (r, g, b, a) = snap.At(x, y);
                if (a < 40) continue;
                // Near the page colour in hue: the page's strongest channel clearly above the others, as the page colour has it.
                var dominant = page.R >= page.G && page.R >= page.B ? r - Math.Max(g, b) : page.G >= page.B ? g - Math.Max(r, b) : b - Math.Max(r, g);
                if (dominant > 60) count++;
            }
        }

        return count;
    }

    private void Compose(Snapshot focus, Snapshot vibe, Snapshot dnd, ShapeFrame frame)
    {
        var width = (int)Math.Round((frame.Width + 80) * Scale);
        var height = (int)Math.Round((frame.Top + frame.Height + 60) * Scale);
        var x = (int)Math.Round((frame.CentreX - frame.Width / 2 - 40) * Scale);
        var grid = new Grid { Background = Paint.Brush(Backdrop), Width = 3 * (width / Scale) + 4 * 16, Height = height / Scale + 44 };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var parts = new[] { ("Focus: the approved edge", focus), ("Vibe: the glow breathes", vibe), ("DND: a dashed rim, no colour", dnd) };
        for (var i = 0; i < parts.Length; i++)
        {
            var (label, snap) = parts[i];
            var cell = new StackPanel { Margin = new Thickness(16, 8, 0, 0) };
            cell.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            cell.Children.Add(new Image
            {
                Source = new CroppedBitmap(snap.Bitmap, new Int32Rect(Math.Max(0, x), 0, Math.Min(width, snap.Width - Math.Max(0, x)), Math.Min(height, snap.Height))),
                Stretch = Stretch.None,
                Width = width / Scale,
                Height = height / Scale,
            });
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        grid.UpdateLayout();
        Snapshot.Of(grid, (int)Math.Round(grid.Width * Scale), (int)Math.Round(grid.Height * Scale), 96 * Scale).SavePng(Path.Combine(folder, "choices", "modes.png"));
    }
}
