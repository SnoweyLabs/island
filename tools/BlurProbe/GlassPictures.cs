using System.Diagnostics;
using System.Text.Json.Nodes;
using Island.Core;
using Island.Glass;

namespace BlurProbe;

/// <summary>
/// Glass mode, part 2: the photographs, under exactly the first probe's rules (Win.CapturePatternRect: only the pattern
/// window's client rectangle, only while it is the foreground window with focus and nothing else overlaps it).
/// </summary>
internal static partial class GlassMode
{
    internal sealed record PictureResult(GlassClickThrough? Chosen);

    sealed record Area(int Ax, int Ay, double S)
    {
        bool InAnchor(int x, int y) => x >= Ax + 2 && x < Ax + AnchorW - 2 && y >= Ay + 2 && y < Ay + AnchorH - 2;

        public Shape Client(Dip d) => d.ToClient(Ax, Ay, S);

        public Func<int, int, bool> Inside(Dip d, int margin) { var c = Client(d); return (x, y) => InAnchor(x, y) && c.Sd(x + 0.5, y + 0.5) <= -margin; }

        public Func<int, int, bool> Outside(Dip d) { var c = Client(d); return (x, y) => InAnchor(x, y) && c.Sd(x + 0.5, y + 0.5) >= OutsideMargin; }
    }

    static PictureResult Pictures(JsonObject root, Verdict verdict, ClickResult clicks, int ax, int ay, int cx, int cy, int cw, int ch, bool front)
    {
        var pics = new JsonObject();
        root["pictures_check"] = pics;
        if (!front) { verdict.NotMeasured("Windows did not give the pattern window the foreground, so nothing was photographed"); return new(null); }

        var control = Program.CaptureStable(5000);
        pics["control_taken"] = control.Image != null;
        pics["control_reason"] = control.Reason;
        if (control.Image == null) { verdict.NotMeasured("the pattern window could not be photographed: " + control.Reason); return new(null); }
        int inset = Win.CaptureInsetPx;
        var all = Imaging.Measure(control.Image, (x, y) => x >= inset && x < cw - inset && y >= inset && y < ch - inset);
        bool exact = all.NonPureFraction <= Program.SharpMaxNonPure && all.Min <= 8 && all.Max >= 247;
        pics["control_capture_exact"] = exact;
        Program.Save("control-pattern.png", control.Image);
        if (!exact) { verdict.NotMeasured("the photograph of the bare stripes is not pure black and white"); return new(null); }

        var area = new Area(ax - cx, ay - cy, Scale);
        GlassClickThrough? chosen = null;
        bool judgedAPassingMode = false;   // a photograph was taken of a mode whose clicks pass
        foreach (var mode in Modes)
        {
            if (Clock.Elapsed.TotalSeconds > WatchdogSeconds) break;
            var m = new JsonObject();
            pics[mode.ToString()] = m;
            Win.CreateAnchor(ax, ay, AnchorW, AnchorH);
            using (var layer = new GlassLayer(Win.Anchor, mode))
            {
                m["available"] = layer.IsAvailable;
                if (layer.IsAvailable)
                {
                    Win.Probe = layer.WindowHandle;
                    Capsule.FollowOn(layer);
                    layer.Show();
                    Win.EnsurePatternForeground();
                    var a = Program.CaptureStable(5000);
                    var (ok, json) = Judge(a, area, Capsule, InsideMarginCapsule);
                    m["capsule"] = json;
                    if (a.Image != null) Program.Save($"{mode}-capsule.png", a.Image);
                    if (a.Image == null) verdict.NotMeasured($"{mode} capsule: " + a.Reason);
                    else if (clicks.Passing.Contains(mode)) judgedAPassingMode = true;
                    if (ok && clicks.Passing.Contains(mode) && chosen == null)
                    {
                        chosen = mode;
                        FullSet(pics, verdict, layer, area, a.Image!);
                    }
                    Win.Probe = 0;
                }
            }
            Win.DestroyAnchor();
            Win.Pump(150);
        }
        if (chosen == null && judgedAPassingMode) verdict.Fail("no_mode_both_blurs_and_lets_clicks_through");
        return new(chosen);
    }

    /// <summary>Blurred inside (away from the edge) and pure black and white outside, in the layer's window.</summary>
    static (bool Ok, JsonObject Json) Judge(Program.Snap snap, Area area, Dip shape, int margin)
    {
        var j = new JsonObject { ["capture_taken"] = snap.Image != null, ["capture_reason"] = snap.Reason, ["settle_tries"] = snap.Tries };
        if (snap.Image == null) return (false, j);
        var inside = Imaging.Measure(snap.Image, area.Inside(shape, margin));
        var outside = Imaging.Measure(snap.Image, area.Outside(shape));
        string cls = Program.ClassifyInside(inside);
        bool sharp = outside.NonPureFraction <= Program.SharpMaxNonPure && outside.Min <= 8 && outside.Max >= 247;
        j["inside"] = Program.Stats(inside);
        j["inside_class"] = cls;
        j["blurred_inside"] = cls == "blurred";
        j["outside"] = Program.Stats(outside);
        j["sharp_outside"] = sharp;
        return (cls == "blurred" && sharp, j);
    }

    static void FullSet(JsonObject pics, Verdict verdict, GlassLayer layer, Area area, Pixels capsuleImage)
    {
        double centre = AnchorW / area.S / 2;
        pics["capsule_blurred_inside_sharp_outside"] = true;   // FullSet is only reached when it is

        // ---- live: the stripes move by half a stripe; the blurred picture inside must move with them ----
        Win.SetShift(StripePx / 2);
        var b = Program.CaptureStable(5000);
        var live = new JsonObject { ["capture_taken"] = b.Image != null };
        if (b.Image != null)
        {
            var mask = area.Inside(Capsule, InsideMarginCapsule);
            var inA = Imaging.Measure(capsuleImage, mask);
            double diffIn = Imaging.MeanAbsDiff(capsuleImage, b.Image, mask);
            double rms0 = Imaging.ShiftRms(capsuleImage, b.Image, mask, 0), rmsPlus = Imaging.ShiftRms(capsuleImage, b.Image, mask, StripePx / 2);
            bool isLive = diffIn >= Math.Max(4, 0.15 * (inA.Max - inA.Min)) && rmsPlus <= 0.5 * rms0;
            live["inside_mean_abs_diff"] = Math.Round(diffIn, 2);
            live["rms_if_not_moved"] = Math.Round(rms0, 2);
            live["rms_if_moved_left_by_half_stripe"] = Math.Round(rmsPlus, 2);
            live["live"] = isLive;
            if (!isLive) verdict.Fail("live");
            Program.Save("glass-capsule-shifted.png", b.Image);
        }
        else verdict.NotMeasured("live: " + b.Reason);
        pics["live"] = live;
        Win.SetShift(0);
        Program.Save("glass-capsule.png", capsuleImage);
        int right = (int)area.Client(Capsule).X + (int)(CapsuleW * area.S), top = (int)area.Client(Capsule).Y;
        Program.Save("glass-edge-4x.png", capsuleImage, right - 48, top - 10, 64, (int)(CapsuleH * area.S) + 20, 4);

        // ---- the ball, centred on a black/white edge so that its few pixels can show a blur at all ----
        int edge = (int)Math.Round((area.Ax + AnchorW / 2.0) / StripePx) * StripePx;
        var ball = BallAt((edge - area.Ax) / area.S);
        ball.FollowOn(layer);
        var c = Program.CaptureStable(5000);
        var (ballOk, ballJson) = Judge(c, area, ball, InsideMarginBall);
        pics["ball"] = ballJson;
        if (c.Image != null) Program.Save("glass-ball.png", c.Image);
        if (c.Image == null) verdict.NotMeasured("ball: " + c.Reason);
        else if (!ballOk) verdict.Fail("ball_blurred_inside_sharp_outside");

        // ---- one still moment in the middle of the expansion, driven by the island's own spring ----
        BallAt(centre).FollowOn(layer);
        DwmFrames(3);
        var w = Spring.At(BallD).WithTarget(CapsuleW);
        var h = Spring.At(BallD).WithTarget(CapsuleH);
        var r = Spring.At(BallD / 2).WithTarget(CapsuleR);
        Dip Now() => new(centre - w.Drawn / 2, TopDip, w.Drawn, h.Drawn, Math.Min(r.Drawn, Math.Min(w.Drawn, h.Drawn) / 2));
        long last = Stopwatch.GetTimestamp();
        int frames = 0;
        while (w.Drawn < (BallD + CapsuleW) / 2 && frames < 600)
        {
            Win.DwmFlush();
            long now = Stopwatch.GetTimestamp();
            double dt = (now - last) / (double)Stopwatch.Frequency;
            last = now;
            w = w.Frame(dt); h = h.Frame(dt); r = r.Frame(dt);
            Now().FollowOn(layer);
            Win.Pump(0);   // the compositor commits a batch when the thread pumps messages, as WPF's dispatcher does after each frame
            frames++;
        }
        var frozen = Now();   // the island's clock stops here: no further Follow until the picture is taken
        var d = Program.CaptureStable(5000);
        var (midOk, midJson) = Judge(d, area, frozen, InsideMarginBall + 4);
        midJson["frozen_shape_dip"] = $"{frozen.W:0.0}x{frozen.H:0.0} radius {frozen.R:0.0}";
        midJson["frames_driven_before_freeze"] = frames;
        pics["middle_of_expansion"] = midJson;
        if (d.Image != null) Program.Save("glass-mid-expansion.png", d.Image);
        if (d.Image == null) verdict.NotMeasured("middle of expansion: " + d.Reason);
        else if (!midOk) verdict.Fail("middle_of_expansion_blurred_inside_sharp_outside");

        pics["lag"] = Lag(layer, area, centre);
    }

    /// <summary>
    /// How long after Follow() the new shape is on the screen: from the ball, Follow(capsule), then photograph after every
    /// compositor frame until a patch inside the capsule but far from the ball shows blur.
    /// </summary>
    static JsonObject Lag(GlassLayer layer, Area area, double centre)
    {
        BallAt(centre).FollowOn(layer);
        Program.CaptureStable(3000);
        var cap = area.Client(Capsule);
        int px = (int)cap.X + 24, py = (int)(cap.Y + cap.H / 2) - 8;
        Func<int, int, bool> patch = (x, y) => x >= px && x < px + 16 && y >= py && y < py + 16;
        var before = Win.CapturePatternRect();
        double patchBefore = before.Taken ? Imaging.Measure(before.Image!, patch).NonPureFraction : -1;
        var sw = Stopwatch.StartNew();
        Capsule.FollowOn(layer);
        Win.Pump(0);   // the change is committed here, when the thread pumps (WPF's dispatcher does this after every frame callback)
        int taken = 0;
        double lastPatch = -1;
        string lastReason = "";
        for (int i = 1; i <= 40; i++)
        {
            Win.DwmFlush();
            Win.Pump(0);
            var shot = Win.CapturePatternRect();
            if (!shot.Taken) { lastReason = shot.Reason; continue; }
            taken++;
            lastPatch = Imaging.Measure(shot.Image!, patch).NonPureFraction;
            if (lastPatch > 0.9)
                return new JsonObject { ["seen_after_ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1), ["compositor_frames_waited"] = i,
                    ["patch_nonpure_before"] = Math.Round(patchBefore, 3),
                    ["note"] = "upper bound: each photograph itself takes time, so a frame can be skipped between looks" };
        }
        return new JsonObject { ["seen_after_ms"] = null, ["photographs_taken"] = taken, ["last_refusal"] = lastReason,
            ["patch_nonpure_before"] = Math.Round(patchBefore, 3), ["patch_nonpure_last"] = Math.Round(lastPatch, 3), ["note"] = "not seen within 40 compositor frames" };
    }
}
