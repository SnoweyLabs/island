using System.Diagnostics;
using System.Globalization;
using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-3 section 8 checks in the island itself: the blur layer is created under the capsule, shown with the
/// island and hidden with it, follows without taking the foreground, the summons take the same time with Blur on
/// (EVALS M4, late frames set NEEDS-HUMAN-VERIFY as in M6), and the processor time while open is measured for each glass
/// and appended to review/perf.md. The pictures of what the blur looks like are in review/glass (the layer on its own,
/// measured by the blur probe in a process of its own: a click check made from inside this process cannot prove that
/// clicks pass through, so it is not made here). Only counts and numbers are recorded.
/// </summary>
internal sealed class BlurStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    private const int Summons = 5;

    public async Task RunAsync()
    {
        // BLUR_UNAVAILABLE: when Blur is chosen and cannot be had (an island whose window was never shown has no layer) the Approved glass is used and the reason is said once, however often Blur is chosen.
        using (var noLayer = new IslandRuntime())
        {
            var said = new List<string>();
            noLayer.BlurFellBack += said.Add;
            noLayer.SetGlass(GlassKind.Blur);
            noLayer.SetGlass(GlassKind.Blur);
            noLayer.SetGlass(GlassKind.Darker);
            noLayer.SetGlass(GlassKind.Blur);
            report.Check("when Blur is chosen and cannot be had the Approved glass is used and the reason is said once, however often Blur is chosen",
                said.Count == 1 && !string.IsNullOrEmpty(said[0]) && noLayer.Glass == GlassKind.Approved, $"said {said.Count} time(s): {string.Join(", ", said)}; glass in use {noLayer.Glass}");
        }

        using var rt = new IslandRuntime();
        rt.Show();
        await Task.Delay(200);
        var available = rt.BlurAvailable;
        report.Info["blurAvailable"] = available;
        report.Info["blurUnavailableReason"] = rt.BlurUnavailableReason ?? "none";
        report.Check("the blur layer was created or its reason for being unavailable is a short code", available || !string.IsNullOrEmpty(rt.BlurUnavailableReason), available ? "available" : rt.BlurUnavailableReason ?? "?");
        if (!available) return;

        var c = rt.Controller;
        var m = c.Machine;
        var layer = (Island.Glass.GlassLayer)rt.BlurLayer!;
        var before = Native.GetForegroundWindow();

        var approvedCpu = await OpenCpuAsync(rt, GlassKind.Approved);
        rt.SetGlass(GlassKind.Blur);
        c.PageKey(PageIds.Media);
        await Waiter.UntilAsync(() => m.IsAtRest, "the capsule open and at rest with the Blur glass", hangLimit, report);
        report.Check("with Blur chosen the layer is shown while the island is open", layer.IsShown && rt.Glass == GlassKind.Blur, $"shown {layer.IsShown}");
        report.Check("the blur layer did not take the foreground", Native.GetForegroundWindow() == before, "same window in front as before");

        var blurCpu = await OpenCpuAsync(rt, GlassKind.Blur);
        report.Info["cpuOpenPercentOfOneCore"] = new { approved = Math.Round(approvedCpu, 1), blur = Math.Round(blurCpu, 1) };

        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden with the Blur glass", hangLimit, report);
        report.Check("the blur layer is hidden when the island is", !layer.IsShown, "no layer on the screen while hidden");

        await RepeatedSummonsAsync(rt);
        AppendPerf(approvedCpu, blurCpu);

        rt.SetGlass(GlassKind.Approved);
        report.Check("switching back to the approved glass takes the layer away", rt.Glass == GlassKind.Approved && !layer.IsShown, "the layer is hidden and no longer followed");
    }

    /// <summary>Processor time of this process while the capsule sits open with the light moving, as a percentage of one core.</summary>
    private static async Task<double> OpenCpuAsync(IslandRuntime rt, GlassKind glass)
    {
        rt.SetGlass(glass);
        var c = rt.Controller;
        c.PageKey(PageIds.Media);
        await Task.Delay(1200);
        var process = Process.GetCurrentProcess();
        var cpu0 = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        await Task.Delay(2500);
        process.Refresh();
        var used = process.TotalProcessorTime - cpu0;
        var percent = 100 * used.TotalMilliseconds / wall.Elapsed.TotalMilliseconds;
        c.Activity();
        return percent;
    }

    private async Task RepeatedSummonsAsync(IslandRuntime rt)
    {
        var c = rt.Controller;
        var m = c.Machine;
        var intervals = new List<double>();
        c.FrameIntervals = intervals;
        var durations = new List<double>();
        for (var i = 0; i < Summons; i++)
        {
            c.PageKey(PageIds.Media);
            if (await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), $"Blur summon {i + 1}: capsule open and at rest", hangLimit, report)) durations.Add(c.RestDurationMs);
            await Task.Delay(250);
            c.ShowHide();
            await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, $"Blur summon {i + 1}: the island hidden again", hangLimit, report);
            await Task.Delay(200);
        }

        c.FrameIntervals = null;
        var sorted = intervals.OrderBy(x => x).ToList();
        var refresh = sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
        var late = sorted.Count(x => x > 2 * refresh);
        var spread = durations.Count == 0 ? 0 : durations.Max() - durations.Min();
        report.Info["blurSummons"] = new { durationsMs = durations.Select(d => Math.Round(d, 1)).ToList(), spreadMs = Math.Round(spread, 2), refreshMs = Math.Round(refresh, 2), lateFrames = late, frames = sorted.Count };
        report.Check("every summon with the Blur glass takes the same time to within one refresh interval (EVALS M4)", durations.Count == Summons && spread <= refresh + 0.01, $"spread {spread:0.##} ms, refresh interval {refresh:0.##} ms");
        if (late > 0) report.NeedsHumanVerify.Add($"With the Blur glass {late} of {sorted.Count} frames during the repeated summons came later than twice the refresh interval ({refresh.ToString("0.0", CultureInfo.InvariantCulture)} ms); watch ten summons with Blur: is it smooth, and does the blur follow the capsule without lag?");
    }

    private void AppendPerf(double approved, double blur)
    {
        var path = Path.Combine(folder, "perf.md");
        var text = $"""

            ## Glasses — processor time with the capsule open and the light moving (WO3 section 8)

            Measured by the self-test over 2.5 s, this process only, as a percentage of one core; the compositor's own cost (dwm, the GPU) is not included and was not measured. One run, one laptop, while other programs were working: an anecdote, not a gate.

            | Glass | Process CPU while open, % of one core |
            |---|---|
            | Approved | {approved.ToString("0.0", CultureInfo.InvariantCulture)} |
            | Blur | {blur.ToString("0.0", CultureInfo.InvariantCulture)} |
            """;
        try
        {
            File.AppendAllText(path, text);
        }
        catch (IOException)
        {
            report.Info["perfAppend"] = "could not append to perf.md";
        }
    }
}
