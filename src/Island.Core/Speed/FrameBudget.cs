namespace Island.Core.Speed;

/// <summary>
/// WORK-ORDER-12 section 2, the first defect: the island drew more often than the screen refreshes. WPF calls the frame callback not only once per frame
/// but also whenever changes to the visual tree force the composition tree to update (Microsoft Learn, CompositionTarget.Rendering), and a window that Windows draws
/// without the graphics card is not paced by the screen, so a cheap frame was followed at once by the next call (1178 frames in 8 seconds where the screen refreshes
/// about 670 times). The budget lets a call draw only when the screen could have shown a new frame: a small bucket that fills at one frame per refresh interval of the
/// screen the island is on and is spent by each frame drawn, so that over any stretch the frames drawn never exceed the refreshes of the screen in it by more than
/// the bucket's size, and a frame that arrives a little early or late is not lost (the average rate is the screen's, not a lower one).
/// </summary>
public sealed class FrameBudget
{
    /// <summary>The most a bucket holds, in frames (Claude): a burst never exceeds this, so over a stretch the excess over the refreshes is at most this.</summary>
    public const double Capacity = 1.25;

    /// <summary>A frame is drawn when the bucket holds at least this much (Claude): a call that comes a quarter of an interval early is still the frame of the refresh it belongs to.</summary>
    public const double Threshold = 0.75;

    private double _tokens = Capacity;
    private double _lastMs = double.NaN;

    /// <summary>
    /// True when a frame may be drawn at <paramref name="nowMs"/>. <paramref name="intervalMs"/> is the time one frame takes on the screen (the refresh interval, or
    /// twice it for the half-rate light at rest); 0 or less means the rate is not known and every call is let through.
    /// </summary>
    public bool Admit(double nowMs, double intervalMs)
    {
        if (!(intervalMs > 0) || !double.IsFinite(nowMs))
        {
            _lastMs = double.NaN;
            return true;
        }

        if (double.IsNaN(_lastMs) || nowMs < _lastMs) _lastMs = nowMs; // the first call, or a clock that went back: nothing is added
        _tokens = Math.Min(Capacity, _tokens + (nowMs - _lastMs) / intervalMs);
        _lastMs = nowMs;
        if (_tokens < Threshold) return false;
        _tokens -= 1;
        return true;
    }

    /// <summary>Forgets what was spent (the island came back, or moved to another screen).</summary>
    public void Reset()
    {
        _tokens = Capacity;
        _lastMs = double.NaN;
    }
}
