using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Island.Core;

namespace Island.App;

/// <summary>
/// Section 8 evidence: memory with the island open and hidden, and processor time consumed while hidden
/// and while open with the light moving, each over more than one full lap of the light. These are
/// records, not gates; they are written to perf.md next to selftest.json.
/// </summary>
internal sealed class PerfStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    /// <summary>One lap of the light is 1 / 0.18 s; measure a little over that.</summary>
    private static readonly int LapMs = (int)Math.Ceiling(1000.0 / LookConstants.ArcSpeedPerSecond * 1.2);

    private sealed record Reading(double WorkingSetMb, double PrivateMb, double CpuMs, double WallMs, long Frames);

    public async Task RunAsync()
    {
        // The idle time is longer than the stretch that is measured (WORK-ORDER-11 section 4): the default 5 seconds let the island leave part-way through the open stretch.
        using var rt = new IslandRuntime(idleSeconds: 120);
        rt.Show();
        await Task.Delay(500);
        var c = rt.Controller;

        // Hidden: the windows are on screen, empty.
        var hiddenStart = Take(0);
        await Task.Delay(LapMs);
        var hidden = Take(c.FramesDrawn);
        var hiddenCpuMs = hidden.CpuMs - hiddenStart.CpuMs;
        var hiddenWallMs = hidden.WallMs - hiddenStart.WallMs;

        // Open, with the light moving.
        c.PageKey(PageIds.Media);
        await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), "the capsule open and at rest for the measurement", hangLimit, report);
        await Task.Delay(300);
        var openStart = Take(c.FramesDrawn);
        await Task.Delay(LapMs);
        var open = Take(c.FramesDrawn);
        var openAtEnd = c.Machine.Phase == IslandPhase.Open && !c.Machine.ShowsPill;
        report.Check("the island was still open at the end of the measured stretch", openAtEnd, $"phase {c.Machine.Phase}");

        c.ShowHide();
        await Waiter.UntilAsync(() => c.Machine.Phase == IslandPhase.Hidden, "the island hidden after the measurement", hangLimit, report);
        await Task.Delay(300);
        var hiddenAfter = Take(c.FramesDrawn);

        var openCpuMs = open.CpuMs - openStart.CpuMs;
        var openWallMs = open.WallMs - openStart.WallMs;
        var frames = open.Frames - openStart.Frames;

        Write(rt, hiddenStart, hiddenCpuMs, hiddenWallMs, open, openCpuMs, openWallMs, frames, hiddenAfter);

        report.Info["perf"] = new
        {
            cpuWhileHiddenPercentOfOneCore = Percent(hiddenCpuMs, hiddenWallMs),
            cpuWhileOpenPercentOfOneCore = Percent(openCpuMs, openWallMs),
            framesDrawnWhileOpen = frames,
            memoryWorkingSetMbHiddenBefore = Math.Round(hiddenStart.WorkingSetMb, 1),
            memoryWorkingSetMbOpen = Math.Round(open.WorkingSetMb, 1),
            memoryWorkingSetMbHiddenAfter = Math.Round(hiddenAfter.WorkingSetMb, 1),
            windowSizeDip = $"{rt.Host.WidthDip.ToString(CultureInfo.InvariantCulture)} x {rt.Host.HeightDip.ToString(CultureInfo.InvariantCulture)}",
            writtenTo = "perf.md",
        };
        report.Check("performance figures were measured and written to perf.md", File.Exists(Path.Combine(folder, "perf.md")), "records, not gates");
    }

    private static Reading Take(long frames)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return new Reading(p.WorkingSet64 / 1048576.0, p.PrivateMemorySize64 / 1048576.0, p.TotalProcessorTime.TotalMilliseconds,
            Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency, frames);
    }

    private static double Percent(double cpuMs, double wallMs) => wallMs <= 0 ? 0 : Math.Round(cpuMs / wallMs * 100, 2);

    private void Write(IslandRuntime rt, Reading hiddenStart, double hiddenCpu, double hiddenWall, Reading open, double openCpu, double openWall, long frames, Reading hiddenAfter)
    {
        var inv = CultureInfo.InvariantCulture;
        string F(double v, string f = "0.0") => v.ToString(f, inv);
        report.Info.TryGetValue("refreshIntervalMs", out var refresh);
        var cores = Environment.ProcessorCount;

        var sb = new StringBuilder();
        sb.AppendLine("# Performance records — Island (WORK-ORDER §8)");
        sb.AppendLine();
        sb.AppendLine($"Measured by `--selftest` on {DateTimeOffset.Now:yyyy-MM-dd}, one run, one laptop ({cores} logical processors, Windows 11, display refresh interval about {refresh} ms), Release build. **These are records, not gates, and one run on one laptop is an anecdote.**");
        sb.AppendLine();
        sb.AppendLine("| Measure | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Window size (two windows, same size, device-independent pixels) | {F(rt.Host.WidthDip, "0")} x {F(rt.Host.HeightDip, "0")} |");
        sb.AppendLine($"| Memory (working set) with the island hidden, before the first summon | {F(hiddenStart.WorkingSetMb)} MB (private {F(hiddenStart.PrivateMb)} MB) |");
        sb.AppendLine($"| Memory (working set) with the island open, Media page | {F(open.WorkingSetMb)} MB (private {F(open.PrivateMb)} MB) |");
        sb.AppendLine($"| Memory (working set) hidden again after the island left | {F(hiddenAfter.WorkingSetMb)} MB (private {F(hiddenAfter.PrivateMb)} MB) |");
        sb.AppendLine($"| Processor time while hidden, over {F(hiddenWall / 1000)} s | {F(hiddenCpu)} ms = {F(Percent(hiddenCpu, hiddenWall), "0.00")}% of one core |");
        sb.AppendLine($"| Processor time while open with the light moving, over {F(openWall / 1000)} s (more than one lap of the light: {F(1.0 / LookConstants.ArcSpeedPerSecond)} s) | {F(openCpu)} ms = {F(Percent(openCpu, openWall), "0.00")}% of one core, {frames} frames drawn |");
        sb.AppendLine();
        sb.AppendLine("## How each was measured");
        sb.AppendLine();
        sb.AppendLine("- **Memory:** after three garbage collections, `WorkingSet64` and `PrivateMemorySize64` of the self-test process itself (the island, its two windows and the test harness together; the tray icon and keybinds are not in this process during this step). Taken at the three moments named in the table.");
        sb.AppendLine("- **Processor time:** the difference in `Process.TotalProcessorTime` of the same process between the start and the end of a wait of the stated length, divided by the wall-clock time of the wait (Stopwatch). \"Hidden\" means the island has left and both windows are on screen, empty, with the app detached from the frame callback. \"Open\" means the capsule is open and at rest on the Media page, the contents shown, the rim light moving.");
        sb.AppendLine("- **Not measured:** graphics card load, power draw, other pages, other display scalings, a second monitor, memory growth over hours. The frame intervals during summons are in `selftest.json` under `info.frameIntervals`.");
        var path = Path.Combine(folder, "perf.md");
        // What other runs wrote under their own headings is kept: the cost of the island hidden for a minute (WORK-ORDER-6 section 6) and everything about what the open
        // island costs (WORK-ORDER-11 section 4: before, where it goes, after).
        File.WriteAllText(path, PerfSections.WithKept(sb.ToString(), File.Exists(path) ? File.ReadAllText(path) : null));
    }
}
