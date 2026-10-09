using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Island.App;

/// <summary>
/// <c>--selftest &lt;folder&gt;</c>. Runs the app's own checks without any key press, writes
/// selftest.json into the folder and returns the process exit code:
/// 0 all passed, 1 a check failed, 2 no display, 3 another self-test is running.
/// </summary>
internal sealed class SelfTest
{
    public const int ExitFailed = 1;
    public const int ExitNoDisplay = 2;
    public const int ExitOtherCopy = 3;

    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(10);

    private readonly CommandLine _cmd;
    private readonly string _folder;
    private readonly string _jsonPath;
    private readonly SelfTestReport _report;

    private SelfTest(CommandLine cmd)
    {
        _cmd = cmd;
        _folder = Path.GetFullPath(cmd.SelfTestFolder!);
        _jsonPath = Path.Combine(_folder, "selftest.json");
        _report = new SelfTestReport(DateTimeOffset.Now);
    }

    public static async Task<int> RunAsync(CommandLine cmd)
    {
        var test = new SelfTest(cmd);
        try
        {
            return await test.RunCoreAsync();
        }
        catch (Exception e)
        {
            test._report.Check("self-test ran to the end", false, $"{e.GetType().Name}: {e.Message}");
            test._report.Verify = "RED — self-test crashed";
            return test.Finish(ExitFailed);
        }
    }

    private async Task<int> RunCoreAsync()
    {
        Directory.CreateDirectory(_folder);
        if (File.Exists(_jsonPath)) File.Delete(_jsonPath);
        _report.Save(_jsonPath); // the start time goes in first, so a stale file can never pass for a result

        if (OtherSelfTestRunning())
        {
            Say("Another self-test is running, so this one will not start.");
            _report.Check("no other self-test is running", false, "another self-test holds the self-test lock");
            _report.Verify = "RED — another self-test is running";
            return Finish(ExitOtherCopy);
        }

        var dataDir = _cmd.DataDir ?? Path.Combine(Path.GetTempPath(), "island-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        _report.Info["dataDir"] = _cmd.DataDir is null ? "fresh temporary folder" : "given by --data-dir";

        try
        {
            var code = await RunStagesAsync(dataDir);
            return Finish(code);
        }
        finally
        {
            if (_cmd.DataDir is null) TryDelete(dataDir);
        }
    }

    private async Task<int> RunStagesAsync(string dataDir)
    {
        // Everything that reaches the outside world asks this gate first; under the self-test it refuses all of it
        // except bringing forward the self-test's own windows and starting its own second copy.
        var gate = new Island.Core.OutsideGate(selfTest: true);
        Island.Core.OutsideGate.Current = gate;

        if (_cmd.Open)
        {
            // The measuring run of WORK-ORDER-11 section 4: the island held open on the pretend world. Never a pass for the whole self-test.
            await new OpenStage(_report, HangLimit, _folder, _cmd.OpenCase, _cmd.OpenLabel, _cmd.OpenLight).RunAsync();
            _report.Verify = _report.AllPassed ? "NO-VERIFIER — only the open-island measuring run was made (--open)" : "RED — a check failed, see selftest.json";
            return _report.AllPassed ? 0 : ExitFailed;
        }

        if (_cmd.Speed)
        {
            // The measuring run of WORK-ORDER-12 section 1: how quickly the island answers. Never a pass for the whole self-test.
            await new SpeedStage(_report, HangLimit, _folder, _cmd.SpeedLabel, _cmd.SpeedWarm).RunAsync();
            _report.Verify = _report.AllPassed ? "NO-VERIFIER — only the speed measuring run was made (--speed)" : "RED — a check failed, see selftest.json";
            return _report.AllPassed ? 0 : ExitFailed;
        }

        if (_cmd.Cost)
        {
            // The measuring run: a minute with the island hidden. Never a pass for the whole self-test.
            await new CostStage(_report, dataDir, _folder).RunAsync();
            _report.Verify = _report.AllPassed ? "NO-VERIFIER — only the cost run was made (--cost)" : "RED — a check failed, see selftest.json";
            return _report.AllPassed ? 0 : ExitFailed;
        }

        if (_cmd.OnlyStage is { } only)
        {
            // For working on one stage: the others are not run, and the result is never a pass for the whole self-test.
            switch (only.ToLowerInvariant())
            {
                case "capsule":
                    CapsuleStage.Run(_report, _folder);
                    break;
                case "looks":
                    LooksStage.Run(_report, _folder);
                    break;
                case "strip":
                    await new StripStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "plus":
                    await new PlusStage(_report, HangLimit, dataDir, _folder).RunAsync();
                    break;
                case "drag":
                    await new DragStage(_report, HangLimit, dataDir, _folder).RunAsync();
                    break;
                case "screens":
                    await new ScreensStage(_report, HangLimit).RunAsync();
                    break;
                case "front":
                    await new FrontStage(_report, HangLimit).RunAsync();
                    break;
                case "startup":
                    new StartupStage(_report, dataDir).Run();
                    break;
                case "modes":
                    new ModesStage(_report, _folder).Run();
                    break;
                case "search":
                    await new SearchStage(_report, HangLimit, _folder).RunAsync();
                    break;
                case "pill":
                    await new PillStage(_report, HangLimit, _folder).RunAsync();
                    break;
                case "agents":
                    await new AgentsStage(_report, HangLimit, _folder).RunAsync();
                    break;
                case "scenes":
                    await new ScenesStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "setup":
                    await new SetupStage(_report, HangLimit, dataDir, _folder).RunAsync();
                    break;
                case "close":
                    await new CloseStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "hand":
                    await new HandStage(_report, dataDir).RunAsync();
                    break;
                case "keys":
                    await new KeysStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "practice":
                    await new PracticeStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "terminals":
                    await new TerminalsStage(_report, HangLimit, _folder).RunAsync();
                    break;
                case "quiet":
                    await new QuietStage(_report, HangLimit).RunAsync();
                    break;
                case "light":
                    await new LightStage(_report, HangLimit).RunAsync();
                    break;
                case "picks":
                    await new PicksStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "shell":
                    await new ShellStage(_report, HangLimit, dataDir).RunAsync();
                    break;
                case "blur":
                    await new BlurStage(_report, HangLimit, _folder).RunAsync();
                    break;
                case "tabs":
                    await new TabsStage(_report, HangLimit).RunAsync();
                    break;
                case "settings":
                    await new SettingsStage(_report, HangLimit, dataDir, _folder).RunAsync();
                    break;
                default:
                    _report.Check($"--only knows the stage {only}", false, "looks, strip, plus, drag, screens, front, startup, close, modes, pill, search, agents, scenes, setup, settings, tabs, keys, hand, terminals, quiet, light, picks, blur, shell, capsule, practice");
                    break;
            }

            _report.Verify = _report.AllPassed ? $"NO-VERIFIER — only the {only} stage was run (--only)" : "RED — a check failed, see selftest.json";
            return _report.AllPassed ? 0 : ExitFailed;
        }

        var frames = new FrameCounter();
        var foregroundBefore = Native.GetForegroundWindow();

        var host = new IslandHost();
        TestShape.Build(host);
        host.Show();

        if (!await Waiter.UntilAsync(() => frames.Count >= 1, "the first rendered frame", HangLimit, _report))
        {
            Say("No frame was rendered: the screen is probably locked or the display is off.");
            _report.Info["noDisplay"] = "No frame was rendered within the hang limit; the screen is probably locked or the display is off.";
            _report.Verify = "NO-VERIFIER — display not available";
            host.Close();
            return ExitNoDisplay;
        }

        await Waiter.UntilAsync(() => frames.Count >= 10, "ten rendered frames", HangLimit, _report);
        frames.Dispose(); // from here the app's own frame callback is the only one, so "hidden costs nothing" can be tested

        WindowStage.Run(_report, host, foregroundBefore);
        host.Close();

        CapsuleStage.Run(_report, _folder);
        ChoicesStage.Run(_report, _folder);
        LooksStage.Run(_report, _folder);
        await new MotionStage(_report, HangLimit).RunAsync(foregroundBefore);
        await new ProgramsStage(_report, HangLimit).RunAsync();
        await new PicksStage(_report, HangLimit, dataDir).RunAsync();
        new ModesStage(_report, _folder).Run();
        await new PillStage(_report, HangLimit, _folder).RunAsync();
        await new SearchStage(_report, HangLimit, _folder).RunAsync();
        await new AgentsStage(_report, HangLimit, _folder).RunAsync();
        await new ScenesStage(_report, HangLimit, dataDir).RunAsync();
        await new SetupStage(_report, HangLimit, dataDir, _folder).RunAsync();
        await new CloseStage(_report, HangLimit, dataDir).RunAsync();
        await new StripStage(_report, HangLimit, dataDir).RunAsync();
        await new MediaStage(_report, HangLimit, _folder).RunAsync();
        await new FoldersStage(_report, HangLimit).RunAsync();
        await new PlusStage(_report, HangLimit, dataDir, _folder).RunAsync();
        await new DragStage(_report, HangLimit, dataDir, _folder).RunAsync();
        await new KeysStage(_report, HangLimit, dataDir).RunAsync();
        await new PracticeStage(_report, HangLimit, dataDir).RunAsync();
        await new HandStage(_report, dataDir).RunAsync();
        await new TerminalsStage(_report, HangLimit, _folder).RunAsync();
        await new QuietStage(_report, HangLimit).RunAsync();
        await new LightStage(_report, HangLimit).RunAsync();
        await new ScreensStage(_report, HangLimit).RunAsync();
        await new FrontStage(_report, HangLimit).RunAsync();
        new StartupStage(_report, dataDir).Run();
        await new TabsStage(_report, HangLimit).RunAsync();
        await new SettingsStage(_report, HangLimit, dataDir, _folder).RunAsync();
        await new KeyboardStage(_report, HangLimit).RunAsync();
        await new ShellStage(_report, HangLimit, dataDir).RunAsync();
        await new PerfStage(_report, HangLimit, _folder).RunAsync();
        await new BlurStage(_report, HangLimit, _folder).RunAsync();
        _report.Info["mainKeyStandIn"] = SelfTestIsolation.StandInUsed; // read before the isolation stage, which uses a stand-in on purpose
        await new IsolationStage(_report, HangLimit, dataDir).RunAsync();
        CheckGate(gate);

        if (!_report.AllPassed) _report.Verify = "RED — a check failed, see selftest.json";
        else if (_report.NeedsHumanVerify.Count > 0) _report.Verify = "NEEDS-HUMAN-VERIFY — " + string.Join(" | ", _report.NeedsHumanVerify);
        else _report.Verify = "GREEN";
        return _report.AllPassed ? 0 : ExitFailed;
    }

    /// <summary>The outside gate counted what it refused (numbers only, never what it was asked about).</summary>
    private void CheckGate(Island.Core.OutsideGate gate)
    {
        var before = gate.RefusedTotal;
        var refused = !gate.Allow(Island.Core.OutsideKind.StartProgram) && !gate.Allow(Island.Core.OutsideKind.BringForward, Environment.ProcessId + 1)
                      && !gate.Allow(Island.Core.OutsideKind.MediaCommand) && !gate.Allow(Island.Core.OutsideKind.OpenFolder);
        _report.Check("the outside gate refuses starting programs, opening folders and sending media commands under the self-test and counts them",
            refused && gate.RefusedTotal == before + 4 && gate.Allow(Island.Core.OutsideKind.BringForward, Environment.ProcessId),
            $"{gate.RefusedTotal - 4} refused before this check, by kind: {string.Join(", ", gate.RefusedCounts().Select(p => $"{p.Key} {p.Value}"))}");
        _report.Info["outsideGate"] = new { allowed = gate.AllowedCount, refused = gate.RefusedCounts() };
    }

    // A copy Dan runs is not a reason to refuse: the self-test shares nothing with it (names, ports and keys of its own).
    // Only another self-test is: it holds this lock until its process ends. The handle stays open for the whole run.
    private static Mutex? _runLock;

    private static bool OtherSelfTestRunning()
    {
        _runLock = new Mutex(false, Island.Core.InstanceNames.SelfTestRun, out var created);
        return !created;
    }

    private int Finish(int code)
    {
        _report.Finish();
        _report.Save(_jsonPath);
        Say($"Self-test finished: {_report.Verify}. Details in selftest.json.");
        return code;
    }

    private static void Say(string message)
    {
        try { Console.Out.WriteLine(message); }
        catch (IOException) { /* a windowed app may have no console to write to */ }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* temp folder */ }
    }
}

/// <summary>Waits for a named event. A time limit exists only to catch a hang, and expiring is a failure.</summary>
internal static class Waiter
{
    public static async Task<bool> UntilAsync(Func<bool> condition, string eventName, TimeSpan hangLimit, SelfTestReport report)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > hangLimit)
            {
                report.Check($"wait for {eventName}", false, $"the event did not happen within the hang limit of {hangLimit.TotalSeconds:0} s");
                return false;
            }

            await Task.Delay(10);
        }

        return true;
    }
}
