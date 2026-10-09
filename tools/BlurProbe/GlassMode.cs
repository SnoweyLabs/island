using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Island.Core;
using Island.Glass;

namespace BlurProbe;

/// <summary>
/// Glass mode (WORK-ORDER-3 section 8): the same pattern window and the same one permitted capture as the first probe, but the
/// blur comes from <see cref="GlassLayer"/>, the library the island will use, sitting beneath a stand-in for the island's window.
/// Writes review/glass/glass.json and the pictures. Run: BlurProbe.exe --glass [--out folder]
/// </summary>
internal static partial class GlassMode
{
    // ---- chosen by the author of this probe, not measured ----
    const int StripePx = 40;   // wider than the first probe's 20 px: sigma 18 flattens 20 px stripes almost to plain grey
    const int PatternW = 1100, PatternH = 420;
    const int AnchorW = 700, AnchorH = 220;   // the stand-in island window, physical pixels
    const double CapsuleW = 500, CapsuleH = 76, CapsuleR = 38, BallD = 30, TopDip = 72;   // sizes from the brief and LookConstants
    const int InsideMarginCapsule = 10, InsideMarginBall = 4, OutsideMargin = 4;
    const double CpuMovingSeconds = 4, CpuStillSeconds = 2;
    const int WatchdogSeconds = 240;

    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static int Run(string[] args)
    {
        Program.outDir = Program.ResolveOutDir(args, "glass");
        Directory.CreateDirectory(Program.outDir);
        var root = new JsonObject
        {
            ["tool"] = "BlurProbe --glass",
            ["generated_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["windows_build"] = Environment.OSVersion.Version.ToString(),
        };
        var verdict = new Verdict();
        try
        {
            Facts(root);
            Measure(root, verdict);
        }
        catch (Exception e)
        {
            verdict.Fail("probe_crashed");
            root["crash"] = Program.Clean(e.GetType().Name + " " + e.HResult.ToString("X8"));
        }
        finally
        {
            Win.DestroyAll();
            verdict.WriteTo(root);
            root["pictures"] = new JsonArray(Program.Saved.Select(s => (JsonNode)s).ToArray());
            File.WriteAllText(Path.Combine(Program.outDir, "glass.json"), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("verdict: " + verdict.Word);
        Console.WriteLine(verdict.Detail);
        return 0;
    }

    static void Facts(JsonObject root)
    {
        var (sw, sh) = Win.ScreenPx;
        root["display"] = new JsonObject { ["primary_px"] = $"{sw}x{sh}", ["system_dpi"] = (int)Win.SystemDpi, ["remote_session"] = Win.RemoteSession, ["input_desktop_available"] = Win.InputDesktopAvailable() };
        bool? fx = null;
        try { fx = new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled; } catch (COMException) { }
        root["windows_effects"] = new JsonObject { ["advanced_effects_enabled"] = fx, ["note"] = "read only; nothing on this machine was changed" };
        root["settings"] = new JsonObject
        {
            ["stripe_px"] = StripePx, ["anchor_window_px"] = $"{AnchorW}x{AnchorH}",
            ["capsule_dip"] = $"{CapsuleW}x{CapsuleH} radius {CapsuleR}", ["ball_dip"] = BallD, ["shape_top_dip"] = TopDip,
            ["glass_recipe"] = $"blur sigma {GlassLayer.BlurDip} dip, saturate {GlassLayer.Saturation}, brightness {GlassLayer.Brightness} (reference .isl-d .body)",
            ["spring"] = $"Island.Core.Spring: stiffness {LookConstants.SpringStiffness}, damping {LookConstants.SpringDamping}, steps of 1/{LookConstants.SpringStepsPerSecond} s",
            ["thresholds"] = "as review/blur/blur.json (Program.cs constants), plus the margins in GlassMode.cs",
        };
    }

    static void Measure(JsonObject root, Verdict verdict)
    {
        if (!Win.InputDesktopAvailable()) { verdict.NotMeasured("the screen is locked (or a secure desktop is showing)"); return; }
        Win.StripePx = StripePx;

        // A layer with no anchor must refuse quietly, with a code.
        using (var bad = new GlassLayer(0)) root["refusal_without_anchor"] = new JsonObject { ["available"] = bad.IsAvailable, ["reason"] = bad.UnavailableReason };

        var (sw, sh) = Win.ScreenPx;
        int ax = (sw - AnchorW) / 2, ay = (sh - AnchorH) / 2;
        var clicks = ClickChecks(ax, ay);
        root["click_through"] = clicks.Json;

        var (cx, cy, cw, ch) = Win.CreatePattern(PatternW, PatternH);
        ax = cx + (cw - AnchorW) / 2;
        ay = cy + (ch - AnchorH) / 2;
        Win.Pump(400);
        bool front = Win.EnsurePatternForeground();
        root["foregroundGranted"] = front;
        root["pattern_window"] = new JsonObject { ["client_px"] = $"{cw}x{ch}", ["foreground_after_ensure"] = front, ["focus_after_ensure"] = Win.PatternHasFocus() };

        var pictures = Pictures(root, verdict, clicks, ax, ay, cx, cy, cw, ch, front);
        GlassClickThrough? chosen = pictures.Chosen ?? (clicks.Passing.Count > 0 ? clicks.Passing[0] : null);
        root["chosen_click_through"] = chosen?.ToString();
        if (chosen == null) { verdict.Fail("no_click_through_mode_works"); return; }
        root["timing"] = Timing(chosen.Value, ax, ay);
        root["glass_activation_messages"] = GlassWindow.Activations;
        root["glass_focus_messages"] = GlassWindow.FocusMessages;
        if (GlassWindow.Activations != 0 || GlassWindow.FocusMessages != 0) verdict.Fail("glass_took_focus");
        if (!clicks.Passing.Contains(chosen.Value)) verdict.Fail("clicks_do_not_reach_other_program");
    }

    /// <summary>Shapes in device-independent pixels, measured from the anchor's top-left.</summary>
    internal readonly record struct Dip(double L, double T, double W, double H, double R)
    {
        public void FollowOn(GlassLayer g) => g.Follow(L, T, W, H, R);

        public Shape ToClient(int anchorClientX, int anchorClientY, double scale) =>
            new(anchorClientX + L * scale, anchorClientY + T * scale, W * scale, H * scale, R * scale);
    }

    static double Scale => Win.SystemDpi / 96.0;

    static Dip Capsule => new((AnchorW / Scale - CapsuleW) / 2, TopDip, CapsuleW, CapsuleH, CapsuleR);

    static Dip BallAt(double centreDip) => new(centreDip - BallD / 2, TopDip, BallD, BallD, BallD / 2);

    static void DwmFrames(int n) { for (int i = 0; i < n; i++) { Win.DwmFlush(); Win.Pump(0); } }

    sealed class Verdict
    {
        readonly List<string> failed = new();
        string? notMeasured;

        public void Fail(string criterion) { if (!failed.Contains(criterion)) failed.Add(criterion); }

        public void NotMeasured(string why) => notMeasured = notMeasured == null ? why : notMeasured + "; " + why;

        public bool Measured => notMeasured == null;

        public string Word => failed.Contains("no_click_through_mode_works") || failed.Contains("no_mode_both_blurs_and_lets_clicks_through") || failed.Contains("probe_crashed") ? "NO"
            : failed.Count == 0 && notMeasured == null ? "YES" : "PARTLY";

        public string Detail => Word switch
        {
            "YES" => "blurred inside and sharp outside for the ball, the open capsule and the middle of the expansion; clicks reach another program; the glass never took focus",
            _ => $"failed: [{string.Join(", ", failed)}]" + (notMeasured == null ? "" : $"; not measured: {notMeasured}"),
        };

        public void WriteTo(JsonObject root)
        {
            root["verdict"] = Word;
            root["verdict_detail"] = Detail;
            root["failed_criteria"] = new JsonArray(failed.Select(f => (JsonNode)f).ToArray());
            root["not_measured"] = notMeasured;
        }
    }
}
