using System.Text.Json.Nodes;
using Island.Glass;

namespace BlurProbe;

/// <summary>
/// Glass mode, part 1: can a click reach ANOTHER program through the glass layer? A second copy of this probe puts a plain
/// window under the layer; WindowFromPoint is asked, from both processes, at a point inside the blurred shape, a point in the
/// layer's window outside the shape, and two corners. Every answer must be the other program's window. No click is made
/// (no synthetic input is allowed), so whether a real mouse click behaves the same is NEEDS-HUMAN-VERIFY.
/// </summary>
internal static partial class GlassMode
{
    internal sealed record ClickResult(JsonObject Json, List<GlassClickThrough> Passing);

    static readonly GlassClickThrough[] Modes = { GlassClickThrough.LayeredTransparent, GlassClickThrough.Transparent, GlassClickThrough.HitTestOnly };

    static ClickResult ClickChecks(int ax, int ay)
    {
        var json = new JsonObject();
        var passing = new List<GlassClickThrough>();
        const int Margin = 40;
        using var target = ClickTargetProcess.Start(ax - Margin, ay - Margin, AnchorW + 2 * Margin, AnchorH + 2 * Margin);
        if (target == null) { json["error"] = "the click target (a second copy of this probe) could not be started"; return new(json, passing); }
        Win.Pump(300);

        double s = Scale;
        var capsule = Capsule;
        var ball = BallAt(AnchorW / s / 2);
        int midY = (int)((TopDip + CapsuleH / 2) * s);
        var points = new (string Name, Dip Shape, int X, int Y)[]
        {
            ("inside_capsule", capsule, AnchorW / 2, midY),
            ("window_outside_capsule", capsule, 20, AnchorH - 20),
            ("corner_top_left", capsule, 0, 0),
            ("corner_bottom_right", capsule, AnchorW - 1, AnchorH - 1),
            ("inside_ball", ball, AnchorW / 2, (int)((TopDip + BallD / 2) * s)),
            ("window_outside_ball", ball, AnchorW / 2 + 60, midY),
        };

        foreach (var mode in Modes)
        {
            var m = new JsonObject();
            json[mode.ToString()] = m;
            Win.CreateAnchor(ax, ay, AnchorW, AnchorH);
            using (var layer = new GlassLayer(Win.Anchor, mode))
            {
                m["available"] = layer.IsAvailable;
                m["reason"] = layer.UnavailableReason;
                if (!layer.IsAvailable) { Win.DestroyAnchor(); continue; }
                bool all = true;
                var answers = new JsonObject();
                foreach (var p in points)
                {
                    p.Shape.FollowOn(layer);
                    layer.Show();
                    DwmFrames(3);
                    int x = ax + p.X, y = ay + p.Y;
                    var here = Category(Win.RootAt(x, y), target, layer);
                    var there = target.At(x, y) is { } a ? Category(a, target, layer) : "no_answer";
                    answers[p.Name] = new JsonObject { ["asked_from_this_probe"] = here, ["asked_from_the_other_program"] = there };
                    all &= here == "other_program" && there == "other_program";
                }
                m["window_from_point"] = answers;
                m["clicks_reach_other_program"] = all;
                if (all) passing.Add(mode);
            }
            Win.DestroyAnchor();
            Win.Pump(100);
        }
        json["note"] = "categories only; window handles are compared in memory. Points are in the stand-in island window, which is itself layered and click-through";
        return new(json, passing);
    }

    static string Category((nint Root, int Pid) at, ClickTargetProcess target, GlassLayer layer) =>
        at.Root == target.Hwnd && at.Pid == target.Pid ? "other_program"
        : at.Root == layer.WindowHandle ? "glass_layer"
        : at.Root == Win.Anchor ? "island_stand_in"
        : at.Root == Win.Pattern && at.Root != 0 ? "pattern_window"
        : "some_other_window";
}
