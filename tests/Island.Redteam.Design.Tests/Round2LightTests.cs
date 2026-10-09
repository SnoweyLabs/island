using System.Text.RegularExpressions;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 2, part 2: the graphics-card light against the old one, by numbers and by the source (nothing can be photographed: no window is shown and no screen is captured). What would differ on screen is written
/// into the report; these tests are what it rests on.
/// </summary>
public class Round2LightTests(ITestOutputHelper output)
{
    private static string Between(string text, string from, string to)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, "not found: " + from);
        var j = text.IndexOf(to, i, StringComparison.Ordinal);
        return j < 0 ? text[i..] : text[i..j];
    }

    private static HashSet<string> Constants(string text) =>
        [.. Regex.Matches(text, @"\b(LookConstants|ModeMark|PillLayout|NoticeLayout)\.(\w+)").Select(m => m.Groups[1].Value + "." + m.Groups[2].Value)];

    /// <summary>Every number of the old rim and bloom (their two classes in <c>Layers.cs</c>) is in <c>LightSpec</c> too, except what the old rim does for the pill, the notice and Do not disturb (the old drawing is kept for them) and the rim's own blur (code-1-15, a proposal).</summary>
    [Fact]
    public void The_Constants_Of_The_Old_Rim_And_Bloom_Are_All_In_The_Lights_Spec_Except_The_Ones_Listed()
    {
        var layers = Src.Read("Island.App/Visuals/Layers.cs");
        var spec = Src.Read("Island.Core/Light/LightSpec.cs");
        var old = Constants(Between(layers, "internal sealed class BloomLayer", "/// <summary>The glass fill")).Union(Constants(Between(layers, "internal sealed class RimLayer", "\n}\n"))).ToHashSet();
        var now = Constants(spec);
        var missing = old.Except(now).OrderBy(x => x).ToList();
        output.WriteLine("in the old drawing and not in LightSpec: " + string.Join(", ", missing));
        var forOtherDrawings = new[] { "ModeMark.DashGap", "ModeMark.DashLength", "ModeMark.DashedRimAlpha", "ModeMark.DashedRimWidth", "NoticeLayout.EdgeWidth", "PillLayout.PlayedAlpha", "PillLayout.RingThickness" };
        Assert.Empty(missing.Except(forOtherDrawings)); // LookConstants.FrontRimBlurCss is in the spec since WORK-ORDER-13 (Dan's P23)
    }

    /// <summary>The old drawing gives the numbers; the spec for the approved look is the same numbers (a second look from the one the mode tests pin).</summary>
    [Fact]
    public void The_Spec_Of_The_Approved_Look_Carries_The_Old_Numbers()
    {
        var spec = LightSpec.For(new Rgb(230, 60, 80), ModeMark.Look.Approved)!;
        Assert.Equal(LookConstants.RimInset, spec.Inset);
        Assert.Equal(LookConstants.BaseRimWidth, spec.BaseRim!.Value.Width);
        Assert.Equal(LookConstants.BaseRimAlpha, spec.BaseRim!.Value.Alpha);
        Assert.Equal(LookConstants.ArcWidth, spec.FirstArc!.Value.Stroke.Width);
        Assert.Equal(LookConstants.ArcFraction, spec.FirstArc!.Value.Fraction);
        Assert.Equal(LookConstants.SecondArcWidth, spec.SecondArc!.Value.Stroke.Width);
        Assert.Equal(LookConstants.SecondArcAlpha, spec.SecondArc!.Value.Stroke.Alpha);
        Assert.Equal(LookConstants.SecondArcPhase, spec.SecondArc!.Value.EndAheadOfHead);
        Assert.Equal(LookConstants.BloomBaseWidth, spec.BloomRing.Width);
        Assert.Equal(LookConstants.BloomArcWidth, spec.BloomArc!.Value.Stroke.Width);
        Assert.Equal(LookConstants.BloomBlurCss, spec.BloomBlurSigma);
        Assert.Equal(LookConstants.BloomLayerAlpha, spec.GlowOpacity);
        Assert.Equal(LookConstants.ArcSpeedPerSecond, spec.SpeedPerSecond);
    }

    // ---- where the glow is drawn ---------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The old bloom is in the group of the shadow window's root, and that root is clipped to everything but the capsule's outer outline (<c>IslandView.Apply</c>): the bloom is drawn outside the outline only.</summary>
    [Fact]
    public void The_Old_Bloom_Is_Drawn_Only_Outside_The_Capsules_Outline_Because_Its_Windows_Root_Is_Clipped_By_A_Hole()
    {
        var view = Src.Read("Island.App/Visuals/IslandView.cs");
        Assert.Matches(@"_backGroup\.Children\.Add\(shadow\);\s*_backGroup\.Children\.Add\(bloom\);\s*backRoot\.Children\.Add\(_backGroup\);", view);
        Assert.Matches(@"_backRoot\.Clip\s*=\s*new CombinedGeometry\(GeometryCombineMode\.Exclude,\s*new RectangleGeometry\(new Rect\(0, 0, _width, _height\)\), hole\);", view);
        Assert.Matches(@"var hole = frame\.Outline\(\);", view);
        var frame = Src.Read("Island.App/Visuals/ShapeFrame.cs");
        Assert.Contains("The outer outline of the shape.", frame);
    }

    private static bool CutsItsGlowToTheOutside(string movingLight) =>
        Regex.IsMatch(movingLight, @"CompositionGeometricClip|CreateGeometricClip|CreateMaskBrush|CompositionMaskBrush|\.Clip\s*=|InsetClip|CutOutRoundedRectangle");

    /// <summary>
    /// The graphics-card light's glow is one blurred sprite the size of the window with nothing that cuts it: no clip, no mask. Its inner half (see the numbers below) therefore lies inside the capsule, under the glass
    /// fill, where the old bloom is cut out. Finding DESIGN-2-1.
    /// </summary>
    [Fact]
    public void Defect_The_Graphics_Light_Glow_Is_Cut_Out_Of_The_Capsules_Inside_As_The_Old_Bloom_Is()
    {
        Assert.True(CutsItsGlowToTheOutside(Src.Read("Island.Glass/MovingLight.cs")), "MovingLight.cs has no clip and no mask: the glow is drawn on both sides of the outline");
    }

    [Fact]
    public void The_Graphics_Light_Glow_Window_Has_A_Hole_In_The_Shape_Of_The_Outline_In_Its_Source()
    {
        var text = Src.Read("Island.Glass/MovingLight.cs");
        // Repaired (design-2-1, WORK-ORDER-12 section 4; this test stated "no clip and no mask anywhere" before): the glow's window has a hole in the shape of the outline.
        Assert.True(CutsItsGlowToTheOutside(text));
        Assert.Contains("SetWindowRgn", Src.Read("Island.Glass/GlassWindow.cs"));
    }

    /// <summary>A Gaussian of standard deviation sigma, integrated by steps (no erf in the library): the weight of a stroke of the given width, centred <paramref name="centre"/> inside the outer edge, seen <paramref name="depth"/> inside it.</summary>
    private static double Blurred(double depth, double centre, double width, double sigma)
    {
        const double step = 0.05;
        var sum = 0.0;
        for (var u = centre - width / 2; u <= centre + width / 2; u += step)
        {
            var z = (depth - u) / sigma;
            sum += Math.Exp(-0.5 * z * z) / (sigma * Math.Sqrt(2 * Math.PI)) * step;
        }

        return sum;
    }

    /// <summary>
    /// The glow's weight across the edge, from 20 outside to 20 inside, for the arc (11 wide, full) and the ring (7 wide, 45%), blurred by the old bloom's own sigma of 10 at the layer's 85%, and what of the inside part is seen through the capsule's
    /// glass (the fill is 60% dark: 40% passes, 30% on the Darker glass). The old drawing shows only the part outside (depth below 0).
    /// </summary>
    [Fact]
    public void Half_Of_The_Glows_Weight_Lies_Inside_The_Outline_Where_The_Old_Bloom_Is_Cut_Out_And_Is_Seen_Through_The_Glass()
    {
        double sigma = LookConstants.BloomBlurCss;
        var centre = LookConstants.RimInset;
        output.WriteLine("depth  arc*0.85  ring*0.85*0.45  seen inside through Approved (x0.40)  through Darker (x0.30)");
        foreach (var d in new[] { -16, -8, -4, 0, 2, 4, 8, 12, 16, 20 })
        {
            var arc = LookConstants.BloomLayerAlpha * Blurred(d, centre, LookConstants.BloomArcWidth, sigma);
            var ring = LookConstants.BloomLayerAlpha * LookConstants.BloomBaseAlpha * Blurred(d, centre, LookConstants.BloomBaseWidth, sigma);
            output.WriteLine($"{d,5}  {arc,8:0.000}  {ring,14:0.000}  {(d > 0 ? arc * (1 - LookConstants.GlassBaseAlpha) : 0),30:0.000}  {(d > 0 ? arc * (1 - LookConstants.GlassDarkerAlpha) : 0),24:0.000}");
        }

        double Mass(double from, double to, double width)
        {
            var sum = 0.0;
            for (var d = from; d < to; d += 0.1) sum += Blurred(d, centre, width, sigma) * 0.1;
            return sum;
        }

        var inside = Mass(0, 60, LookConstants.BloomArcWidth);
        var outside = Mass(-60, 0, LookConstants.BloomArcWidth);
        var share = inside / (inside + outside);
        output.WriteLine($"the arc's glow: {share:P1} of its weight lies inside the outline (the old drawing shows {1 - share:P1})");
        Assert.InRange(share, 0.45, 0.58);

        // What reaches the eye inside: at 4 inside, with the arc, through the glass.
        var seen = LookConstants.BloomLayerAlpha * Blurred(4, centre, LookConstants.BloomArcWidth, sigma) * (1 - LookConstants.GlassBaseAlpha);
        output.WriteLine($"4 inside, under the arc: {seen:P1} of the page colour is added on top of what the capsule already shows there; its own inner glow is at {LookConstants.CategoryGlowAlpha:P0} at the edge");
        Assert.InRange(seen, 0.08, 0.2);
    }

    // ---- what moves with the shape --------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// During the fly-in the old drawing scales the groups that hold the rim and the bloom (<c>_backScale</c>, <c>_frontScale</c>: the stretch about the shape's centre, up to <see cref="LookConstants.StretchMax"/>), so strokes and corner radii stretch with the shape; the graphics-card
    /// light is given the stretched width and height only: its strokes keep their width and its corners their radius.
    /// </summary>
    [Fact]
    public void During_The_Fly_In_The_Old_Light_Stretches_Its_Strokes_And_Corners_And_The_Graphics_Light_Stretches_Only_Its_Box()
    {
        var view = Src.Read("Island.App/Visuals/IslandView.cs");
        Assert.Contains("_backGroup.RenderTransform = _backScale;", view);
        Assert.Contains("_frontGroup.RenderTransform = _frontScale;", view);
        var controller = Src.Read("Island.App/IslandController.cs");
        Assert.Contains("var w = frame.Width * _machine.StretchX;", controller);
        Assert.Contains("light.Follow(new LightFrame(centre.X - w / 2, centre.Y - h / 2, w, h, frame.Radius, colour, look, breathing, now, frame.Radius * _machine.StretchX, frame.Radius * _machine.StretchY));", controller); // WORK-ORDER-13: the corners are stretched with the shape (P30)
        var stretchY = 1 + LookConstants.StretchMax;
        var stretchX = 1 / Math.Sqrt(stretchY);
        output.WriteLine($"at the largest stretch ({stretchY:0.00} by {stretchX:0.00}) the old arc is {LookConstants.ArcWidth * stretchY:0.0} thick on the top and bottom edges and {LookConstants.ArcWidth * stretchX:0.0} on the sides; the graphics light keeps {LookConstants.ArcWidth}");
        Assert.True(Math.Abs(LookConstants.ArcWidth * stretchY - LookConstants.ArcWidth) > 0.5);
    }

    // ---- the breath ------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The breath of Vibe is 44 keyframes between which the compositor goes linearly (<c>MovingLight.BreathKeyFrames</c>); the old breath is a cosine: the largest gap between the two over a whole breath.</summary>
    [Fact]
    public void The_Breath_Of_44_Linear_Keyframes_Follows_The_Cosine_Within_A_Fraction_Of_A_Percent()
    {
        const int keyFrames = 44;
        var worst = 0.0;
        for (var i = 0; i < keyFrames; i++)
            for (var k = 1; k < 20; k++)
            {
                var f = k / 20.0;
                var t0 = ModeMark.BreathSeconds * i / keyFrames;
                var t1 = ModeMark.BreathSeconds * (i + 1) / keyFrames;
                var linear = ModeMark.Breath(t0) * (1 - f) + ModeMark.Breath(t1) * f;
                var exact = ModeMark.Breath(t0 + (t1 - t0) * f);
                worst = Math.Max(worst, Math.Abs(linear - exact));
            }

        output.WriteLine($"largest gap between the keyframe breath and the cosine: {worst:0.00000} of the glow's strength (the glow itself is at {LookConstants.BloomLayerAlpha:P0})");
        Assert.True(worst < 0.005);
        Assert.Contains("private const int BreathKeyFrames = 44;", Src.Read("Island.Glass/MovingLight.cs"));
    }
}
