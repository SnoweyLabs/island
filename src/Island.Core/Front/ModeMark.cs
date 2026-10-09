namespace Island.Core;

/// <summary>
/// What the edge of the island and of the notice looks like in each mode (WORK-ORDER-7 section 1, choice 11C "the edge itself"), as plain
/// numbers that depend only on the mode and on time. Focus is the approved edge, exactly. Vibe: the soft glow behind the edge breathes,
/// from its approved strength down to 40% of it and back once every 2.2 s, easing in and out. DND: no page colour on the edge; the rim
/// is a dashed white line, 1.6 thick at 55%, with no moving light and no glow behind. The small pill can stay for hours, so nothing breathes
/// there: in Vibe its glow stands still at the low end of the breath.
/// </summary>
public static class ModeMark
{
    public const double BreathSeconds = 2.2;

    /// <summary>The low end of the breath, as a share of the approved glow (Claude, from the picture's 12px at 50% down to a 3px at 30%).</summary>
    public const double BreathLow = 0.4;

    public const double DashedRimWidth = 1.6;
    public const double DashedRimAlpha = 0.55;

    /// <summary>Dash and gap, in widths of the line (Claude: a browser's dashed border is about this).</summary>
    public const double DashLength = 3;
    public const double DashGap = 2;

    /// <summary>A change of mode while the island is visible cross-fades over this long: the time the approved look uses for a colour change.</summary>
    public const double CrossFadeMs = 350;

    /// <summary>The look of the edge: how much of each part is drawn, 0 to 1 (the glow's strength is a share of the approved one).</summary>
    public readonly record struct Look(double Glow, double MovingLight, double PageRim, double DashedRim)
    {
        public static Look Approved { get; } = new(1, 1, 1, 0);

        public static Look Lerp(Look from, Look to, double t) => new(
            from.Glow + (to.Glow - from.Glow) * t,
            from.MovingLight + (to.MovingLight - from.MovingLight) * t,
            from.PageRim + (to.PageRim - from.PageRim) * t,
            from.DashedRim + (to.DashedRim - from.DashedRim) * t);
    }

    /// <summary>
    /// The look of a mode at a moment. <paramref name="seconds"/> is the island's own clock; it only matters in Vibe on the capsule or the
    /// notice. <paramref name="isPill"/> is the small pill: nothing breathes. A mode that is not one of the three reads as Focus.
    /// </summary>
    public static Look LookOf(Mode mode, double seconds, bool isPill = false) => mode switch
    {
        Mode.Vibe => new Look(isPill ? BreathLow : Breath(seconds), 1, 1, 0),
        Mode.DND => new Look(0, 0, 0, 1),
        _ => Look.Approved,
    };

    /// <summary>1 down to <see cref="BreathLow"/> and back once every <see cref="BreathSeconds"/>, eased: starts at 1 and depends only on time. A time that is not a number reads as 0.</summary>
    public static double Breath(double seconds)
    {
        var t = double.IsFinite(seconds) ? seconds : 0;
        var phase = t % BreathSeconds / BreathSeconds * 2 * Math.PI; // the breath repeats, so a huge time is the same as its remainder
        var down = 0.5 - 0.5 * Math.Cos(phase); // 0 at the start, 1 half a period later: ease in, ease out
        return 1 - (1 - BreathLow) * down;
    }

    /// <summary>
    /// The look while changing from one mode to another: <paramref name="sinceChangeMs"/> after the change, a smooth step over <see cref="CrossFadeMs"/>.
    /// </summary>
    public static Look Blend(Look from, Look to, double sinceChangeMs)
    {
        if (!double.IsFinite(sinceChangeMs) || sinceChangeMs >= CrossFadeMs) return to;
        if (sinceChangeMs <= 0) return from;
        var x = sinceChangeMs / CrossFadeMs;
        return Look.Lerp(from, to, x * x * (3 - 2 * x));
    }
}
