using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Island.Core;
using Island.Glass;

namespace BlurProbe;

/// <summary>
/// Glass mode, part 3: what the layer costs. Follow() is called once per compositor frame (DwmFlush paces the loop), from the
/// island's spring, as the island's frame callback would; every call is timed and its allocations counted.
/// </summary>
internal static partial class GlassMode
{
    static JsonObject Timing(GlassClickThrough mode, int ax, int ay)
    {
        var j = new JsonObject { ["click_through_mode"] = mode.ToString() };
        Win.CreateAnchor(ax, ay, AnchorW, AnchorH);
        using (var layer = new GlassLayer(Win.Anchor, mode))
        {
            if (!layer.IsAvailable) { j["error"] = layer.UnavailableReason; Win.DestroyAnchor(); return j; }
            Capsule.FollowOn(layer);
            j["hidden_follow_every_frame"] = Loop(layer, CpuStillSeconds, moving: true);
            var t = Stopwatch.StartNew();
            layer.Show();
            j["show_call_us"] = Math.Round(t.Elapsed.TotalMicroseconds, 1);
            j["shown_still"] = Loop(layer, CpuStillSeconds, moving: false);
            j["shown_moving"] = Loop(layer, CpuMovingSeconds, moving: true);
            t.Restart();
            layer.Hide();
            j["hide_call_us"] = Math.Round(t.Elapsed.TotalMicroseconds, 1);
        }
        Win.DestroyAnchor();
        j["note"] = "process CPU covers this whole probe (its frame loop and the spring maths included); dwm CPU is the system compositor's whole process, everything else on screen included, so compare it between the three runs";
        return j;
    }

    static JsonObject Loop(GlassLayer layer, double seconds, bool moving)
    {
        double centre = AnchorW / Scale / 2;
        var w = Spring.At(CapsuleW); var h = Spring.At(CapsuleH); var r = Spring.At(CapsuleR);
        var calls = new List<double>(4096);
        long allocated = 0;
        bool toBall = true;
        double nextSwitch = 0.8;

        var me = Process.GetCurrentProcess();
        var cpu0 = me.TotalProcessorTime;
        var dwm0 = DwmCpu();
        var wall = Stopwatch.StartNew();
        long last = Stopwatch.GetTimestamp();
        while (wall.Elapsed.TotalSeconds < seconds)
        {
            Win.DwmFlush();
            long now = Stopwatch.GetTimestamp();
            double dt = (now - last) / (double)Stopwatch.Frequency;
            last = now;
            if (moving && wall.Elapsed.TotalSeconds >= nextSwitch)
            {
                w = w.WithTarget(toBall ? BallD : CapsuleW);
                h = h.WithTarget(toBall ? BallD : CapsuleH);
                r = r.WithTarget(toBall ? BallD / 2 : CapsuleR);
                toBall = !toBall;
                nextSwitch += 0.8;
            }
            w = w.Frame(dt); h = h.Frame(dt); r = r.Frame(dt);
            double width = w.Drawn, height = h.Drawn, radius = Math.Min(r.Drawn, Math.Min(width, height) / 2);

            long b0 = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            layer.Follow(centre - width / 2, TopDip, width, height, radius);
            long t1 = Stopwatch.GetTimestamp();
            allocated += GC.GetAllocatedBytesForCurrentThread() - b0;
            calls.Add((t1 - t0) * 1e6 / Stopwatch.Frequency);
            Win.Pump(0);
        }
        double wallMs = wall.Elapsed.TotalMilliseconds;
        me.Refresh();
        double cpuMs = (me.TotalProcessorTime - cpu0).TotalMilliseconds;
        var dwm1 = DwmCpu();

        calls.Sort();
        return new JsonObject
        {
            ["seconds"] = Math.Round(wallMs / 1000, 2),
            ["frames"] = calls.Count,
            ["frames_per_second"] = Math.Round(calls.Count * 1000 / wallMs, 1),
            ["follow_us_median"] = Math.Round(calls[calls.Count / 2], 2),
            ["follow_us_p99"] = Math.Round(calls[(int)(calls.Count * 0.99)], 2),
            ["follow_us_max"] = Math.Round(calls[^1], 2),
            ["follow_bytes_allocated_total"] = allocated,
            ["process_cpu_percent_of_one_core"] = Math.Round(cpuMs / wallMs * 100, 2),
            ["dwm_cpu_percent_of_one_core"] = dwm0 is { } a && dwm1 is { } b ? Math.Round((b - a).TotalMilliseconds / wallMs * 100, 2) : null,
        };
    }

    /// <summary>CPU time of the system compositor (dwm), when Windows lets this process read it; null otherwise.</summary>
    static TimeSpan? DwmCpu()
    {
        try
        {
            var dwm = Process.GetProcessesByName("dwm");
            try { return dwm.Length == 1 ? dwm[0].TotalProcessorTime : null; }
            finally { foreach (var p in dwm) p.Dispose(); }
        }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
