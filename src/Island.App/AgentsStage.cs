using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Island.App.Visuals;
using Island.Core;
using Island.Sources.Front;

namespace Island.App;

/// <summary>
/// WORK-ORDER-7 section 4 with no real agent and never the real pipe: the island listens on a pipe name invented for this run, Island.Notify is started
/// as a helper with the hooks page's Stop example (folder ending "island") and the notice must read "island" and "Agent finished - waiting for you"
/// without the foreground changing; Island.Notify aimed at a pipe nobody listens on ends with code 0 and says how long it took; the connector, asked for
/// anything, is refused by the gate (the real settings file of Claude Code is never opened here). A snapshot goes to review/choices/notice.png.
/// </summary>
internal sealed class AgentsStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    private const string StopExample = "{\"session_id\":\"abc123\",\"hook_event_name\":\"Stop\",\"cwd\":\"C:\\\\work\\\\island\"}";
    private static readonly TimeSpan SlowExit = TimeSpan.FromSeconds(1);

    public async Task RunAsync()
    {
        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var store = new PickStore([]);
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages);
        rt.Show();
        await Task.Delay(150);
        var m = rt.Controller.Machine;
        using var host = new AgentNoticeHost(rt, world);
        var pipe = "island.selftest." + Guid.NewGuid().ToString("N");
        using var server = new Island.Agents.AgentPipeServer(pipe);
        var ui = Dispatcher.CurrentDispatcher;
        server.NoticeReceived += n => ui.BeginInvoke(() => host.Post(n));
        report.Check("the island listens on a pipe name invented for this run", server.Start(), pipe.Length > 0 ? "invented name" : "no name");

        var before = Native.GetForegroundWindow();
        var exit = RunNotify(pipe, StopExample, out var took);
        report.Check("Island.Notify ends with code 0 when the island listens", exit == 0, $"exit code {exit}");
        var shown = await Waiter.UntilAsync(() => m.ShowsNotice && m.IsAtRest && host.Showing is not null, "the notice open and at rest", hangLimit, report);
        var lines = rt.View.Notice.Lines;
        report.Check("the notice names the project and says the agent is done", shown && lines.Project == "island" && lines.Line == "Agent finished \u2014 waiting for you", $"{lines.Project} / {lines.Line}");
        report.Check("the notice takes no keyboard", !m.HasKeyboard, $"keyboard {m.HasKeyboard}");
        report.Check("the foreground window is the one from before, or foregroundGranted: false", Native.GetForegroundWindow() == before, "the notice took nothing");

        // A click brings the terminal forward; here nobody owns a window of the chain, so it only goes.
        rt.View.Notice.RaiseClickForSelfTest();
        await Waiter.UntilAsync(() => !m.ShowsNotice, "the notice gone after a click", hangLimit, report);
        report.Check("a click on the notice dismisses it", !m.ShowsNotice && host.Showing is null, "gone");

        // Nobody listens: Island.Notify must still end with 0, and quickly.
        var lonely = "island.selftest." + Guid.NewGuid().ToString("N");
        var exitAlone = RunNotify(lonely, StopExample, out var alone);
        report.Check("Island.Notify with nobody listening ends with code 0", exitAlone == 0, $"exit code {exitAlone}, {alone.TotalMilliseconds:0} ms");
        if (alone > SlowExit) report.NeedsHumanVerify.Add($"Island.Notify with no island listening took {alone.TotalMilliseconds:0} ms; a hook should never wait this long");
        report.Info["notifyNobodyListeningMs"] = $"{alone.TotalMilliseconds:0}";
        report.Info["notifyListeningMs"] = $"{took.TotalMilliseconds:0}";

        // The connector never reaches the real file under the self-test: the gate refuses and counts it.
        var refused = OutsideGate.Current.Refused(OutsideKind.EditAgentSettings);
        var connector = new OutsideAgentConnector(PretendPackage.NotPackaged);
        var state = connector.State();
        var result = connector.Connect();
        report.Check("the connector is refused by the gate under the self-test and touches nothing",
            state == AgentConnection.NotConnected && !result.Done && OutsideGate.Current.Refused(OutsideKind.EditAgentSettings) == refused + 2, $"{state}, done {result.Done}");

        // WORK-ORDER-11: Codex's connector and the update are refused the same way, and under the gate every row reads "Not connected.".
        var codex = new OutsideCodexConnector(PretendPackage.NotPackaged);
        var refusedBeforeCodex = OutsideGate.Current.Refused(OutsideKind.EditAgentSettings);
        var codexState = codex.State();
        var codexConnect = codex.Connect();
        var codexUpdate = codex.Update();
        var claudeUpdate = connector.Update();
        report.Check("Codex's connector and every update are refused by the gate under the self-test and touch nothing",
            codexState == AgentConnection.NotConnected && !codexConnect.Done && !codexUpdate.Done && !claudeUpdate.Done
            && OutsideGate.Current.Refused(OutsideKind.EditAgentSettings) == refusedBeforeCodex + 4, $"{codexState}, refused {OutsideGate.Current.Refused(OutsideKind.EditAgentSettings) - refusedBeforeCodex} of 4");

        // What one run of Island.Notify costs (WORK-ORDER-11 section 3): the middle of five runs, in time from start to end and in processor time. Each tool a helper uses starts it once.
        var times = new List<(double Ms, double Cpu)>();
        for (var i = 0; i < 5; i++)
        {
            var run = RunNotifyWith([pipe], StopExample);
            if (run.Exit == 0) times.Add((run.Took.TotalMilliseconds, run.Cpu.TotalMilliseconds));
        }

        if (times.Count > 0)
        {
            var middleMs = times.OrderBy(t => t.Ms).ElementAt(times.Count / 2).Ms;
            var middleCpu = times.OrderBy(t => t.Cpu).ElementAt(times.Count / 2).Cpu;
            report.Info["notifyRunMs"] = Math.Round(middleMs, 1);
            report.Info["notifyRunCpuMs"] = Math.Round(middleCpu, 1);
            report.Check("one run of Island.Notify was timed, start to end and in processor time (a measurement, not a limit)", times.Count == 5, $"{middleMs:0} ms, {middleCpu:0} ms of processor time (middle of {times.Count})");
        }

        await ClickBringsTheTerminalForwardAsync();
        await TheNoticeObeysTheModesAsync();
        await TheNoticeGoesToTheSecondScreenAsync();
        Snapshot();
    }

    /// <summary>A world with one window of this program's own (the island's window, standing for a terminal) and a recording door: what a click on the notice asks for is read from the record.</summary>
    private static (IslandRuntime Rt, AgentNoticeHost Host, RecordingOutside Recording, PretendWorld Pretend, long Handle) NewNoticeWorld()
    {
        var pretend = new PretendWorld();
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var pages = new PickPages(() => new PickStore([]), world, synchronousIcons: true, plusTile: false);
        var rt = new IslandRuntime(30, pages);
        return (rt, new AgentNoticeHost(rt, world), recording, pretend, 0);
    }

    /// <summary>EVALS A2: a click on the notice brings the terminal of the helper forward: the nearest program of the chain that owns a window, asked of the one outside door (recorded here, never used).</summary>
    private async Task ClickBringsTheTerminalForwardAsync()
    {
        var (rt, host, recording, pretend, _) = NewNoticeWorld();
        using (rt)
        using (host)
        {
            rt.Show();
            await Task.Delay(150);
            var handle = rt.Host.Capsule.Handle.ToInt64(); // the window exists once the island is shown
            pretend.Windows = [new OpenWindow(handle, "island.exe", null, "island", 0)];
            host.Post(new AgentNotice(AgentSignal.Finished, "island", "click-key", [Environment.ProcessId]));
            var shown = await Waiter.UntilAsync(() => rt.Machine.ShowsNotice && host.Showing is not null, "the notice showing for the click check", hangLimit, report);
            rt.View.Notice.RaiseClickForSelfTest();
            await Waiter.UntilAsync(() => !rt.Machine.ShowsNotice, "the notice gone after the click on a chain that owns a window", hangLimit, report);
            report.Check("a click on the notice asks the outside door to bring the window of the chain's program forward, once, and the notice goes",
                shown && recording.Calls.Count(c => c == $"bring:{handle}") == 1 && !rt.Machine.ShowsNotice && host.Showing is null,
                $"shown {shown}, calls: {string.Join(", ", recording.Calls)}");
        }
    }

    /// <summary>EVALS A5 and X2 to X4: the notice obeys the mode. Vibe over a fullscreen program keeps it waiting, Do not disturb drops it, and one that is showing leaves when the table stops allowing it.</summary>
    private async Task TheNoticeObeysTheModesAsync()
    {
        var notice = new AgentNotice(AgentSignal.Finished, "island", "mode-key", [Environment.ProcessId]);
        var (rt, host, _, pretend, _) = NewNoticeWorld();
        using (rt)
        using (host)
        {
            rt.Show();
            await Task.Delay(150);

            // Vibe over a fullscreen program: it stays away and waits (no sound in Vibe, nothing shown by itself).
            rt.Gate.Mode = Mode.Vibe;
            rt.Gate.Reading = () => new FrontReading(FrontState.FullscreenProgram, null);
            host.Post(notice);
            await Task.Delay(400);
            report.Check("in Vibe over a fullscreen program the notice does not appear by itself", !rt.Machine.ShowsNotice && host.Showing is null, $"shows {rt.Machine.ShowsNotice}");

            // The fullscreen program goes: the waiting notice comes (shown once the table allows it).
            rt.Gate.Reading = () => new FrontReading(FrontState.Clear, null);
            pretend.Raise();
            var came = await Waiter.UntilAsync(() => rt.Machine.ShowsNotice && host.Showing is not null, "the waiting notice showing once nothing is fullscreen", hangLimit, report);
            report.Check("the waiting notice is shown once the table allows it", came, "shown after the front cleared");

            // It is showing and a fullscreen program comes to the front in Vibe: it leaves by itself.
            rt.Gate.Reading = () => new FrontReading(FrontState.FullscreenProgram, null);
            pretend.Raise();
            var left = await Waiter.UntilAsync(() => !rt.Machine.ShowsNotice, "the notice leaving when the table stops allowing it", hangLimit, report);
            report.Check("a notice that appeared by itself leaves when a fullscreen program comes to the front in Vibe", left && host.Showing is null, $"shows {rt.Machine.ShowsNotice}");

            // Do not disturb: a notice is dropped, and does not come back when the front clears.
            rt.Gate.Mode = Mode.DND;
            rt.Gate.Reading = () => new FrontReading(FrontState.Clear, null);
            host.Post(new AgentNotice(AgentSignal.Finished, "island", "dnd-key", [Environment.ProcessId]));
            await Task.Delay(500);
            pretend.Raise();
            await Task.Delay(300);
            report.Check("in Do not disturb a notice never appears", !rt.Machine.ShowsNotice && host.Showing is null, $"shows {rt.Machine.ShowsNotice}");
        }
    }

    /// <summary>Dan's Q2 (WORK-ORDER-13, E-X8): with a fullscreen program in front and a second screen, the notice comes on the second screen at once (no sound, no waiting); on one screen, or in Do not disturb, it does not.</summary>
    private async Task TheNoticeGoesToTheSecondScreenAsync()
    {
        var main = new ScreenInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.0, true);
        var right = new ScreenInfo(new PixelRect(1920, 0, 3840, 1080), new PixelRect(1920, 0, 3840, 1040), 1.0);
        var (rt, host, _, pretend, _) = NewNoticeWorld();
        using (rt)
        using (host)
        {
            rt.Show();
            await Task.Delay(150);
            rt.Gate.Mode = Mode.Vibe;
            rt.Gate.Reading = () => new FrontReading(FrontState.FullscreenProgram, null);

            host.ScreensProbe = () => ([main], main.Full);
            host.Post(new AgentNotice(AgentSignal.Finished, "island", "one-screen", [Environment.ProcessId]));
            await Task.Delay(400);
            report.Check("with one screen and a fullscreen program in front the notice does not appear, as before", !rt.Machine.ShowsNotice && host.LastOtherScreen is null, $"shows {rt.Machine.ShowsNotice}");

            host.ScreensProbe = () => ([main, right], main.Full);
            pretend.Raise();
            var came = await Waiter.UntilAsync(() => rt.Machine.ShowsNotice && host.Showing is not null, "the notice on the second screen", hangLimit, report);
            report.Check("with a second screen and a fullscreen program in front on the first, the notice goes to the second screen", came && host.LastOtherScreen == right.Full, $"shows {rt.Machine.ShowsNotice}, other screen {host.LastOtherScreen}");
            await Task.Delay(1200);
            report.Check("a notice on the second screen is not taken away because the first screen still has the fullscreen program in front", rt.Machine.ShowsNotice && host.Showing is not null, $"shows {rt.Machine.ShowsNotice}");

            rt.Gate.Mode = Mode.DND;
            host.Post(new AgentNotice(AgentSignal.Finished, "island", "dnd-second", [Environment.ProcessId]));
            await Task.Delay(300);
        }
    }

    private static int RunNotify(string pipe, string json, out TimeSpan took)
    {
        var run = RunNotifyWith([pipe], json);
        took = run.Took;
        return run.Exit;
    }

    /// <summary>Island.Notify started with these arguments and this input, on an invented pipe: how it ended, how long it took and how much processor time it used.</summary>
    internal static (int Exit, TimeSpan Took, TimeSpan Cpu) RunNotifyWith(string[] arguments, string json)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Island.Notify.exe");
        var info = new ProcessStartInfo(exe) { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var clock = Stopwatch.StartNew();
        using var p = OutsideShell.StartOwnCopy(info);
        if (p is null) return (-1, clock.Elapsed, TimeSpan.Zero);

        p.StandardInput.Write(json);
        p.StandardInput.Close();
        var done = p.WaitForExit(5000);
        var took = clock.Elapsed;
        if (!done)
        {
            p.Kill();
            return (-2, took, TimeSpan.Zero);
        }

        var cpu = TimeSpan.Zero;
        try
        {
            cpu = p.TotalProcessorTime;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // an exited process whose times cannot be read: the time from start to end still counts
        }

        return (p.ExitCode, took, cpu);
    }

    private void Snapshot()
    {
        var scene = new OffscreenScene(new Rgb(27, 31, 58));
        var path = Path.Combine(folder, "choices", "notice.png");
        scene.RenderNotice(new NoticeContent("island", "Agent finished \u2014 waiting for you"), ModeMark.Look.Approved, 2).SavePng(path);
        report.Check("the notice was drawn into review/choices/notice.png", File.Exists(path), "a snapshot proves it draws, not that it looks right");
    }
}
