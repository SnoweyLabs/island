using Island.Core;

namespace Island.Sources.Folders;

/// <summary>
/// Reads the open File Explorer windows and their tabs. It only LOOKS: it never opens, closes, activates or
/// navigates a window. Everything runs on one dedicated STA thread (Shell COM from an MTA thread gives null
/// handles). The expensive read happens only while a File Explorer frame exists, every ~2 seconds, and
/// only while the island is not <see cref="SetQuiet">quiet</see> (it is, while hidden: WORK-ORDER-6 section 6 found the read costing
/// about 3% of one core around the clock); <see cref="Changed"/> is raised, on that thread, only when the list differs from the one before.
/// </summary>
public sealed class ExplorerWindowReader : IFolderSource, IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _interval;
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly ManualResetEventSlim _firstRead = new(false);
    private readonly ManualResetEventSlim _wake = new(false);
    private volatile bool _quiet;
    private int _polls;
    private readonly object _startLock = new();
    private Thread? _thread;
    private volatile IReadOnlyList<FolderWindow> _windows = [];

    public ExplorerWindowReader(TimeSpan? interval = null) => _interval = interval ?? DefaultInterval;

    /// <summary>The last good list (empty before the first read), top-most frame first. Safe to read from any thread.</summary>
    public IReadOnlyList<FolderWindow> Windows => _windows;

    /// <summary>Raised on the reader's own thread, not the UI thread. Handlers must be quick and must marshal themselves.</summary>
    public event Action? Changed;

    /// <summary>Starts the reader thread. Calling it twice, or after <see cref="Dispose"/>, does nothing.</summary>
    public void Start()
    {
        lock (_startLock)
        {
            if (_thread is not null || _stop.IsSet) return;
            var thread = new Thread(Run) { IsBackground = true, Name = "Island.FolderReader" };
            thread.SetApartmentState(ApartmentState.STA);
            _thread = thread;
            thread.Start();
        }
    }

    /// <summary>
    /// While quiet the reader does not read at all (nobody is looking: the island is hidden); the list it has is the last one it read.
    /// Not quiet again, it reads at once and then every interval. Safe to call from any thread, any number of times.
    /// </summary>
    public void SetQuiet(bool quiet)
    {
        _quiet = quiet;
        if (!quiet) _wake.Set();
    }

    /// <summary>How many times the reader has looked (for the self-test).</summary>
    public int PollCount => Volatile.Read(ref _polls);

    /// <summary>True once the first read has finished (also when it found nothing).</summary>
    public bool WaitForFirstRead(TimeSpan timeout) => _firstRead.Wait(timeout);

    /// <summary>Stops the thread and waits briefly for it. Safe to call more than once.</summary>
    public void Dispose()
    {
        _stop.Set();
        Thread? thread;
        lock (_startLock) thread = _thread;
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        var match = ReadKnownFolders();
        WaitHandle[] handles = [_stop.WaitHandle, _wake.WaitHandle];
        while (!_stop.IsSet)
        {
            _wake.Reset(); // before the look at _quiet, so a wake that comes after it is never lost
            if (!_quiet)
            {
                Poll(match);
                Interlocked.Increment(ref _polls);
                _firstRead.Set();
            }

            WaitHandle.WaitAny(handles, _quiet ? Timeout.Infinite : (int)_interval.TotalMilliseconds);
        }
    }

    private static FolderMatch ReadKnownFolders()
    {
        try { return new FolderMatch(KnownFolderPaths.Read()); }
        catch (Exception) { return new FolderMatch([]); } // no known folders: every entry is just "not a known folder"
    }

    private void Poll(FolderMatch match)
    {
        IReadOnlyList<FolderWindow> next;
        try
        {
            if (!ExplorerRawRead.AnyFrame()) next = [];
            else if (ExplorerRawRead.Read() is { } raw) next = FolderReading.Build(raw.Entries, raw.TabsByFrame, match);
            else return; // could not read at all: keep the last good list
        }
        catch (Exception)
        {
            return; // never throws into the caller; the last good list stays
        }

        if (next.SequenceEqual(_windows)) return;
        _windows = next;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* a faulty listener must not kill the reader */ }
    }
}
