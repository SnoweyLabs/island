using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlurProbe;

internal static class Program
{
    // ---- chosen by the author of this probe, not measured (see README.md) ----
    const int StripePx = 20;          // width of one black or white stripe
    const float SigmaPx = 8f;         // Gaussian standard deviation of the system blur
    const int PatternW = 1100, PatternH = 420;
    const int ProbeW = 700, ProbeH = 220;
    const int CapsuleW = 500, CapsuleH = 76, CapsuleR = 38, BallD = 30;
    const int InsideMarginCapsule = 10, InsideMarginBall = 4, OutsideMargin = 4;   // pixels from the outline that are not judged
    internal const double SharpMaxNonPure = 0.002;      // outside: at most 0.2% of pixels may be anything but pure black / white
    const double BlurredMaxPure = 0.05;        // inside: at most 5% pure pixels
    const int FlatRange = 6;                   // inside: a grey range under this means "no stripes visible at all"
    const int WatchdogSeconds = 150;

    static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    internal static string outDir = "";
    internal static readonly List<string> Saved = new();

    [STAThread]
    static int Main(string[] args)
    {
        // Two further modes, added for WORK-ORDER-3 section 8; with neither flag the probe does exactly what it did before.
        if (args.Contains("--click-target")) return Win.RunClickTarget(args);
        if (args.Contains("--glass")) return GlassMode.Run(args);
        outDir = ResolveOutDir(args, "blur");
        Directory.CreateDirectory(outDir);
        var root = new JsonObject
        {
            ["tool"] = "BlurProbe",
            ["generated_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["windows_build"] = Environment.OSVersion.Version.ToString(),
        };
        string verdict = "NOT-MEASURED", detail = "the probe did not reach the measurement";
        JsonArray failed = new();
        string? methodThatDid = null;
        try
        {
            Facts(root);
            Run(root, out verdict, out detail, failed, out methodThatDid);
        }
        catch (Exception e)
        {
            detail = "probe crashed before a verdict: " + Clean(e.GetType().Name + ": " + e.Message);
            root["crash"] = detail;
        }
        finally
        {
            Win.DestroyAll();
            root["verdict"] = verdict;
            root["verdict_detail"] = detail;
            root["failed_criteria"] = failed;
            root["method_that_did_it"] = methodThatDid;
            root["pictures"] = new JsonArray(Saved.Select(s => (JsonNode)s).ToArray());
            File.WriteAllText(Path.Combine(outDir, "blur.json"), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("verdict: " + verdict);
        Console.WriteLine(detail);
        return verdict == "NOT-MEASURED" ? 2 : 0;
    }

    internal static string ResolveOutDir(string[] args, string folder)
    {
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--out") return Path.GetFullPath(args[i + 1]);
        // default: review/blur beside the solution file, found by walking up from the executable
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Island.sln"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? Directory.GetCurrentDirectory(), "review", folder);
    }

    // Keeps the account name and profile folder out of anything that is written to a file.
    internal static string Clean(string s)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) s = s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(Environment.UserName)) s = s.Replace(Environment.UserName, "<account>", StringComparison.OrdinalIgnoreCase);
        return s;
    }

    static void Facts(JsonObject root)
    {
        var (sw, sh) = Win.ScreenPx;
        root["display"] = new JsonObject { ["primary_px"] = $"{sw}x{sh}", ["system_dpi"] = (int)Win.SystemDpi, ["remote_session"] = Win.RemoteSession, ["input_desktop_available"] = Win.InputDesktopAvailable() };
        var fx = new JsonObject();
        try { fx["advanced_effects_enabled"] = new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled; }
        catch (Exception e) { fx["advanced_effects_enabled"] = null; fx["advanced_effects_error"] = Clean(e.Message); }
        try { fx["energy_saver_status"] = Windows.System.Power.PowerManager.EnergySaverStatus.ToString(); }
        catch (Exception e) { fx["energy_saver_status"] = null; fx["energy_saver_error"] = Clean(e.Message); }
        fx["note"] = "read only; nothing on this machine was changed";
        root["windows_effects"] = fx;
        root["settings"] = new JsonObject
        {
            ["stripe_px"] = StripePx, ["blur_sigma_px"] = SigmaPx, ["probe_window_px"] = $"{ProbeW}x{ProbeH}",
            ["capsule_px"] = $"{CapsuleW}x{CapsuleH} radius {CapsuleR}", ["ball_px"] = BallD,
            ["sizes_are"] = "physical pixels (the probe is per-monitor-v2 DPI aware)",
            ["thresholds"] = new JsonObject
            {
                ["outside_sharp_max_nonpure_fraction"] = SharpMaxNonPure, ["inside_blurred_max_pure_fraction"] = BlurredMaxPure,
                ["inside_flat_if_grey_range_below"] = FlatRange, ["pure_pixel_tolerance"] = 8,
                ["inside_margin_capsule_px"] = InsideMarginCapsule, ["inside_margin_ball_px"] = InsideMarginBall, ["outside_margin_px"] = OutsideMargin
            }
        };
    }

    // ------------------------------------------------------------------------------------------------

    internal sealed record Snap(Pixels? Image, string Reason, int Tries);

    static void Run(JsonObject root, out string verdict, out string detail, JsonArray failed, out string? methodThatDid)
    {
        verdict = "NOT-MEASURED"; detail = ""; methodThatDid = null;
        if (!Win.InputDesktopAvailable()) { detail = "NOT MEASURED: the screen is locked (or a secure desktop is showing), so no window could be drawn or photographed."; return; }

        Win.StripePx = StripePx;
        try { GlassHost.Init(); root["compositor"] = "created"; }
        catch (Exception e) { root["compositor"] = "FAILED: " + Clean(e.Message); detail = "could not create the system compositor: " + Clean(e.Message); verdict = "NO"; return; }

        var (cx, cy, cw, ch) = Win.CreatePattern(PatternW, PatternH);
        int pw = Math.Min(ProbeW, cw - 20), ph = Math.Min(ProbeH, ch - 20);
        int px0 = (cw - pw) / 2, py0 = (ch - ph) / 2;          // probe window origin, in pattern client pixels
        Win.Pump(400);
        bool front = Win.EnsurePatternForeground();
        root["pattern_window"] = new JsonObject { ["client_px"] = $"{cw}x{ch}", ["foreground_after_ensure"] = front, ["focus_after_ensure"] = Win.PatternHasFocus() };

        // control: the pattern alone, no probe window. Proves the capture is exact and the stripes are pure.
        var control = CaptureStable(5000);
        var ctl = new JsonObject { ["taken"] = control.Image != null, ["reason"] = control.Reason };
        root["control"] = ctl;
        if (control.Image == null)
        {
            detail = "NOT MEASURED: the pattern window could not be photographed: " + control.Reason +
                (control.Reason.Contains("foreground") || control.Reason.Contains("focus") ? " (the probe could not take focus; Windows may be refusing foreground changes)" : "");
            return;
        }
        int inset = Win.CaptureInsetPx;
        var all = Imaging.Measure(control.Image, (x, y) => x >= inset && x < cw - inset && y >= inset && y < ch - inset);
        ctl["frame_px_left_grey_and_not_photographed"] = inset;
        ctl["nonpure_fraction"] = Math.Round(all.NonPureFraction, 5);
        ctl["gray_min"] = all.Min; ctl["gray_max"] = all.Max;
        bool exact = all.NonPureFraction <= SharpMaxNonPure && all.Min <= 8 && all.Max >= 247;
        ctl["capture_exact"] = exact;
        Save("control-pattern.png", control.Image);
        if (!exact)
        {
            detail = "NOT MEASURED: the photograph of the bare stripe pattern is not pure black and white (non-pure fraction " + Math.Round(all.NonPureFraction, 4) +
                "), so the capture cannot be trusted. The screen may be locked, the display off, or colour handling is altering pixels.";
            return;
        }

        var methods = new JsonObject();
        root["methods"] = methods;
        var results = new List<MethodResult>();
        foreach (var (name, host, attrFirst) in new[] { ("host_backdrop_brush", true, true), ("plain_backdrop_brush", false, true), ("host_backdrop_brush_attribute_set_after_show", true, false) })
        {
            if (Clock.Elapsed.TotalSeconds > WatchdogSeconds) { methods[name] = new JsonObject { ["skipped"] = "watchdog" }; continue; }
            var r = RunMethod(name, host, attrFirst, control.Image, cx + px0, cy + py0, pw, ph, px0, py0);
            methods[name] = r.Json;
            results.Add(r);
        }

        // verdict: best method wins
        var best = results.OrderBy(r => r.Rank).ThenBy(r => r.Failed.Count).FirstOrDefault();
        if (best == null) { detail = "NOT MEASURED: no method could be run"; return; }
        foreach (var f in best.Failed) failed.Add(f);
        verdict = best.Verdict;
        methodThatDid = best.Verdict == "YES" ? best.Name : null;
        detail = best.Verdict switch
        {
            "YES" => $"{best.Name}: blurred inside the shape, sharp outside it, live after the stripes moved, with the pattern window (not the probe) in front, and the ball confined.",
            "PARTLY" => $"best method {best.Name} failed: {string.Join(", ", best.Failed)}.",
            _ => $"no method produced a blur inside the shape (best: {best.Name}; {best.Note})."
        };

        // enlarged crop of the curved end, from the best method's capsule picture
        if (best.CapsulePicture != null)
        {
            int right = px0 + (pw + CapsuleW) / 2, top = py0 + (ph - CapsuleH) / 2;
            Save("edge-4x.png", best.CapsulePicture, right - 48, top - 10, 64, CapsuleH + 20, 4);
            root["edge_crop"] = new JsonObject { ["from_method"] = best.Name, ["crop_px"] = $"64x{CapsuleH + 20}", ["scale"] = 4, ["filter"] = "nearest neighbour (no smoothing, so single pixels can be judged)" };
        }
        root["morph_animation_tested"] = false;
        root["morph_note"] = "the shape was switched between capsule and ball by setting the clip geometry, not animated";
    }

    sealed record MethodResult(string Name, JsonObject Json, string Verdict, int Rank, List<string> Failed, string Note, Pixels? CapsulePicture);

    static MethodResult RunMethod(string name, bool host, bool attrFirst, Pixels control, int screenX, int screenY, int pw, int ph, int px0, int py0)
    {
        var m = new JsonObject();
        var failed = new List<string>();
        Win.ProbeActivations = 0; Win.ProbeFocusMessages = 0;
        Win.SetShift(0); Win.Pump(100);
        var hwnd = Win.CreateProbe(screenX, screenY, pw, ph);
        Glass? glass = null;
        Pixels? capsulePicture = null;
        try
        {
            try { glass = new Glass(hwnd, pw, ph, host, SigmaPx, attrFirst); m["tree_created"] = true; }
            catch (Exception e)
            {
                m["tree_created"] = false; m["error"] = Clean(e.GetType().Name + ": " + e.Message);
                failed.AddRange(new[] { "blurred_inside", "sharp_outside", "live", "held_while_pattern_focused", "ball_confined" });
                return new(name, m, "NO", 2, failed, "could not build the composition tree: " + (string)m["error"]!, null);
            }
            var capsuleLocal = new Shape((pw - CapsuleW) / 2.0, (ph - CapsuleH) / 2.0, CapsuleW, CapsuleH, CapsuleR);
            var ballLocal = new Shape((pw - BallD) / 2.0, (ph - BallD) / 2.0, BallD, BallD, BallD / 2.0);
            Shape ToClient(Shape s) => s with { X = s.X + px0, Y = s.Y + py0 };
            bool InProbe(int x, int y) => x >= px0 + 2 && x < px0 + pw - 2 && y >= py0 + 2 && y < py0 + ph - 2;
            Func<int, int, bool> Inside(Shape s, int margin) => (x, y) => InProbe(x, y) && s.Sd(x + 0.5, y + 0.5) <= -margin;
            Func<int, int, bool> Outside(Shape s) => (x, y) => InProbe(x, y) && s.Sd(x + 0.5, y + 0.5) >= OutsideMargin;

            glass.SetShape(capsuleLocal);
            Win.ShowProbe();
            if (host && !attrFirst) glass.ApplyHostAttribute();
            Win.Pump(500);
            Win.EnsurePatternForeground();

            // ---- capsule, stripes at rest ----
            var a = CaptureStable(5000);
            m["capsule_capture_taken"] = a.Image != null; m["capsule_capture_reason"] = a.Reason; m["capsule_capture_settle_tries"] = a.Tries;
            if (a.Image == null) { failed.AddRange(new[] { "blurred_inside", "sharp_outside", "live", "held_while_pattern_focused", "ball_confined" }); return new(name, m, "NO", 2, failed, "could not photograph: " + a.Reason, null); }
            capsulePicture = a.Image;
            var cap = ToClient(capsuleLocal);
            var inA = Imaging.Measure(a.Image, Inside(cap, InsideMarginCapsule));
            var outA = Imaging.Measure(a.Image, Outside(cap));
            string insideClass = ClassifyInside(inA);
            bool blurredInside = insideClass == "blurred";
            bool sharpOutside = outA.NonPureFraction <= SharpMaxNonPure && outA.Min <= 8 && outA.Max >= 247;
            m["capsule"] = new JsonObject { ["inside"] = Stats(inA), ["inside_class"] = insideClass, ["blurred_inside"] = blurredInside, ["outside"] = Stats(outA), ["sharp_outside"] = sharpOutside,
                ["inside_mean_abs_diff_vs_bare_pattern"] = Math.Round(Imaging.MeanAbsDiff(control, a.Image, Inside(cap, InsideMarginCapsule)), 2) };

            // ---- live: move the stripes by half a stripe ----
            Win.SetShift(StripePx / 2);
            Win.Pump(300);
            var b = CaptureStable(5000);
            bool live = false; bool tracks = false;
            var liveJson = new JsonObject { ["capture_taken"] = b.Image != null, ["capture_reason"] = b.Reason };
            if (b.Image != null)
            {
                var insideMask = Inside(cap, InsideMarginCapsule);
                double diffIn = Imaging.MeanAbsDiff(a.Image, b.Image, insideMask);
                double diffOut = Imaging.MeanAbsDiff(a.Image, b.Image, Outside(cap));
                double rms0 = Imaging.ShiftRms(a.Image, b.Image, insideMask, 0);
                double rmsPlus = Imaging.ShiftRms(a.Image, b.Image, insideMask, StripePx / 2);
                double rmsMinus = Imaging.ShiftRms(a.Image, b.Image, insideMask, -StripePx / 2);
                bool changed = diffIn >= Math.Max(4, 0.15 * (inA.Max - inA.Min));
                tracks = rmsPlus <= 0.5 * rms0;
                live = changed && tracks && blurredInside;
                liveJson["inside_mean_abs_diff"] = Math.Round(diffIn, 2);
                liveJson["outside_mean_abs_diff_sanity"] = Math.Round(diffOut, 2);
                liveJson["inside_changed"] = changed;
                liveJson["rms_if_not_moved"] = Math.Round(rms0, 2);
                liveJson["rms_if_moved_left_by_half_stripe"] = Math.Round(rmsPlus, 2);
                liveJson["rms_if_moved_right_by_half_stripe"] = Math.Round(rmsMinus, 2);
                liveJson["inside_tracks_the_pattern"] = tracks;
                liveJson["inside_after_shift"] = Stats(Imaging.Measure(b.Image, insideMask));
                liveJson["outside_after_shift"] = Stats(Imaging.Measure(b.Image, Outside(cap)));
                Save($"{name}-capsule-a.png", a.Image); Save($"{name}-capsule-b-shifted.png", b.Image);
            }
            liveJson["live"] = live;
            m["live"] = liveJson;

            // ---- ball ----
            glass.SetShape(ballLocal);
            Win.Pump(300);
            var c = CaptureStable(5000);
            bool ballConfined = false;
            var ballJson = new JsonObject { ["capture_taken"] = c.Image != null, ["capture_reason"] = c.Reason };
            if (c.Image != null)
            {
                var ball = ToClient(ballLocal);
                var inC = Imaging.Measure(c.Image, Inside(ball, InsideMarginBall));
                var outC = Imaging.Measure(c.Image, Outside(ball));
                bool ballBlurred = ClassifyInside(inC) == "blurred";
                bool outSharp = outC.NonPureFraction <= SharpMaxNonPure && outC.Min <= 8 && outC.Max >= 247;
                ballConfined = ballBlurred && outSharp;
                ballJson["inside"] = Stats(inC); ballJson["inside_class"] = ClassifyInside(inC); ballJson["blurred_inside"] = ballBlurred;
                ballJson["outside"] = Stats(outC); ballJson["sharp_outside"] = outSharp;
                Save($"{name}-ball.png", c.Image);
            }
            ballJson["confined"] = ballConfined;
            m["ball"] = ballJson;

            // ---- focus: every picture above was only taken with the pattern window in front and focused; the probe must never have been activated ----
            bool allTaken = a.Image != null && b.Image != null && c.Image != null;
            bool held = allTaken && Win.ProbeActivations == 0 && Win.ProbeFocusMessages == 0;
            m["focus"] = new JsonObject
            {
                ["pattern_in_front_and_focused_at_every_capture"] = allTaken,   // CapturePatternRect refuses otherwise
                ["probe_activation_messages"] = Win.ProbeActivations, ["probe_focus_messages"] = Win.ProbeFocusMessages,
                ["held_while_pattern_focused"] = held
            };

            // ---- verdict for this method ----
            if (!blurredInside) failed.Add("blurred_inside");
            if (!sharpOutside) failed.Add("sharp_outside");
            if (!live) failed.Add("live");
            if (!held) failed.Add("held_while_pattern_focused");
            if (!ballConfined) failed.Add("ball_confined");
            string verdict = failed.Count == 0 ? "YES" : (!blurredInside ? "NO" : "PARTLY");
            m["failed_criteria"] = new JsonArray(failed.Select(f => (JsonNode)f).ToArray());
            m["verdict"] = verdict;
            return new(name, m, verdict, verdict == "YES" ? 0 : verdict == "PARTLY" ? 1 : 2, failed, $"inside looked {insideClass}", capsulePicture);
        }
        finally
        {
            glass?.Dispose();
            Win.DestroyProbe();
            Win.SetShift(0);
            Win.Pump(200);
        }
    }

    internal static string ClassifyInside(RegionStats s)
    {
        if (s.Count == 0) return "no pixels";
        int range = s.Max - s.Min;
        if (range < FlatRange) return "flat";                    // no stripes visible: black, grey or a solid tint
        if (s.PureFraction > 0.5) return "unblurred";            // the stripes pass through untouched
        if (s.PureFraction <= BlurredMaxPure) return "blurred";
        return "partial";
    }

    internal static JsonObject Stats(RegionStats s) => new()
    {
        ["pixels"] = s.Count, ["gray_min"] = s.Min, ["gray_max"] = s.Max, ["gray_mean"] = Math.Round(s.Mean, 1),
        ["pure_fraction"] = Math.Round(s.PureFraction, 4), ["nonpure_fraction"] = Math.Round(s.NonPureFraction, 4), ["max_colour_spread"] = s.MaxSpread
    };

    /// <summary>Photographs the pattern rectangle until two pictures in a row are identical (the compositor has finished), or time runs out.</summary>
    internal static Snap CaptureStable(int maxMs)
    {
        var end = Environment.TickCount64 + maxMs;
        Pixels? prev = null; string reason = "no attempt"; int tries = 0;
        while (Environment.TickCount64 < end)
        {
            Win.Pump(150);
            if (!Win.PatternIsForeground() || !Win.PatternHasFocus()) Win.EnsurePatternForeground();
            var r = Win.CapturePatternRect();
            tries++;
            if (!r.Taken) { reason = r.Reason; prev = null; continue; }
            if (prev != null && prev.Data.AsSpan().SequenceEqual(r.Image!.Data)) return new(r.Image, "stable", tries);
            prev = r.Image; reason = "never settled (the picture kept changing)";
        }
        // a picture that never settled is still a taken, validated picture; the caller decides what it is worth
        return prev != null ? new(prev, reason, tries) : new(null, reason, tries);
    }

    internal static void Save(string file, Pixels p, int cx = 0, int cy = 0, int cw = -1, int ch = -1, int scale = 1)
    {
        Imaging.SavePng(Path.Combine(outDir, file), p, cx, cy, cw, ch, scale);
        Saved.Add(file);
    }
}
