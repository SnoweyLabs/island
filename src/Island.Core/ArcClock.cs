namespace Island.Core;

/// <summary>
/// Where the moving light is on the rim. A fraction of the outline, a function of time and
/// nothing else: the same moment gives the same place at any frame rate, at any size, during any
/// animation and across a colour change. Fraction 0 is the middle of the top edge and fractions
/// grow clockwise (see <see cref="RoundedPerimeter"/>).
/// </summary>
public static class ArcClock
{
    /// <summary>Head of the first arc at the given time on a monotonic clock, in seconds.</summary>
    public static double Head(double seconds)
    {
        return Wrap(seconds * LookConstants.ArcSpeedPerSecond);
    }

    /// <summary>The fraction of a lap, 0 up to but never 1: a tiny negative number (float noise) is 1.0 after the subtraction and is taken for the start of the lap instead.</summary>
    public static double Wrap(double f)
    {
        var w = f - Math.Floor(f);
        return w >= 1 ? 0 : w;
    }

    /// <summary>Head of the second arc, half a perimeter away from the first.</summary>
    public static double SecondHead(double seconds)
    {
        return Wrap(Head(seconds) + LookConstants.SecondArcPhase);
    }
}
