using System.Windows;

namespace Island.App;

public partial class App : Application
{
    private AppHost? _host;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var cmd = CommandLine.Parse(e.Args);

        if (cmd.RenderAssets is not null)
        {
            ShipAssets.Render(cmd.RenderAssets);
            Shutdown(0);
            return;
        }

        if (cmd.UninstallCleanup)
        {
            // Asked by the uninstaller, and by nothing else. With --selftest beside it the gate is the self-test's, which refuses it as it refuses the buttons.
            var gate = cmd.SelfTestFolder is not null ? new Island.Core.OutsideGate(selfTest: true) : Island.Core.OutsideGate.Current;
            var package = AppHost.NewPackageFacts();
            var outcome = Island.Core.UninstallCleanup.Run(
                [new OutsideAgentConnector(package), new OutsideCodexConnector(package)],
                Island.Core.StartupChoice.For(package, () => new Island.Core.StartupSwitch(new OutsideStartupRegistry(), Environment.ProcessPath ?? string.Empty), () => new OutsideStartupTask()),
                gate);
            Shutdown(outcome.AllDone ? 0 : ExitCodes.CleanupIncomplete);
            return;
        }

        if (cmd.SelfTestFolder is not null)
        {
            var code = await SelfTest.RunAsync(cmd);
            Shutdown(code);
            return;
        }

        if (cmd.SelfTestHold)
        {
            await SelfTestHold.RunAsync(cmd.DataDir);
            Shutdown(0);
            return;
        }

        if (cmd.SelfTestCopy) Island.Core.OutsideGate.Current = new Island.Core.OutsideGate(selfTest: true); // meets the self-test's own lock, never Dan's

        if (cmd.Quit)
        {
            SingleInstance.SignalRunningCopy(quit: true);
            Shutdown(0);
            return;
        }

        // A mistake in some event handler must not take the tray app down while Dan works: it is logged by kind only
        // (a message could carry a window title or a name) and the app goes on. Not installed under the self-test,
        // where such a mistake has to fail the run.
        DispatcherUnhandledException += (_, args) =>
        {
            _host?.Files.Log("unhandled error on the window thread: " + args.Exception.GetType().Name);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _host?.Files.Log("unobserved error in a background task: " + args.Exception.GetType().Name);
            args.SetObserved();
        };

        // Started by Windows's start-up list (--autostart) or, packaged, by the package's start-up task (no argument: the activation kind says so): silent.
        var silent = cmd.Autostart || AppHost.NewPackageFacts() is { IsPackaged: true, StartedByStartupTask: true };
        var files = new AppFiles(cmd.DataDir);
        try
        {
            _host = AppHost.Start(files, quit: () => Shutdown(0), summonOnStart: !silent);
        }
        catch (Exception ex)
        {
            // A start that fails after the single-instance lock was taken would otherwise leave a process with no window holding it (the app ends only by Shutdown).
            files.Log("the start failed: " + ex.GetType().Name);
            Shutdown(ExitCodes.StartFailed);
            return;
        }

        if (_host is null) Shutdown(ExitCodes.AlreadyRunning); // the running copy has been told and shows ALREADY_RUNNING
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        // The generated Main returns nothing, so the exit code is passed on explicitly.
        Environment.ExitCode = e.ApplicationExitCode;
        base.OnExit(e);
    }
}
