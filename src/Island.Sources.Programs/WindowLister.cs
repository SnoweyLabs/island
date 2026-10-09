using Island.Core;

namespace Island.Sources.Programs;

/// <summary>
/// The open windows, as Alt+Tab would list them (<see cref="WindowRules"/>). All reading happens on one thread of
/// its own that also runs the message loop the window-event hook needs. A burst of events starts one timer;
/// when it fires there is one full re-enumeration (so events are throttled, not endlessly postponed), and a
/// slow timer re-reads anyway because events can be lost (Research/windows-apis.md section 7). Titles live in
/// memory only. Nothing here throws into the caller.
/// </summary>
public sealed class WindowLister : IOpenWindowSource, IDisposable
{
    private readonly int _throttleMs;
    private readonly int _reconcileMs;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _firstRead = new(false);
    private readonly Native.WinEventProc _onEvent; // kept in a field: the hook calls it for as long as it lives
    private volatile IReadOnlyList<OpenWindow> _windows = [];
    private uint _threadId;
    private nint _throttleTimer;
    private nint _reconcileTimer;
    private bool _pending;

    public WindowLister(int throttleMs = 150, int reconcileMs = 5000)
    {
        _throttleMs = throttleMs;
        _reconcileMs = reconcileMs;
        _onEvent = OnEvent;
        _thread = new Thread(Run) { IsBackground = true, Name = "Island windows" };
        _thread.Start();
    }

    public IReadOnlyList<OpenWindow> Windows => _windows;

    /// <summary>Raised on the lister's own thread, only when the list actually changed.</summary>
    public event Action? Changed;

    /// <summary>How many of the five window-event hooks Windows accepted (for a console check; 5 when all is well).</summary>
    public int HooksInstalled { get; private set; }

    /// <summary>For a console check: waits until the first full read is done.</summary>
    public bool WaitForFirstRead(TimeSpan timeout) => _firstRead.Wait(timeout);

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessageW(_threadId, Native.WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _threadId = Native.GetCurrentThreadId();
        var hooks = new[]
        {
            Native.EventSystemForeground, Native.EventObjectShow, Native.EventObjectHide,
            Native.EventObjectDestroy, Native.EventObjectNameChange,
        }.Select(e => Native.SetWinEventHook(e, e, 0, _onEvent, 0, 0, Native.WinEventOutOfContext)).ToList();

        HooksInstalled = hooks.Count(h => h != 0);
        try
        {
            Refresh();
            _reconcileTimer = Native.SetTimer(0, 0, (uint)_reconcileMs, 0);
            MessageLoop();
        }
        finally
        {
            foreach (var hook in hooks.Where(h => h != 0)) Native.UnhookWinEvent(hook);
        }
    }

    private void MessageLoop()
    {
        while (Native.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.Message == Native.WmTimer && msg.Hwnd == 0 && (msg.WParam == _throttleTimer || msg.WParam == _reconcileTimer))
            {
                if (msg.WParam == _throttleTimer)
                {
                    Native.KillTimer(0, _throttleTimer);
                    _pending = false;
                }

                Refresh();
                continue;
            }

            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint timeMs)
    {
        // Only whole windows count (OBJID_WINDOW, CHILDID_SELF).
        if (hwnd == 0 || idObject != 0 || idChild != 0 || _pending) return;
        _pending = true;
        _throttleTimer = Native.SetTimer(0, 0, (uint)_throttleMs, 0);
    }

    private void Refresh()
    {
        try
        {
            var next = WindowReader.Read();
            if (!_windows.SequenceEqual(next))
            {
                _windows = next;
                Changed?.Invoke();
            }
        }
        catch (Exception)
        {
            // Fail soft: keep the last good list; the next event or the slow timer tries again.
        }
        finally
        {
            _firstRead.Set();
        }
    }
}
