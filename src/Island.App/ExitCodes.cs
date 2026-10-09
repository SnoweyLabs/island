namespace Island.App;

/// <summary>Process exit codes. 0 is success; the rest say why the app or the self-test stopped.</summary>
internal static class ExitCodes
{
    public const int SelfTestFailed = 1;
    public const int NoDisplay = 2;
    public const int OtherCopyRunning = 3;

    /// <summary>A second copy was started while one was already running (ALREADY_RUNNING).</summary>
    public const int AlreadyRunning = 4;

    /// <summary>The start failed after the single-instance lock was taken: the process ends so that the lock goes with it.</summary>
    public const int StartFailed = 5;

    /// <summary>--uninstall-cleanup could not take out every helper's lines or the start-up value (or the gate refused it); the uninstaller goes on.</summary>
    public const int CleanupIncomplete = 6;
}
