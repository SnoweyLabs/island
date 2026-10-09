namespace Island.Core;

/// <summary>
/// Where the moving light is drawn (WORK-ORDER-12 section 2). It is <see cref="ArcClock"/>'s place at the moment asked, except for the fixed-rate kind while nothing but the edge
/// moves: then the place the light was last drawn at is held until 1000 / <see cref="FixedRateHz"/> milliseconds have passed, so the rim and the glow behind it are drawn at most
/// that many times a second (Dan's P25; 40 is Claude's number). Whatever else moves (the capsule's own movement, a spring, a page change, a playing tile) is not touched: while any of it moves the light has its exact place
/// on every frame. The place is always a point on the clock, never a made-up one, so the light does not drift from where it would be.
/// </summary>
public sealed class LightClock
{
    /// <summary>How many times a second the fixed-rate light's place changes (Claude): smooth enough for a slow lap of the edge, a quarter of the work on a 165 Hz screen.</summary>
    public const double FixedRateHz = 40;

    public const double FixedRateIntervalMs = 1000.0 / FixedRateHz;

    private double _heldAtMs = double.NegativeInfinity;
    private double _held;

    /// <summary>The head of the first arc at <paramref name="nowMs"/> (a monotonic clock, in milliseconds).</summary>
    /// <param name="refreshHz">The screen's refresh rate; 0 when not known (the light then has its exact place on every frame).</param>
    /// <param name="onlyEdgeMoves">True while nothing but the light and the edge's own look moves.</param>
    public double Head(double nowMs, LightKind kind, double refreshHz, bool onlyEdgeMoves)
    {
        if (!double.IsFinite(nowMs)) return _held; // a time that is not a number is not a moment: the last place stays, and nothing is stored from it
        // a screen that refreshes no faster than the fixed rate gets the exact place on every frame
        if (kind != LightKind.FixedRate || !onlyEdgeMoves || !(refreshHz > FixedRateHz * 1.1)) return Exact(nowMs);
        if (nowMs < _heldAtMs || nowMs - _heldAtMs >= FixedRateIntervalMs - 1e-9) return Exact(nowMs);
        return _held;
    }

    private double Exact(double nowMs)
    {
        _heldAtMs = nowMs;
        _held = ArcClock.Head(nowMs / 1000.0);
        return _held;
    }
}
