using Island.Core;

namespace Island.App;

/// <summary>
/// One copy only. The first copy holds a named mutex and listens on two named events: "again" (a
/// second copy started and is about to exit; the running copy shows ALREADY_RUNNING, because a
/// process that has exited cannot show anything) and "quit" (stop.cmd). Under the self-test the names are
/// the self-test's own (<see cref="InstanceNames"/>), so its copies meet each other and never Dan's.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static InstanceNames Names => InstanceNames.For(OutsideGate.Current.SelfTest);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _again;
    private readonly EventWaitHandle _quit;
    private readonly RegisteredWaitHandle[] _waits;
    private readonly SynchronizationContext _ui;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        var names = Names;
        _again = new EventWaitHandle(false, EventResetMode.AutoReset, names.Again);
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, names.Quit);
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _waits =
        [
            ThreadPool.RegisterWaitForSingleObject(_again, (_, _) => _ui.Post(_ => AnotherCopyStarted?.Invoke(), null), null, Timeout.Infinite, false),
            ThreadPool.RegisterWaitForSingleObject(_quit, (_, _) => _ui.Post(_ => QuitRequested?.Invoke(), null), null, Timeout.Infinite, false),
        ];
    }

    /// <summary>Raised on the UI thread when a second copy started and left.</summary>
    public event Action? AnotherCopyStarted;

    /// <summary>Raised on the UI thread when stop.cmd asked the running copy to close.</summary>
    public event Action? QuitRequested;

    /// <summary>Takes ownership if no other copy runs; returns null otherwise.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, Names.Running, out var created);
        if (created) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    /// <summary>Tells the running copy that another one started (or, with quit, to close). False when none is running.</summary>
    public static bool SignalRunningCopy(bool quit)
    {
        if (!EventWaitHandle.TryOpenExisting(quit ? Names.Quit : Names.Again, out var handle)) return false;
        using (handle) handle.Set();
        return true;
    }

    public void Dispose()
    {
        foreach (var w in _waits) w.Unregister(null);
        _again.Dispose();
        _quit.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
