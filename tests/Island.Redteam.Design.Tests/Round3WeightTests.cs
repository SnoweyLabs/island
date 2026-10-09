using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 3: the glow's outside part (what the old bloom shows) against what the cut shows, as weight: the leak of the cut (design-3-1) counted in the same unit as round 2's 47.9%.</summary>
public class Round3WeightTests(ITestOutputHelper output)
{
    private static double Blurred(double depth, double width)
    {
        double sigma = LookConstants.BloomBlurCss, centre = LookConstants.RimInset, sum = 0;
        for (var u = centre - width / 2; u <= centre + width / 2; u += 0.05)
        {
            var z = (depth - u) / sigma;
            sum += Math.Exp(-0.5 * z * z) / (sigma * Math.Sqrt(2 * Math.PI)) * 0.05;
        }

        return sum;
    }

    private static double Glow(double depth) => LookConstants.BloomLayerAlpha * (Blurred(depth, LookConstants.BloomArcWidth) + LookConstants.BloomBaseAlpha * Blurred(depth, LookConstants.BloomBaseWidth));

    [Fact]
    public void Record_What_A_Cut_That_Lies_Inside_The_Outline_Adds_To_The_Outside_Weight()
    {
        double Mass(double from, double to) { var s = 0.0; for (var d = from; d < to; d += 0.05) s += Glow(d) * 0.05; return s; }
        var outside = Mass(-60, 0);
        output.WriteLine($"old bloom: weight outside the outer outline {outside:0.00} (dip x share of the page colour), the whole glow {outside + Mass(0, 60):0.00} ({outside / (outside + Mass(0, 60)):P1} outside)");
        foreach (var (scale, px) in new[] { (1.0, 0.5), (1.0, 1.0), (1.5, 0.75), (1.5, 1.0), (1.75, 1.5), (2.0, 1.0) })
        {
            var dip = px / scale;
            var extra = Mass(0, dip);
            var seen = Glow(dip / 2) * (1 - LookConstants.GlassBaseAlpha);
            output.WriteLine($"scale {scale:0.00}, cut {px:0.00} px (= {dip:0.00} dip) inside the outline: +{extra:0.00} = {extra / outside:P1} of the outside weight; in that band the glow is {Glow(dip / 2):0.000} of the page colour, {seen:0.000} of it through the glass (Approved)");
        }

        Assert.True(Glow(0) > 0.4);
    }
}
