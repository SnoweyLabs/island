using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Island.Bridge;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 section 6, started as <c>--selftest &lt;folder&gt; --cost</c>: the app as it runs, hidden, with every reader switched on (windows,
/// installed programs, Explorer windows, media sessions, and the add-on's listener on a port of its own with nobody connected) for one
/// minute; the app's own processor time over that minute as a share of one core, and its memory. The figures go into review/perf.md.
/// Above one percent of one core the result is a NEEDS-HUMAN-VERIFY with the figures; nothing fails because of it.
/// </summary>
internal sealed class CostStage(SelfTestReport report, string tempRoot, string folder)
{
    public static readonly TimeSpan HiddenFor = TimeSpan.FromSeconds(60);

    /// <summary>Above this share of one core while hidden (WORK-ORDER.md section 8 measured 0.2% to 0.5% before the readers existed) a person is asked to look.</summary>
    public const double LookAbovePercent = 1.0;

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var bridge = new TabBridge([port]);
        bridge.Start();
        using var host = AppHost.Start(new AppFiles(Path.Combine(tempRoot, "cost")), quit: () => { }, summonOnStart: false);
        if (!report.Check("the app for the cost run starts, hidden, with every reader on", host is not null, "its own names, a stand-in key when the real one is held")) return;

        var controller = host!.Runtime.Controller;
        await Task.Delay(Settle); // the catalogue of installed programs and the readers have finished their first look
        var hiddenAtStart = controller.Machine.Phase == IslandPhase.Hidden;
        var framesBefore = controller.FramesDrawn;
        var cpuStart = CpuMs();
        var wallStart = Stopwatch.GetTimestamp();

        await Task.Delay(HiddenFor);

        var cpuEnd = CpuMs();
        var wallMs = (Stopwatch.GetTimestamp() - wallStart) * 1000.0 / Stopwatch.Frequency;
        var cpuMs = cpuEnd - cpuStart;
        var percent = wallMs <= 0 ? 0 : Math.Round(cpuMs / wallMs * 100, 2);
        var frames = controller.FramesDrawn - framesBefore;
        var (workingSetMb, privateMb) = Memory();

        report.Check("the island stayed hidden for the whole minute", hiddenAtStart && controller.Machine.Phase == IslandPhase.Hidden, "nothing summoned it");
        report.Check("nothing was drawn while nothing was visible", frames == 0, $"{frames} frame(s) drawn while hidden");
        report.Info["cost"] = new { hiddenSeconds = Math.Round(wallMs / 1000, 1), cpuPercentOfOneCore = percent, workingSetMb = Math.Round(workingSetMb, 1), privateMb = Math.Round(privateMb, 1), framesDrawn = frames };
        if (percent > LookAbovePercent)
            report.NeedsHumanVerify.Add($"While hidden for a minute with every reader on, the app used {percent.ToString("0.00", CultureInfo.InvariantCulture)}% of one core and {workingSetMb.ToString("0", CultureInfo.InvariantCulture)} MB (above {LookAbovePercent}%): does the laptop feel slower, or warm, when the island only sits there? The figures are in review/perf.md.");

        // The second figure (WORK-ORDER-7 section 2): the pill showing progress for one minute, from an invented session.
        var pillPercent = await PillMinuteAsync();
        report.Info["costPill"] = new { cpuPercentOfOneCore = pillPercent };
        if (pillPercent > LookAbovePercent)
            report.NeedsHumanVerify.Add($"With the pill showing progress for a minute the app used {pillPercent.ToString("0.00", CultureInfo.InvariantCulture)}% of one core (above {LookAbovePercent}%): leave the pill up while a long video plays; does the laptop feel slower or warm? The figures are in review/perf.md.");

        var section = Section(wallMs, cpuMs, percent, workingSetMb, privateMb, frames, pillPercent);
        try
        {
            CostSection.Upsert(Path.Combine(folder, "perf.md"), section);
            report.Check("the figures were written to perf.md", true, "records, not gates");
        }
        catch (IOException)
        {
            report.Check("the figures were written to perf.md", false, "perf.md could not be written");
        }
    }

    /// <summary>
    /// The pill up for a minute with its ring showing (an invented session of an hour, playing): the processor share of this process over that
    /// minute. The pill at rest lets go of the frame callback, so this is the cost of the timer that moves the ring and nothing else.
    /// </summary>
    private async Task<double> PillMinuteAsync()
    {
        var pretend = new Island.Core.PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var pages = new PickPages(() => new PickStore([]), world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(5, pages);
        rt.Show();
        await Task.Delay(150);
        pretend.Sessions = [new MediaSessionInfo("alpha", "alpha.exe", false, "Song", "Artist", PlaybackState.Playing, 10, 3600,
            new ProgressReport(10, 3600, DateTimeOffset.UtcNow, true, 1))];
        pretend.Raise();
        var up = await Waiter.UntilAsync(() => rt.Machine.ShowsPill && rt.Machine.IsAtRest, "the pill open and at rest for the cost run", TimeSpan.FromSeconds(20), report);
        if (!report.Check("the pill came up for the cost run, with a ring and without the frame callback", up && rt.Controller.RingShare is not null && !rt.Controller.RunsAtFrameRate, "invented session")) return 0;

        await Task.Delay(Settle);
        var cpuStart = CpuMs();
        var wallStart = Stopwatch.GetTimestamp();
        await Task.Delay(HiddenFor);
        var wallMs = (Stopwatch.GetTimestamp() - wallStart) * 1000.0 / Stopwatch.Frequency;
        return Math.Round((CpuMs() - cpuStart) / wallMs * 100, 2);
    }

    private static double CpuMs()
    {
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return p.TotalProcessorTime.TotalMilliseconds;
    }

    private static (double WorkingSetMb, double PrivateMb) Memory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return (p.WorkingSet64 / 1048576.0, p.PrivateMemorySize64 / 1048576.0);
    }

    private static string Section(double wallMs, double cpuMs, double percent, double workingSetMb, double privateMb, long frames, double pillPercent)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(CostSection.Heading);
        sb.AppendLine();
        sb.AppendLine($"Measured by `--selftest --cost` on {DateTimeOffset.Now:yyyy-MM-dd}, one run, one laptop ({Environment.ProcessorCount} logical processors), Release build. **A record, not a gate; one run on one laptop is an anecdote.**");
        sb.AppendLine();
        sb.AppendLine("| Measure | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Island hidden for | {(wallMs / 1000).ToString("0.0", inv)} s |");
        sb.AppendLine($"| Processor time of the app in that time | {cpuMs.ToString("0.0", inv)} ms = {percent.ToString("0.00", inv)}% of one core |");
        sb.AppendLine($"| Memory at the end (working set / private) | {workingSetMb.ToString("0.0", inv)} MB / {privateMb.ToString("0.0", inv)} MB |");
        sb.AppendLine($"| Frames drawn while hidden | {frames} |");
        sb.AppendLine($"| Above {LookAbovePercent}% of one core | {(percent > LookAbovePercent ? "yes: NEEDS-HUMAN-VERIFY" : "no")} |");
        sb.AppendLine($"| The same app with the small pill up for a minute, its ring showing an invented long item | {pillPercent.ToString("0.00", inv)}% of one core{(pillPercent > LookAbovePercent ? " (above the line: NEEDS-HUMAN-VERIFY)" : string.Empty)} |");
        sb.AppendLine();
        sb.AppendLine("- **What was on:** the app as a person runs it with its own names (one copy, settings and picks files in a temporary folder, a stand-in key when the real one is held elsewhere), the window lister, the installed-programs catalogue, the File Explorer reader and Windows' media sessions, and the add-on's listener on a port of its own with no add-on connected. The add-on's real listener is never started by a self-test.");
        sb.AppendLine($"- **How:** the difference in `Process.TotalProcessorTime` of this process over a wait of {HiddenFor.TotalSeconds:0} s after {Settle.TotalSeconds:0} s of settling, divided by the wall-clock time of the wait. The island is hidden from the start of the run; no frame callback runs while it is. Memory is read after three garbage collections.");
        sb.AppendLine("- **What was learnt (2026-10-06):** the first run read 3.18% of one core. Taking the readers away one at a time in one experiment (windows, Explorer, media, catalogue and icons all running: 3.59%; Explorer reader stopped: 0.55%; media reader stopped: 0.08%; window lister stopped: 0.00%; a bare process: 0.10%) showed the File Explorer reader, which read the shell every 2 seconds even while the island was hidden. It now rests while the island is hidden and reads at once when it is summoned; the run after that read 0.89%. Looked for once, as WORK-ORDER-6 says; not chased further (the media reader is most of what is left).");
        sb.AppendLine("- **Not measured:** the compositor (dwm) and the graphics card, a second screen, a media player that is playing, a browser with the add-on connected and many tabs, hours of running.");
        return sb.ToString();
    }
}

/// <summary>The cost section of review/perf.md, kept when the ordinary self-test rewrites the rest of the file.</summary>
internal static class CostSection
{
    public const string Heading = "## What it costs while hidden (WORK-ORDER-6 section 6)";

    /// <summary>Where the section lies in the text: from its heading up to the next heading of the same level (or the end). Null when there is none.</summary>
    private static (int Start, int End)? Find(string normal)
    {
        var start = normal.IndexOf(Heading, StringComparison.Ordinal);
        if (start < 0) return null;
        var next = normal.IndexOf("\n## ", start + Heading.Length, StringComparison.Ordinal);
        return (start, next < 0 ? normal.Length : next + 1);
    }

    /// <summary>The section, as one block ending in one line break; null when the text has none.</summary>
    public static string? Extract(string text)
    {
        var normal = text.Replace("\r\n", "\n");
        return Find(normal) is { } at ? normal[at.Start..at.End].TrimEnd('\n') + "\n" : null;
    }

    /// <summary>Puts the section into the file: in place of the earlier one, or after everything else.</summary>
    public static void Upsert(string path, string section)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : string.Empty;
        var fresh = section.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        var text = Find(existing) is { } at
            ? existing[..at.Start] + fresh + (at.End < existing.Length ? "\n" : string.Empty) + existing[at.End..]
            : existing.TrimEnd('\n') + (existing.Length == 0 ? string.Empty : "\n\n") + fresh;
        File.WriteAllText(path, text);
    }
}
