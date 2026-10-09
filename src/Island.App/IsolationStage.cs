using System.Diagnostics;
using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// NEXT-PLAN "FIRST, ONCE" step 1: the self-test shares nothing with a copy Dan runs. A helper process of the
/// self-test takes the real lock, the real main key and the first real port of the add-on (or finds each already
/// taken, which means Dan's copy runs and is the same proof); beside it the self-test's own copy starts, takes a
/// stand-in key and is summoned by it. The helper ends before the stage does. Only words like "held" and "taken"
/// and yes/no go into selftest.json.
/// </summary>
internal sealed class IsolationStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    public async Task RunAsync()
    {
        var dir = Path.Combine(tempRoot, "isolation");
        Directory.CreateDirectory(dir);

        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            report.Check("the helper that holds the real lock, key and port starts", false, "the program path is unknown");
            return;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "--selftest-hold", "--data-dir", dir },
        };
        using var helper = OutsideShell.StartOwnCopy(psi);
        if (helper is null)
        {
            report.Check("the helper that holds the real lock, key and port starts", false, "it could not be started");
            return;
        }

        try
        {
            await ProveAsync(helper, dir);
        }
        finally
        {
            if (!helper.HasExited) helper.Kill();
        }

        using var cts = new CancellationTokenSource(hangLimit);
        try
        {
            await helper.WaitForExitAsync(cts.Token);
            report.Check("the helper is gone before the stage ends", true, "ended");
        }
        catch (OperationCanceledException)
        {
            report.Check("the helper is gone before the stage ends", false, "it did not end within the hang limit");
        }
    }

    /// <summary>
    /// WORK-ORDER-6 section 5: a close request aimed at a window of a second process of the self-test's own (standing in for
    /// somebody else's window) is refused by the gate and counted, and the window is still there. No stage ever aims a close at a
    /// window the self-test did not make.
    /// </summary>
    private void CloseAimedAtAnotherProcess(Process helper, string dir)
    {
        var file = Path.Combine(dir, SelfTestHold.WindowFileName);
        if (!File.Exists(file) || !long.TryParse(File.ReadAllText(file), out var handle))
        {
            report.Check("the helper's stand-in window for somebody else's was made", false, "no handle was written");
            return;
        }

        var window = new IntPtr(handle);
        var theirs = (int)Native.ProcessIdOf(window) == helper.Id && helper.Id != Environment.ProcessId;
        var refusedBefore = OutsideGate.Current.Refused(OutsideKind.CloseWindow);
        var asked = OutsideClose.Request(window);
        report.Check("a close request aimed at a window of another process is refused by the gate and counted, and that window stays",
            theirs && !asked && OutsideGate.Current.Refused(OutsideKind.CloseWindow) == refusedBefore + 1 && Native.IsWindow(window),
            $"the window belongs to the helper: {theirs}; refused: {OutsideGate.Current.Refused(OutsideKind.CloseWindow) - refusedBefore}");
    }

    private async Task ProveAsync(Process helper, string dir)
    {
        var reported = await Waiter.UntilAsync(() => File.Exists(Path.Combine(dir, SelfTestHold.FileName)), "the helper's report", hangLimit, report);
        if (!reported) return;

        var found = File.ReadAllLines(Path.Combine(dir, SelfTestHold.FileName))
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);
        string Of(string name) => found.GetValueOrDefault(name, "missing");
        report.Info["isolation"] = new { realLock = Of("lock"), realMainKey = Of("key"), realPort = Of("port") };

        var known = new[] { "held", "taken" };
        report.Check("the real lock, the real main key and the first real port are each held by the helper or already held by another copy",
            known.Contains(Of("lock")) && known.Contains(Of("key")) && known.Contains(Of("port")), $"lock {Of("lock")}, key {Of("key")}, port {Of("port")}");

        CloseAimedAtAnotherProcess(helper, dir);

        // Beside it the self-test's own copy starts, on its own names, with a stand-in key.
        var files = new AppFiles(Path.Combine(dir, "copy"));
        using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
        if (!report.Check("the self-test's own copy starts while the real lock is held elsewhere", host is not null, "its lock has a name of its own"))
            return;

        var main = host!.Settings.ShowHide;
        report.Check("the stand-in key replaces the real main key when Windows holds it elsewhere, and is in the settings file",
            main != Settings.Defaults.ShowHide && StandInKey.Candidates.Contains(main) && Settings.Load(files.SettingsPath).Settings.ShowHide == main,
            "Ctrl+Alt+Shift with F11, F10 or F9");
        report.Check("the self-test's own settings name the mode Focus, and the table and the island's edge follow the settings' mode",
            host.Settings.Mode == Mode.Focus && host.Runtime.Gate.Mode == Mode.Focus && host.Runtime.Controller.Mode == Mode.Focus, $"mode {host.Settings.Mode}");
        report.Check("every key the settings contain is accepted by Windows", host.HotkeyResults.Count > 0 && host.HotkeyResults.All(r => r.Accepted),
            $"{host.HotkeyResults.Count} registered");

        // A summon by the stand-in key works (the key's message is handled exactly as Windows would hand it over).
        var c = host.Runtime.Controller;
        host.Press(main);
        var summoned = await Waiter.UntilAsync(() => c.Machine.Phase != IslandPhase.Hidden, "the summon by the stand-in key", hangLimit, report);
        report.Check("a summon by the stand-in key works", summoned, "the island left the hidden state");
        if (summoned) await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), "the island open", hangLimit, report);

        report.Check("the helper is still alive while this is proven", !helper.HasExited, "it holds the lock, key and port until the stage kills it");
    }
}
