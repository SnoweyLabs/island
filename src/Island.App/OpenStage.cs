using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Core.Terminals;

namespace Island.App;

/// <summary>
/// WORK-ORDER-11 section 4, started as <c>--selftest &lt;folder&gt; --open</c>: what the open island costs. It holds the island open with an idle time longer than the stretch it
/// measures, on the self-test's pretend world, for each case (a page, a glass and a mode), and gives the app's own processor time as a share of one core, the frames drawn and how
/// many times each layer was rendered. Each figure is the middle one of three readings, written with its frame count and the spread of the three. It confirms at the end of each
/// stretch that the island was still open. It is not part of the ordinary self-test (which is never run in a loop) and is not changed between "before" and "after".
/// <c>--open-case page,glass,mode</c> runs one case (page: picks, media, terminals; glass: approved, darker, blur; mode: focus, vibe, dnd); without it the whole table is run.
/// <c>--open-label before|after</c> names the table in review/perf.md.
/// </summary>
internal sealed class OpenStage(SelfTestReport report, TimeSpan hangLimit, string folder, string? onlyCase, string label, string? lights = null)
{
    /// <summary>One stretch of a reading: more than one lap of the light (5.6 s), so a lap's worth of every kind of frame is in it.</summary>
    public static readonly TimeSpan Stretch = TimeSpan.FromSeconds(8);

    private const int Readings = 3;

    private sealed record Case(string Page, GlassKind Glass, Mode Mode, LightKind Light = LightKind.AsBefore)
    {
        public string Name => $"{Page}, {Glass.ToString().ToLowerInvariant()}, {Mode}, light {Light.ToString().ToLowerInvariant()}";
    }

    private sealed record Reading(double Percent, long Frames, IReadOnlyList<(string Name, long Renders)> Renders, bool StillOpen);

    private sealed record Figure(Case Case, double Percent, long Frames, double Spread, IReadOnlyList<(string Name, long Renders)> Renders, bool StillOpen, string? Note);

    public async Task RunAsync()
    {
        var cases = Cases();
        var figures = new List<Figure>();
        foreach (var one in cases) figures.Add(await MeasureAsync(one));

        Write(figures);
        report.Info["openCost"] = figures.Select(f => new { page = f.Case.Page, glass = f.Case.Glass.ToString(), mode = f.Case.Mode.ToString(), light = f.Case.Light.ToString(), percentOfOneCore = f.Percent, frames = f.Frames, spread = f.Spread, stillOpen = f.StillOpen, note = f.Note }).ToList();
        report.Check("every case was measured with the island still open at the end of its stretch", figures.All(f => f.StillOpen || f.Note is not null), $"{figures.Count} cases");
        report.Check("the figures were written to perf.md", File.Exists(Path.Combine(folder, "perf.md")), "records, not gates");
    }

    private IReadOnlyList<Case> Cases()
    {
        // --open-light names the kinds of light to measure (WORK-ORDER-12 section 2), e.g. asbefore,fixedrate,graphicscard; without it only the light as before is measured.
        var kinds = (lights ?? "asbefore").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(k => Enum.Parse<LightKind>(k, ignoreCase: true)).ToList();
        var all = new List<Case>();
        if (onlyCase is not null)
        {
            var parts = onlyCase.Split(',');
            foreach (var kind in kinds)
                all.Add(new Case(parts[0], Enum.Parse<GlassKind>(parts.ElementAtOrDefault(1) ?? "approved", ignoreCase: true), Enum.Parse<Mode>(parts.ElementAtOrDefault(2) ?? "vibe", ignoreCase: true), kind));
            return all;
        }

        foreach (var kind in kinds)
        {
            foreach (var mode in new[] { Mode.Vibe, Mode.Focus, Mode.DND })
            foreach (var page in new[] { "picks", "media", "terminals" })
                all.Add(new Case(page, GlassKind.Approved, mode, kind));
            foreach (var glass in new[] { GlassKind.Darker, GlassKind.Blur }) all.Add(new Case("picks", glass, Mode.Vibe, kind));
        }

        return all;
    }

    private async Task<Figure> MeasureAsync(Case one)
    {
        var pretend = new PretendWorld
        {
            Windows =
            [
                new OpenWindow(9001, "alpha.exe", null, "invented", 0),
                new OpenWindow(9002, "beta.exe", null, "invented", 1),
                new OpenWindow(9003, "alpha-player.exe", null, "invented", 2),
                new OpenWindow(9101, "alpha-term.exe", null, "invented shell", 3, "INVENTED_TERMINAL_CLASS", 4101),
            ],
            Sessions = one.Page == "media" ? [new MediaSessionInfo("s1", "alpha-player.exe", false, "Alpha track", "Beta artist", PlaybackState.Playing, 30, 3600, new ProgressReport(30, 3600, DateTimeOffset.UtcNow, true, 1))] : [],
        };
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var store = new PickStore(
        [
            Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null), Pick.ForProgram("Gamma", PageIds.Apps, "gamma.exe", null),
            Pick.ForProgram("Delta", PageIds.Apps, "delta.exe", null), Pick.ForProgram("Epsilon", PageIds.Apps, "epsilon.exe", null),
            Pick.ForProgram("Alpha Player", PageIds.Media, "alpha-player.exe", null), Pick.ForSite("Example Site", "example.org", PageIds.Media),
        ]);
        var tables = new TerminalTables(["INVENTED_TERMINAL_CLASS"], [new TerminalProgramRow("alpha-term.exe", "Alpha Term")], [], [new HelperProgramRow("invented-helper.exe", TerminalConstants.ClaudeCode)],
            new Dictionary<string, HelperColor> { [TerminalConstants.ClaudeCode] = TerminalConstants.ClaudeCodeColor });
        var helpers = new HelperSessions(tables);
        var probe = new InventedProbe(
            new TerminalProbeFacts([new ConsoleWindowFact(9201, "PseudoConsoleWindow", Environment.ProcessId, 9101)], [new ProcessFact(Environment.ProcessId, 1, "invented-helper.exe")]));
        var terminals = new TerminalsPage(world, probe, tables, readsWindows: true, helpers, synchronousIcons: true);
        var pages = new PickPages(() => store, world, synchronousIcons: true) { Terminals = terminals };
        using var rt = new IslandRuntime(3600, pages, null, null, takeKeyboard: false); // the idle time is longer than every stretch: the island stays open
        rt.Show();
        if (one.Light == LightKind.GraphicsCard) rt.EnableMovingLight(); // the graphics-card light needs its windows
        await Task.Delay(300);
        var c = rt.Controller;
        var m = c.Machine;
        c.Light = one.Light;
        c.SetMode(one.Mode, instant: true);
        if (one.Glass == GlassKind.Blur && !rt.BlurAvailable) return new Figure(one, 0, 0, 0, [], false, "Blur is not available on this laptop now");
        rt.SetGlass(one.Glass);

        var pageId = one.Page switch { "media" => PageIds.Media, "terminals" => PageIds.Terminals, _ => PageIds.Apps };
        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest, "the island open for the measurement", hangLimit, report);
        c.PageKey(pageId);
        await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == pageId, "the page open and at rest for the measurement", hangLimit, report);
        if (one.Page == "terminals")
        {
            helpers.Apply(new Island.Core.Agents.Sessions.SessionMessage("claude", "UserPromptSubmit", "", "open-cost-session", Environment.TickCount64, "Q:\\Invented\\island", [Environment.ProcessId]));
            terminals.ProbeOnce();
            await Waiter.UntilAsync(() => m.ContentsItems.Count > 0 && m.ContentsItems[0].Ring == HelperState.Working, "the working ring drawn", hangLimit, report);
        }

        if (one.Page == "media") c.MediaChanged();
        await Task.Delay(1500); // everything settled: the frame callback runs, the light turns

        var readings = new List<Reading>();
        for (var i = 0; i < Readings; i++) readings.Add(await ReadAsync(rt));
        var middle = readings.OrderBy(r => r.Percent).ElementAt(Readings / 2);
        var spread = readings.Max(r => r.Percent) - readings.Min(r => r.Percent);
        return new Figure(one, middle.Percent, middle.Frames, Math.Round(spread, 2), middle.Renders, readings.All(r => r.StillOpen), null);
    }

    private async Task<Reading> ReadAsync(IslandRuntime rt)
    {
        var c = rt.Controller;
        var framesBefore = c.FramesDrawn;
        var rendersBefore = rt.View.LayerRenders();
        var cpuBefore = CpuMs();
        var wallBefore = Stopwatch.GetTimestamp();
        await Task.Delay(Stretch);
        var cpu = CpuMs() - cpuBefore;
        var wallMs = (Stopwatch.GetTimestamp() - wallBefore) * 1000.0 / Stopwatch.Frequency;
        var frames = c.FramesDrawn - framesBefore;
        var rendersAfter = rt.View.LayerRenders();
        var renders = rendersAfter.Select((r, i) => (r.Name, r.Renders - rendersBefore[i].Renders)).ToList();
        var open = c.Machine.Phase == IslandPhase.Open && !c.Machine.ShowsPill;
        return new Reading(Math.Round(cpu / wallMs * 100, 2), frames, renders, open);
    }

    private static double CpuMs()
    {
        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return p.TotalProcessorTime.TotalMilliseconds;
    }

    private void Write(IReadOnlyList<Figure> figures)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        var heading = lights is null ? $"{PerfSections.OpenCostPrefix} §4) — {label}" : $"{PerfSections.OpenCostPrefix12}) — {label}";
        sb.AppendLine(heading);
        sb.AppendLine();
        sb.AppendLine($"Measured by `--selftest <folder> --open` on {DateTimeOffset.Now:yyyy-MM-dd}, one laptop, Release build, the self-test's pretend world, the island held open (idle time longer than the stretch). Each figure is the middle one of three readings of {Stretch.TotalSeconds:0} s, as the app's own processor time in percent of one core, with its frame count and the spread of the three readings (highest minus lowest). Nothing else of this run's was going on in another worktree while it ran unless the line says so. **A record, not a gate; one run on one laptop is an anecdote.**");
        sb.AppendLine();
        sb.AppendLine("| Page | Glass | Mode | Light | % of one core | Frames | Spread | Layers rendered (counts per stretch) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var f in figures)
        {
            var renders = f.Note is not null ? f.Note : string.Join(", ", f.Renders.Select(r => $"{r.Name} {r.Renders}"));
            sb.AppendLine($"| {f.Case.Page} | {f.Case.Glass.ToString().ToLowerInvariant()} | {f.Case.Mode} | {Spoken(f.Case.Light)} | {f.Percent.ToString("0.00", inv)} | {f.Frames} | {f.Spread.ToString("0.00", inv)} | {renders} |");
        }

        var path = Path.Combine(folder, "perf.md");
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        File.WriteAllText(path, PerfSections.Upsert(existing, sb.ToString()));
        foreach (var f in figures) Console.WriteLine($"{f.Case.Name}: {f.Percent:0.00}% of one core, {f.Frames} frames, spread {f.Spread:0.00}");
    }

    private sealed class InventedProbe(TerminalProbeFacts facts) : ITerminalProbe
    {
        public TerminalProbeFacts Read() => facts;
    }

    private static string Spoken(LightKind kind) => kind switch { LightKind.GraphicsCard => "Graphics card", LightKind.FixedRate => "Fixed rate", _ => "As before" };
}
