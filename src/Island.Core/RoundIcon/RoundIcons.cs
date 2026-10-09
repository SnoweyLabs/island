using System.Runtime.CompilerServices;

namespace Island.Core;

/// <summary>What a tile draws for an icon (WORK-ORDER-10 §1): the plan (disc colour, letters or not) and the picture to lay on the disc.</summary>
/// <param name="Plan">The decision of <see cref="RoundIconRule"/>.</param>
/// <param name="Drawn">The picture drawn on the disc: the icon as it is, or the icon turned white when it is a flat shape.</param>
/// <param name="ClosedDisc">The grey disc of a closed pick: <see cref="RoundIcons.Grey"/> of the disc, darker or lighter in the same steps when the grey picture would not stay visible on it.</param>
public sealed record RoundIconLook(RoundIconPlan Plan, IconImage Drawn, RoundIconColour ClosedDisc);

/// <summary>
/// The look of an icon, made once and remembered, like <see cref="GreyIcons"/>: the icon cache calls <see cref="Of"/> when the icon arrives, off the drawing
/// thread, so the drawing thread only ever finds it made (and, if it was not, makes it in a few milliseconds: an icon is at most a few hundred pixels across).
/// The grey copy of the picture a closed pick draws is made with it.
/// </summary>
public static class RoundIcons
{
    private static readonly ConditionalWeakTable<IconImage, RoundIconLook> Made = new();

    public static RoundIconLook Of(IconImage icon) => Made.GetValue(icon, Make);

    private static RoundIconLook Make(IconImage icon)
    {
        var plan = RoundIconRule.Analyse(icon);
        var drawn = plan.Kind == RoundIconKind.Letters ? icon : plan.DrawnWhite ? new IconImage(icon.Width, icon.Height, RoundIconRule.Whiten(icon.Bgra)) : icon;
        var closed = ClosedDiscFor(plan.Disc, GreyIcons.Of(drawn));
        return new RoundIconLook(plan, drawn, closed);
    }

    // Disc and picture are both reduced to brightness on a closed tile, so two colours of one brightness would come out as one grey: the grey disc steps away until the picture is told from it.
    private static RoundIconColour ClosedDiscFor(RoundIconColour disc, IconImage grey)
    {
        var start = Grey(disc);
        var lighter = start.R < RoundIconConstants.DarkDiscMaxChannel;
        var best = start;
        var bestVisible = -1;
        for (var step = 0; step <= RoundIconConstants.MaxDiscSteps; step++)
        {
            var candidate = Step(start, step * RoundIconConstants.DiscStepPercent, lighter);
            var (visible, opaque) = Count(candidate.R, grey.Bgra);
            if ((long)visible * 100 >= (long)RoundIconConstants.VisiblePercent * opaque) return candidate;
            if (visible > bestVisible)
            {
                bestVisible = visible;
                best = candidate;
            }
        }

        return best;
    }

    private static RoundIconColour Step(RoundIconColour c, int percent, bool lighter)
    {
        var v = lighter ? (byte)(c.R + ((255 - c.R) * percent + 50) / 100) : (byte)((c.R * (100 - percent) + 50) / 100);
        return new RoundIconColour(v, v, v);
    }

    private static (int Visible, int Opaque) Count(byte disc, byte[] bgra)
    {
        int visible = 0, opaque = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < RoundIconConstants.OpaqueAlphaLine) continue;
            opaque++;
            if (Math.Abs(bgra[i] - disc) >= RoundIconConstants.ClosedVisibleDifference) visible++;
        }

        return (visible, opaque);
    }

    /// <summary>The disc of a closed pick: no colour, a little darker, by the same step the closed picture takes (<see cref="GreyIcons"/>).</summary>
    public static RoundIconColour Grey(RoundIconColour disc)
    {
        var luma = 0.114 * disc.B + 0.587 * disc.G + 0.299 * disc.R;
        var grey = (byte)Math.Clamp(Math.Round(luma * ChoiceConstants.ClosedBrightness), 0, 255);
        return new RoundIconColour(grey, grey, grey);
    }
}
