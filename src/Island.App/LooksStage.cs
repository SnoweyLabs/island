using System.IO;
using Island.App.Visuals;
using Island.Core;
using Page = Island.Core.Page;

namespace Island.App;

/// <summary>
/// WORK-ORDER-5: the looks Dan chose, each checked on a snapshot of the island's own elements with invented items
/// and test pictures drawn in code. Nothing here reads the desktop, and a snapshot proves the island draws, not that
/// it looks right (that stays NEEDS-HUMAN-VERIFY).
/// </summary>
internal static class LooksStage
{
    private const double Scale = 2;
    private static readonly Rgb Backdrop = new(27, 31, 58);

    public static void Run(SelfTestReport report, string folder)
    {
        RoundTiles(report, folder);
        ClosedIsGrey(report);
        WindowsAreDots(report);
        LongPageSlides(report, folder);
        PlayingTileDances(report, folder);
    }

    /// <summary>A test picture: a square of one known colour, opaque, with the size a program's icon has.</summary>
    internal static IconImage Solid(byte r, byte g, byte b, int size = 256)
    {
        var bgra = new byte[size * size * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = 255;
        }

        return new IconImage(size, size, bgra);
    }

    /// <summary>Renders one row of the Apps page with the given items, the first one selected, and returns the snapshot and the first tile's centre in snapshot pixels.</summary>
    internal static (Snapshot Snap, int X, int Y) Render(Item[] items, Page? page = null, NowPlayingView? nowPlaying = null)
    {
        var contents = new PageContents(page ?? Pages.Get(PageIds.Apps), items);
        var scene = new OffscreenScene(Backdrop);
        var snap = scene.Render(OffscreenScene.RestFrame(contents, 0.3), contents, Scale, nowPlaying: nowPlaying);
        var x = (int)Math.Round(scene.Contents.TileCentreX(0) * Scale);
        var y = (int)Math.Round((LookConstants.TopGap + LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth) * Scale);
        return (snap, x, y);
    }

    private static bool Near((byte R, byte G, byte B, byte A) pixel, byte r, byte g, byte b, int tolerance) =>
        Math.Abs(pixel.R - r) <= tolerance && Math.Abs(pixel.G - g) <= tolerance && Math.Abs(pixel.B - b) <= tolerance;

    // ---- WORK-ORDER-10 §1: every tile is a disc in the icon's own colour --------------------------

    /// <summary>
    /// The six test icons of WORK-ORDER-10 §1, drawn in code, laid out as a page of six picks. Each tile's FACE is read alone, before the opacity of an unselected
    /// tile, the crescent, the ring, the glow, the dots and the bars are laid over it (<see cref="TileView.FaceOf"/>): for the first five, every pixel on a ring just
    /// inside the face's edge is opaque and of the disc's colour (the disc reaches the edge all the way round); the sixth shows letters. The snapshot, the six
    /// side by side, open and closed, goes to review/choices/round-icons.png.
    /// </summary>
    private static void RoundTiles(SelfTestReport report, string folder)
    {
        var items = RoundIconSamples.All.Select((icon, i) => new Item($"Alpha {i}", "open", $"A{i}", 215, PickId: $"program:alpha{i}", Icon: icon)).ToArray();
        const double zoom = 4;
        var d = LookConstants.ItemSize;
        var ring = (d / 2 - 1.5) * zoom;

        for (var i = 0; i < items.Length; i++)
        {
            var plan = RoundIcons_Of(items[i]);
            if (plan.Kind == RoundIconKind.Letters)
            {
                report.Check($"test icon {i + 1} (no opaque pixel) shows the two letters", i == 5, "the two-letter tile");
                continue;
            }

            var face = TileView.FaceOf(items[i], grey: false);
            face.Measure(new System.Windows.Size(d, d));
            face.Arrange(new System.Windows.Rect(0, 0, d, d));
            face.UpdateLayout();
            var snap = Snapshot.Of(face, (int)(d * zoom), (int)(d * zoom), 96 * zoom);
            var cx = d * zoom / 2;
            var all = Enumerable.Range(0, 24).Select(k => k * Math.PI / 12).Select(angle => snap.At((int)Math.Round(cx + ring * Math.Cos(angle)), (int)Math.Round(cx + ring * Math.Sin(angle)))).ToList();
            var onDisc = all.Count(p => p.A == 255 && Near(p, plan.Disc.R, plan.Disc.G, plan.Disc.B, 3));
            report.Check($"test icon {i + 1}: the disc reaches the edge all the way round, in its own colour", onDisc == all.Count, $"{onDisc} of {all.Count} points on the ring are the disc's colour and opaque");
        }

        // The snapshot: six open, and the same six closed.
        var panel = new System.Windows.Controls.StackPanel { Background = Paint.Brush(Backdrop, 1), Orientation = System.Windows.Controls.Orientation.Vertical };
        foreach (var grey in new[] { false, true })
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new System.Windows.Thickness(16) };
            foreach (var item in items) row.Children.Add(new System.Windows.Controls.Border { Child = TileView.FaceOf(item, grey), Margin = new System.Windows.Thickness(8) });
            panel.Children.Add(row);
        }

        panel.Measure(new System.Windows.Size(420, 160));
        panel.Arrange(new System.Windows.Rect(0, 0, 420, 160));
        panel.UpdateLayout();
        var path = Path.Combine(folder, "choices", "round-icons.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Snapshot.Of(panel, 420 * 3, 160 * 3, 96 * 3).SavePng(path);
        report.Check("the six test icons, open and closed, were drawn into review/choices/round-icons.png", File.Exists(path), "a snapshot proves it draws, not that it looks right");
    }

    private static RoundIconPlan RoundIcons_Of(Item item) => Island.Core.RoundIcons.Of(item.Icon!).Plan;

    // ---- §2 A closed pick is grey ---------------------------------------------------------------

    private static void ClosedIsGrey(SelfTestReport report)
    {
        const byte r = 230, g = 40, b = 60;
        var pretend = new PretendWorld();
        pretend.Icons["alpha.exe"] = Solid(r, g, b, 64);
        var world = AppWorld.Pretend(pretend);
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        var apps = Pages.Get(PageIds.Apps);

        // No window of the program exists: the pick is closed and the picture its tile draws has no colour in any pixel.
        var closed = pages.ItemsOf(apps)[0];
        var closedPicture = TileOf(closed).IconPicture;
        report.Check("a closed pick's own picture has equal red, green and blue in every pixel",
            closed.IsClosed && closedPicture is not null && Pixels(closedPicture).All(p => p.R == p.G && p.G == p.B),
            $"{(closedPicture is null ? 0 : Pixels(closedPicture).Count())} pixels read");

        // A window of the program exists (an invented one): the pick is open and its picture is the known colour.
        pretend.Windows = [new OpenWindow(1001, "alpha.exe", null, "invented window", 0)];
        pages.Invalidate();
        var open = pages.ItemsOf(apps)[0];
        var openPicture = TileOf(open).IconPicture;
        report.Check("an open pick's own picture is the known colour",
            !open.IsClosed && openPicture is not null && Pixels(openPicture).All(p => Math.Abs(p.R - r) <= 1 && Math.Abs(p.G - g) <= 1 && Math.Abs(p.B - b) <= 1),
            open.IsClosed ? "still closed" : "colour kept");

        // The tile of a closed pick is drawn grey on the island too: no hue in the pixels at its centre.
        var (snap, cx, cy) = Render([closed], nowPlaying: null);
        var centre = snap.At(cx, cy);
        report.Check("a closed pick's tile is drawn without colour", Math.Abs(centre.R - centre.G) <= 6 && Math.Abs(centre.G - centre.B) <= 14, $"centre pixel spread {Math.Max(centre.R, Math.Max(centre.G, centre.B)) - Math.Min(centre.R, Math.Min(centre.G, centre.B))}");
    }

    private static TileView TileOf(Item item)
    {
        var scene = new OffscreenScene(Backdrop);
        var contents = new PageContents(Pages.Get(PageIds.Apps), [item]);
        scene.Render(OffscreenScene.RestFrame(contents, 0.3), contents, Scale);
        return scene.Contents.TileAt(0);
    }

    private static IEnumerable<(byte B, byte G, byte R, byte A)> Pixels(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var bytes = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(bytes, stride, 0);
        for (var i = 0; i + 3 < bytes.Length; i += 4) yield return (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]);
    }

    // ---- §3 Several windows show as dots --------------------------------------------------------

    private static void WindowsAreDots(SelfTestReport report)
    {
        TileView TileWith(int windows) => TileOf(new Item("Alpha", windows == 1 ? "open" : $"{windows} windows", "Al", 215, PickId: "program:alpha", Count: windows));

        var counts = new[] { 0, 1, 2, 3, 5, 6 }.Select(n => (n, dots: TileWith(n).DotCount)).ToList();
        report.Check("a tile draws no dot for none or one window, that many for two to five, and five for six",
            counts.All(c => c.dots == WindowDots.For(c.n)) && counts.Single(c => c.n == 2).dots == 2 && counts.Single(c => c.n == 6).dots == 5 && counts.Single(c => c.n == 1).dots == 0,
            string.Join(", ", counts.Select(c => $"{c.n} windows: {c.dots}")));

        // The snapshot has white dots under the tile with two windows and not with one.
        var (two, cx, cy) = Render([new Item("Alpha", "2 windows", "Al", 215, PickId: "program:alpha", Count: 2)]);
        var (one, _, _) = Render([new Item("Alpha", "open", "Al", 215, PickId: "program:alpha", Count: 1)]);
        var below = cy + (int)Math.Round((LookConstants.ItemSize / 2 + ChoiceConstants.DotCentreBelowTile) * Scale);
        var half = (int)Math.Round((ChoiceConstants.DotSize + ChoiceConstants.DotGap) / 2 * Scale);
        bool White((byte R, byte G, byte B, byte A) p) => p.R > 235 && p.G > 235 && p.B > 235;
        var twoDots = White(two.At(cx - half, below)) && White(two.At(cx + half, below));
        var oneDots = White(one.At(cx - half, below)) || White(one.At(cx + half, below)) || White(one.At(cx, below));
        report.Check("two windows draw two white dots under the tile and one window draws none", twoDots && !oneDots, $"two windows: {twoDots}, one window: {oneDots}");
    }

    // ---- §4 A long page slides sideways ---------------------------------------------------------

    private static void LongPageSlides(SelfTestReport report, string folder)
    {
        // Twelve invented picks, the row slid by two and a half tiles: both ends fade and both arrows show.
        var items = Enumerable.Range(0, 12).Select(i => new Item($"Pick {i + 1}", "open", $"P{i + 1}", i * 30 % 360, PickId: $"program:p{i}", Count: i == 1 ? 2 : 0)).ToList();
        items.Add(new Item("Add to the island", "what is open now", "+", 0, IsPlus: true));
        var contents = new PageContents(Pages.Get(PageIds.Apps), items);
        var scene = new OffscreenScene(Backdrop);
        var slid = scene.Render(OffscreenScene.RestFrame(contents, 0.3), contents, Scale, stripPosition: 2.5);
        slid.SavePng(Path.Combine(folder, "choices", "strip.png"));
        report.Check("a slid row has an arrow on each side and is drawn", scene.Contents.ArrowsShown == (true, true) && slid.Bounds(8) is not null, $"arrows {scene.Contents.ArrowsShown}");
    }

    // ---- §7 The tile that is playing dances -----------------------------------------------------

    private static void PlayingTileDances(SelfTestReport report, string folder)
    {
        // The Media page, drawn from invented items; the rim light's clock is held still (the arc head is the same in both pictures).
        var items = new List<Item>
        {
            new("Tunes", "open", "Tu", 350, PickId: "program:tunes"),
            new("Clips", "website", "Cl", 140, PickId: "site:example.org"),
            new(PlusRow.AddTitle, PlusRow.AddSubtitle(0), "+", 0, IsPlus: true),
        };
        var contents = new PageContents(Pages.Get(PageIds.Media), items);
        var scene = new OffscreenScene(Backdrop);
        var frame = OffscreenScene.RestFrame(contents, 0.3);
        scene.Render(frame, contents, Scale);

        scene.Contents.SetEqualizer(0, Equalizer.Heights(1.0, playing: true));
        var first = scene.Capture(Scale);
        scene.Contents.SetEqualizer(0, Equalizer.Heights(1.3, playing: true)); // 0.3 s of the island's own clock later
        var second = scene.Capture(Scale);

        first.SavePng(Path.Combine(folder, "choices", "playing-tile.png"));
        var cx = (int)Math.Round(scene.Contents.TileCentreX(0) * Scale);
        var cy = (int)Math.Round((LookConstants.TopGap + LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth) * Scale);
        var half = (int)Math.Ceiling(LookConstants.ItemSize / 2 * Scale) + 2;
        var diff = first.DiffBounds(second);
        report.Check("0.3 s apart the two pictures differ inside the playing tile and nowhere else",
            diff is { } d && d.Left >= cx - half && d.Right <= cx + half && d.Top >= cy - half && d.Bottom <= cy + half,
            diff is { } b ? $"differences between x {b.Left}..{b.Right}, y {b.Top}..{b.Bottom}; the tile is x {cx - half}..{cx + half}, y {cy - half}..{cy + half}" : "no difference");

        scene.Contents.SetEqualizer(0, Equalizer.Heights(1.0, playing: false));
        var stillA = scene.Capture(Scale);
        scene.Contents.SetEqualizer(0, Equalizer.Heights(1.3, playing: false));
        var stillB = scene.Capture(Scale);
        report.Check("paused, the pictures 0.3 s apart are the same", stillA.SameAs(stillB), "the bars stand still");
    }
}
