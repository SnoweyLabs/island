namespace Island.Core;

/// <summary>
/// The page colour changing over 350 ms, linear in sRGB (how the preview's registered colour
/// property transitions). Retargeting mid-change starts from the colour on screen; a snap (used when
/// the island is summoned from hidden, so the ball arrives already lit) has no transition at all.
/// </summary>
public sealed class ColourTransition(Rgb initial)
{
    private Rgb _from = initial;
    private Rgb _to = initial;
    private double _startMs;

    public Rgb Target => _to;

    public Rgb At(double nowMs)
    {
        var t = Math.Clamp((nowMs - _startMs) / LookConstants.ColorChangeMs, 0, 1);
        return ColorMath.LerpSrgb(_from, _to, t);
    }

    /// <summary>Changes the colour at the given moment, with or without the transition.</summary>
    public void Retarget(Rgb to, double nowMs, bool snap)
    {
        if (to == _to) return;
        _from = snap ? to : At(nowMs);
        _to = to;
        _startMs = nowMs;
    }
}
