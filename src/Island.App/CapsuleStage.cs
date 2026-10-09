using System.Globalization;
using System.IO;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// Section 4 checks: the ball and the five capsules drawn standing still, on a dark and a light
/// flat background, as twelve snapshots. Snapshots render the island's own elements, never the screen.
/// </summary>
internal static class CapsuleStage
{
    /// <summary>Snapshot scale: 2 gives 1360 by 380 pixels, enough detail to judge the glass.</summary>
    private const double Scale = 2;

    /// <summary>Fraction along the rim where the first arc's head rests in a snapshot.</summary>
    private const double ArcHead = 0.30;

    /// <summary>A point on the base rim that neither arc covers (arcs cover 0.02..0.30 and 0.68..0.80).</summary>
    private const double FreeRimFraction = 0.55;

    private static readonly Rgb Dark = new(27, 31, 58);
    private static readonly Rgb Light = new(242, 243, 246);

    private const double WcagNormalText = 4.5;

    public static void Run(SelfTestReport report, string folder)
    {
        RecordBlurUnits(report);

        var dark = new OffscreenScene(Dark);
        var light = new OffscreenScene(Light);
        var darkSnaps = new List<(PageContents Category, Snapshot Snap)>();

        // The ball, then each capsule: dark and light.
        Save(dark.Render(OffscreenScene.RestFrame(null, ArcHead), null, Scale), folder, "ball-dark.png");
        Save(light.Render(OffscreenScene.RestFrame(null, ArcHead), null, Scale), folder, "ball-light.png");

        foreach (var category in Pages.AllPlaceholders)
        {
            var frame = OffscreenScene.RestFrame(category, ArcHead);
            var d = dark.Render(frame, category, Scale);
            var l = light.Render(frame, category, Scale);
            Save(d, folder, $"{category.Page.Id}-dark.png");
            Save(l, folder, $"{category.Page.Id}-light.png");
            darkSnaps.Add((category, d));

            CheckRimColour(report, category, "dark", d, frame);
            CheckRimColour(report, category, "light", l, frame);
            CheckDrawnSize(report, category);
        }

        CheckAllDiffer(report, darkSnaps);
        MeasureContrast(report, folder);

        // EVALS D1: the ball and a capsule of every page, dark and light, were drawn into the folder.
        var expected = new[] { "ball-dark.png", "ball-light.png" }.Concat(Pages.AllPlaceholders.SelectMany(c => new[] { $"{c.Page.Id}-dark.png", $"{c.Page.Id}-light.png" })).ToList();
        var missing = expected.Where(n => !File.Exists(Path.Combine(folder, n))).ToList();
        report.Check("the snapshots ball-dark, ball-light and <page>-dark and -light exist for every page", missing.Count == 0, missing.Count == 0 ? $"{expected.Count} pictures" : "missing: " + string.Join(", ", missing));

        // EVALS P3: a page made by the person lights the edge in its own colour, not in the colour of a built-in page that it is looked up by.
        var custom = new PageContents(new Page("page-9", "Games", "#7CE04A", "G", null, false), Pages.Placeholder(PageIds.Apps).Items);
        var customFrame = OffscreenScene.RestFrame(custom, ArcHead);
        var customSnap = dark.Render(customFrame, custom, Scale);
        var rect = customFrame.Rect;
        var inset = LookConstants.RimInset;
        var perimeter = new RoundedPerimeter(rect.X + inset, rect.Y + inset, rect.Width - 2 * inset, rect.Height - 2 * inset, customFrame.Radius - inset);
        var point = perimeter.PointAt(FreeRimFraction);
        var (cr, cg, cb, _) = customSnap.At((int)Math.Round(point.X * Scale), (int)Math.Round(point.Y * Scale));
        var all = Pages.AllPlaceholders.Select(c => c.Page).Append(custom.Page).Select(pg => (pg, d: Distance(Rgb.FromHex(pg.Color), cr, cg, cb))).OrderBy(x => x.d).ToList();
        report.Check("a page made by the person lights the edge in its own colour: a rim pixel is closest to that page's colour of all the pages", all[0].pg.Id == custom.Page.Id,
            $"pixel rgb({cr},{cg},{cb}); closest {all[0].pg.Id} at {Fmt(all[0].d)}, next {all[1].pg.Id} at {Fmt(all[1].d)}");
    }

    private static void RecordBlurUnits(SelfTestReport report)
    {
        var rows = BlurCalibration.Measure(10, 30, 75);
        report.Info["blurUnits"] = BlurCalibration.Describe(rows) + $"; used: radius = {Units.WpfRadiusPerSigma} x sigma";
        report.Check("WPF BlurEffect radius is three times the Gaussian sigma (the factor used to translate CSS blur)",
            rows.All(r => Math.Abs(r.Radius / r.Sigma - Units.WpfRadiusPerSigma) < 0.1),
            BlurCalibration.Describe(rows));
    }

    private static void Save(Snapshot snapshot, string folder, string name) =>
        snapshot.SavePng(Path.Combine(folder, name));

    /// <summary>The drawn capsule is as big as CapsuleLayout says: measured on the front layer alone.</summary>
    private static void CheckDrawnSize(SelfTestReport report, PageContents category)
    {
        var expected = CapsuleLayout.SizeFor(category);
        var front = new OffscreenScene(null, includeBack: false) { RimVisible = false };
        var snap = front.Render(OffscreenScene.RestFrame(category, ArcHead), null, Scale);
        var box = snap.Bounds(128);
        if (box is null)
        {
            report.Check($"{category.Page.Id}: drawn capsule size equals CapsuleLayout", false, "nothing was drawn");
            return;
        }

        var width = (box.Value.Right - box.Value.Left + 1) / Scale;
        var height = (box.Value.Bottom - box.Value.Top + 1) / Scale;
        var ok = Math.Abs(width - expected.Width) <= 1.0 && Math.Abs(height - expected.Height) <= 1.0;
        report.Check($"{category.Page.Id}: drawn capsule size equals CapsuleLayout", ok,
            $"drawn {Fmt(width)} x {Fmt(height)}, layout {Fmt(expected.Width)} x {Fmt(expected.Height)} (glass body alone, tolerance 1, at 50% alpha; the rim reaches 0.3 beyond the edge by design)");
    }

    private static void CheckAllDiffer(SelfTestReport report, List<(PageContents Category, Snapshot Snap)> snaps)
    {
        var same = new List<string>();
        for (var i = 0; i < snaps.Count; i++)
            for (var j = i + 1; j < snaps.Count; j++)
                if (snaps[i].Snap.SameAs(snaps[j].Snap))
                    same.Add($"{snaps[i].Category.Page.Id}={snaps[j].Category.Page.Id}");

        report.Check("the five dark capsule snapshots all differ from one another", same.Count == 0,
            same.Count == 0 ? "all pairs differ" : "identical: " + string.Join(", ", same));
    }

    /// <summary>A pixel on the base rim, away from the arcs, is closest to its own category colour.</summary>
    private static void CheckRimColour(SelfTestReport report, PageContents category, string background, Snapshot snap, ShapeFrame frame)
    {
        var line = System.Windows.Rect.Empty;
        var rect = frame.Rect;
        var inset = LookConstants.RimInset;
        line = new System.Windows.Rect(rect.X + inset, rect.Y + inset, rect.Width - 2 * inset, rect.Height - 2 * inset);
        var perimeter = new RoundedPerimeter(line.X, line.Y, line.Width, line.Height, frame.Radius - inset);
        var p = perimeter.PointAt(FreeRimFraction);
        var (r, g, b, _) = snap.At((int)Math.Round(p.X * Scale), (int)Math.Round(p.Y * Scale));

        var distances = Pages.AllPlaceholders
            .Select(c => (c, d: Distance(Rgb.FromHex(c.Page.Color), r, g, b)))
            .OrderBy(x => x.d)
            .ToList();
        var closest = distances[0].c;
        report.Check($"{category.Page.Id}-{background}: a rim pixel away from the arcs is closest to its own category colour",
            closest.Page.Id == category.Page.Id,
            $"pixel rgb({r},{g},{b}); closest {closest.Page.Id} at {Fmt(distances[0].d)}, next {distances[1].c.Page.Id} at {Fmt(distances[1].d)}");
    }

    private static double Distance(Rgb c, double r, double g, double b) =>
        Math.Sqrt((c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b));

    /// <summary>
    /// Contrast of the white title text against the glass directly behind it, on the light snapshots,
    /// WCAG 2.2 criterion 1.4.3 (normal text needs 4.5:1). The approved look is not changed: a darker
    /// alternative is rendered as a proposal.
    /// </summary>
    private static void MeasureContrast(SelfTestReport report, string folder)
    {
        var rows = new List<object>();
        var worst = (Category: Pages.AllPlaceholders[0], Ratio: double.MaxValue);

        foreach (var category in Pages.AllPlaceholders)
        {
            var ratio = TitleContrast(category, LookConstants.GlassBaseAlpha, out var glass);
            var subtitle = SubtitleContrast(glass);
            rows.Add(new { category = category.Page.Id, titleRatio = Math.Round(ratio, 2), subtitleRatio = Math.Round(subtitle, 2), glass = glass.ToHex() });
            if (ratio < worst.Ratio) worst = (category, ratio);
        }

        report.Info["titleContrastOnLight"] = rows;
        report.Info["contrastThreshold"] = $"{WcagNormalText}:1 (WCAG 2.2 1.4.3, from the work order; not re-checked at w3.org by this run)";
        var allPass = rows.Count > 0 && worst.Ratio >= WcagNormalText;
        report.Info["titleContrastMeetsThreshold"] = allPass;

        // Proposal: raise the dark tint of the glass until the worst category reaches the threshold.
        double? alpha = null;
        for (var a = LookConstants.GlassBaseAlpha; a <= 0.99; a += 0.02)
        {
            if (Pages.AllPlaceholders.All(c => TitleContrast(c, a, out _) >= WcagNormalText)) { alpha = a; break; }
        }

        var scene = new OffscreenScene(Light, alpha ?? 0.99);
        var proposal = scene.Render(OffscreenScene.RestFrame(worst.Category, ArcHead), worst.Category, Scale);
        proposal.SavePng(Path.Combine(folder, "proposal-darker-glass.png"));
        report.Info["proposalDarkerGlass"] = alpha is { } v
            ? $"dark tint at {v.ToString("0.00", CultureInfo.InvariantCulture)} instead of {LookConstants.GlassBaseAlpha.ToString(CultureInfo.InvariantCulture)} reaches {WcagNormalText}:1 for all five; shown on {worst.Category.Page.Id}"
            : "no tint up to 0.99 reached the threshold";
        report.Check("title contrast on the light snapshots was measured and recorded", rows.Count == Pages.AllPlaceholders.Count,
            $"worst {worst.Category.Page.Id} at {worst.Ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1; below {WcagNormalText}:1 is expected and recorded as a question, not fixed");
    }

    internal static double TitleContrast(PageContents category, double glassAlpha, out Rgb glass)
    {
        var scene = new OffscreenScene(Light, glassAlpha);
        var frame = OffscreenScene.RestFrame(category, ArcHead);
        var snap = scene.Render(frame, category, Scale, textVisible: false);

        var title = ContentsLayer.TitleRect(category);
        var x = (int)Math.Round((frame.Left + title.X) * Scale);
        var y = (int)Math.Round((frame.Top + title.Y) * Scale);
        var (r, g, b) = snap.Mean(x, y, (int)Math.Round(title.Width * Scale), (int)Math.Round(title.Height * Scale));
        glass = new Rgb(r, g, b);
        return Contrast(Rgb.White, glass);
    }

    private static double SubtitleContrast(Rgb glass) =>
        Contrast(ColorMath.LerpSrgb(glass, Rgb.White, LookConstants.SubtitleAlpha), glass);

    /// <summary>WCAG contrast ratio of two sRGB colours.</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Rgb c)
    {
        static double Lin(double v)
        {
            var s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
