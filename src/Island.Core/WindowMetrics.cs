namespace Island.Core;

/// <summary>
/// Size of the fixed window the island lives in, computed from the look so that
/// nothing is ever clipped: the widest capsule at the peak of its spring
/// overshoot, plus the full reach of the drop shadow and the bloom.
/// All values are device-independent pixels.
/// </summary>
public static class WindowMetrics
{
    /// <summary>A Gaussian is visible out to about this many standard deviations.</summary>
    private const double SigmasOfReach = 3;

    /// <summary>Spare pixels so rounding never clips the last faint pixel.</summary>
    private const double Margin = 4;

    /// <summary>CSS box-shadow blur is twice the standard deviation.</summary>
    public static double ShadowSigma => LookConstants.ShadowCssBlur / 2;

    /// <summary>How far the shadow's visible part reaches beyond the shape on each side.</summary>
    public static double ShadowReach => SigmasOfReach * ShadowSigma;

    /// <summary>CSS filter blur is the standard deviation itself; the bloom stroke also has half its width.</summary>
    public static double BloomReach =>
        SigmasOfReach * LookConstants.BloomBlurCss + LookConstants.BloomArcWidth / 2;

    /// <summary>The widest capsule any page can take: the most picks shown at once plus the + tile, with the Media page's controls (WORK-ORDER-5).</summary>
    public static double LargestCapsuleWidth => CapsuleLayout.Width(ChoiceConstants.MaxVisibleTiles + 1, isMedia: true);

    private static double? _widestCapsule;

    /// <summary>
    /// Sets the widest capsule the pages will ever produce (from the picks), once, when the app starts, before
    /// anything reads the window size. The window is never resized afterwards.
    /// </summary>
    public static bool TryConfigure(double widestCapsule)
    {
        if (_widestCapsule is not null || _sized) return false;
        _widestCapsule = widestCapsule;
        return true;
    }

    private static bool _sized;

    private static double WidestCapsule => _widestCapsule ?? Pages.AllPlaceholders.Max(c => CapsuleLayout.SizeFor(c).Width);

    /// <summary>Widest capsule width over all pages at the peak of the spring overshoot.</summary>
    public static double PeakCapsuleWidth => Peak(WidestCapsule, LookConstants.BallSize);

    /// <summary>Capsule height at the peak of the spring overshoot.</summary>
    public static double PeakCapsuleHeight => Peak(LookConstants.CapsuleHeight, LookConstants.BallSize);

    private static double? _width;
    private static double? _height;

    public static double Width => _width ??= Fix(Math.Ceiling(PeakCapsuleWidth + 2 * Math.Max(ShadowReach, BloomReach) + Margin));

    /// <summary>Capsule height at the peak of the spring overshoot when it grows to two rows (WORK-ORDER-5 §5).</summary>
    public static double PeakTwoRowHeight => Peak(ChoiceConstants.TwoRowHeight, LookConstants.BallSize);

    /// <summary>
    /// The window holds the largest shape any page can take: seven picks, the + tile and the Media controls wide; two rows high
    /// (150) with the overshoot of the springs; the shadow; and under the one-row capsule the drop zone of a drag (WORK-ORDER-5 §6),
    /// which fits inside that height.
    /// </summary>
    public static double Height => _height ??= Fix(Math.Ceiling(Math.Max(
        LookConstants.TopGap + PeakTwoRowHeight + Math.Max(ShadowReach + LookConstants.ShadowOffsetY, BloomReach) + Margin,
        LookConstants.TopGap + LookConstants.CapsuleHeight + 8 + 40 + Margin)));

    private static double Fix(double size)
    {
        _sized = true;
        return size;
    }

    /// <summary>Highest value a spring reaches on its way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static double Peak(double to, double from)
    {
        var s = new Spring(from, 0, to);
        var max = from;
        for (var frame = 0; frame < 60 * 5; frame++)
        {
            s = s.Frame(1.0 / 60);
            max = Math.Max(max, s.Value);
        }

        return max;
    }
}
