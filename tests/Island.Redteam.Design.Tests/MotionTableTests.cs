using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// The curves the island's movements follow, at the same moments: which of them are meant to be "ease in and out", and how far apart they are (EVALS M asks for the same path every time, not for one curve;
/// this records which curve each movement has, so that round 2 and Snowey can see them in one table).
/// </summary>
public class MotionTableTests(ITestOutputHelper output)
{
    private static double Smooth(double x) => x * x * (3 - 2 * x);                          // ModeMark.Blend
    private static double Cosine(double x) => 0.5 - 0.5 * Math.Cos(Math.PI * x);            // ModeMark.Breath (half a period)
    private static double CssEaseInOut(double x) => Easing.CubicBezier(0.42, 0, 0.58, 1, x); // Equalizer
    private static double Linear(double x) => x;                                            // ColourTransition

    [Fact]
    public void The_Curves_At_A_Quarter_Half_And_Three_Quarters_Are_Recorded()
    {
        (string Name, Func<double, double> F)[] curves =
        [
            ("colour change (ColourTransition): linear", Linear),
            ("mode cross-fade (ModeMark.Blend): smoothstep", Smooth),
            ("Vibe breath (ModeMark.Breath): cosine", Cosine),
            ("playing bars (Equalizer): CSS ease-in-out", CssEaseInOut),
            ("contents opacity (Easing.Fade)", Easing.Fade),
            ("contents rise and scale (Easing.Move)", Easing.Move),
            ("contents blur (Easing.Ease): CSS ease", Easing.Ease),
        ];
        foreach (var (name, f) in curves) output.WriteLine($"{name,-52} {f(0.25),6:0.000} {f(0.5),6:0.000} {f(0.75),6:0.000}");
        // The three symmetric in-and-out curves differ from each other by at most this much at any point of the way.
        double Max(Func<double, double> a, Func<double, double> b) => Enumerable.Range(0, 101).Max(i => Math.Abs(a(i / 100.0) - b(i / 100.0)));
        output.WriteLine($"smoothstep against CSS ease-in-out: largest difference {Max(Smooth, CssEaseInOut):0.000}");
        output.WriteLine($"cosine against CSS ease-in-out: largest difference {Max(Cosine, CssEaseInOut):0.000}");
        output.WriteLine($"linear against smoothstep (the colour change against the mode cross-fade, both {LookConstants.ColorChangeMs} ms): largest difference {Max(Linear, Smooth):0.000}");
        Assert.InRange(Max(Smooth, CssEaseInOut), 0, 0.06);
        Assert.InRange(Max(Cosine, CssEaseInOut), 0, 0.06);
        Assert.InRange(Max(Linear, Smooth), 0.05, 0.2);
        Assert.Equal(LookConstants.ColorChangeMs, ModeMark.CrossFadeMs);
    }

    /// <summary>The settings screen's step dots in the reference have <c>transition: background .3s</c>; the app rebuilds the dots at each step (no transition). Recorded as written.</summary>
    [Fact]
    public void The_Step_Dots_Of_The_Reference_Fade_And_The_Dots_Of_The_App_Do_Not()
    {
        var reference = Src.Reference("island-setup-previews.html");
        var step = Src.Read("Island.SettingsUi/StepIsland.cs");
        Assert.DoesNotContain("BeginAnimation", step);
        Assert.DoesNotContain("Storyboard", step);
        if (reference is null) return;
        Assert.Contains(".c-dots i{width:22px;height:6px;border-radius:3px;background:rgba(255,255,255,.2);transition:background .3s}", reference);
    }
}
