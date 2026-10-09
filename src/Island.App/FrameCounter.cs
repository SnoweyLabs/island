using System.Windows.Media;

namespace Island.App;

/// <summary>
/// Counts rendered frames. The frame callback can fire more than once for the same
/// frame, so frames are told apart by the frame's own rendering time.
/// </summary>
internal sealed class FrameCounter : IDisposable
{
    private TimeSpan _last = TimeSpan.MinValue;

    public FrameCounter() => CompositionTarget.Rendering += OnRendering;

    public long Count { get; private set; }

    private void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _last) return;
        _last = time;
        Count++;
    }

    public void Dispose() => CompositionTarget.Rendering -= OnRendering;
}
