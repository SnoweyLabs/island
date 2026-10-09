using System.Runtime.InteropServices;
using Island.Core;
using Microsoft.Win32;

namespace Island.Sources.Screens;

/// <summary>
/// Reads the screens (EnumDisplayMonitors, GetMonitorInfoW: full rectangle and work area; GetDpiForMonitor with
/// MDT_EFFECTIVE_DPI: scaling) and the pointer (GetCursorPos). Reading only: no window is created or moved here.
/// The screens are read on a pool thread, never on the caller's, and kept in <see cref="Screens"/>; the pointer is
/// read on demand because the call takes microseconds. It never throws; when Windows will not answer the list keeps
/// its last good value (empty before the first one) and the pointer is null.
///
/// The process must be per-monitor DPI aware (the app's manifest says PerMonitorV2); otherwise Windows hands back
/// virtualised coordinates and a system-wide scaling, and the numbers are wrong on a mixed-scaling desktop.
///
/// Changes: <c>SystemEvents.DisplaySettingsChanged</c> (Microsoft Learn: occurs when the user changes the display
/// settings) and <c>SystemEvents.UserPreferenceChanged</c> (any category; Learn does not say which category carries a
/// work-area change, so every category wakes the reader and only a real difference is reported). Both are static
/// events, so they are detached in <see cref="Dispose"/>. The app can also forward its own window messages
/// (WM_DISPLAYCHANGE, WM_DPICHANGED, WM_SETTINGCHANGE) to <see cref="NotifyPossibleChange"/>.
/// </summary>
public sealed class ScreenReader : IScreenSource, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _firstRead = new(false);
    private IReadOnlyList<ScreenInfo> _screens = [];
    private Timer? _timer;
    private bool _started;
    private bool _disposed;

    /// <summary>The screens as last read; empty until the first read is done or when none could be read.</summary>
    public IReadOnlyList<ScreenInfo> Screens => Volatile.Read(ref _screens);

    /// <summary>The pointer now, in real pixels of the virtual screen; null when Windows will not say (for example on a locked desktop).</summary>
    public ScreenPoint? Pointer => ReadPointer();

    /// <summary>Raised on a pool thread, after a quiet moment, when the list of screens or a work area or a scaling is not what it was.</summary>
    public event Action? Changed;

    /// <summary>Starts listening and schedules the first read. Calling it again does nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            Subscribe();
            _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Waits until the first read is done (also when it found nothing). False on timeout.</summary>
    public bool WaitForFirstRead(TimeSpan timeout) => _firstRead.Wait(timeout);

    /// <summary>Asks for a fresh read after the quiet moment; cheap to call often (the app may call it from its own window messages).</summary>
    public void NotifyPossibleChange()
    {
        lock (_gate)
        {
            if (!_started || _disposed) return;
            _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_started) Unsubscribe();
            _timer?.Dispose();
        }
    }

    private void Subscribe()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged += OnSystemEvent;
            SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        }
        catch (Exception e) when (e is InvalidOperationException or ExternalException)
        {
            // No system events in this context: the first read still works and the app can call NotifyPossibleChange.
        }
    }

    private void Unsubscribe()
    {
        try
        {
            SystemEvents.DisplaySettingsChanged -= OnSystemEvent;
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        }
        catch (Exception e) when (e is InvalidOperationException or ExternalException)
        {
            // Nothing was attached.
        }
    }

    private void OnSystemEvent(object? sender, EventArgs e) => NotifyPossibleChange();

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => NotifyPossibleChange();

    private void Refresh()
    {
        try
        {
            var read = ReadScreens();
            var before = Screens;
            // An empty answer after a good one is a failed read, not a desktop with no screens.
            var changed = read.Count > 0 && !read.SequenceEqual(before);
            if (changed) Volatile.Write(ref _screens, read);
            if (changed) Changed?.Invoke(); // before the first-read signal, so a waiter has seen the event
        }
        catch (Exception)
        {
            // A reader that cannot read stays quiet; it must never take the process down from a timer thread.
        }
        finally
        {
            _firstRead.Set();
        }
    }

    /// <summary>One reading of every screen; empty when Windows would not enumerate them.</summary>
    internal static IReadOnlyList<ScreenInfo> ReadScreens()
    {
        var found = new List<ScreenInfo>();
        Native.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            if (ReadOne(monitor) is { } screen) found.Add(screen);
            return true; // keep going
        };

        try
        {
            return Native.EnumDisplayMonitors(0, 0, callback, 0) ? found : [];
        }
        finally
        {
            GC.KeepAlive(callback);
        }
    }

    private static ScreenInfo? ReadOne(nint monitor)
    {
        var info = new Native.MonitorInfo { CbSize = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return null;
        return new ScreenInfo(
            new PixelRect(info.RcMonitor.Left, info.RcMonitor.Top, info.RcMonitor.Right, info.RcMonitor.Bottom),
            new PixelRect(info.RcWork.Left, info.RcWork.Top, info.RcWork.Right, info.RcWork.Bottom),
            ScaleOf(monitor),
            (info.DwFlags & Native.MonitorInfoFPrimary) != 0);
    }

    /// <summary>The scaling of one monitor (effective DPI over 96); 1.0 when Windows will not say.</summary>
    private static double ScaleOf(nint monitor)
    {
        const int sOk = 0;
        var hr = Native.GetDpiForMonitor(monitor, Native.MdtEffectiveDpi, out var dpiX, out _);
        return hr == sOk && dpiX > 0 ? dpiX / Native.DefaultDpi : 1.0;
    }

    private static ScreenPoint? ReadPointer() =>
        Native.GetCursorPos(out var p) ? new ScreenPoint(p.X, p.Y) : null;
}
