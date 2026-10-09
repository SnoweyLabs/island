using System.Windows.Threading;

namespace Island.App;

/// <summary>
/// Samples the foreground window while the island moves. It keeps only counts and one flag, never
/// which window: whether one of the island's own windows was ever the foreground window, and how
/// many times the foreground window changed (someone else switching windows is not the island's doing).
/// </summary>
internal sealed class FocusWatch : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly IntPtr[] _ours;
    private IntPtr _last = Native.GetForegroundWindow();

    public FocusWatch(params IntPtr[] ourWindows)
    {
        _ours = ourWindows;
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    public bool OurWindowWasForeground { get; private set; }
    public int Changes { get; private set; }

    private void Sample()
    {
        var now = Native.GetForegroundWindow();
        if (now != _last)
        {
            Changes++;
            _last = now;
        }

        if (Array.IndexOf(_ours, now) >= 0) OurWindowWasForeground = true;
    }

    public void Dispose() => _timer.Stop();
}
