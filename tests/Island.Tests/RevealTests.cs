using Island.Core;

namespace Island.Tests;

public class RevealTests
{
    [Fact]
    public void Linear_Bezier_Is_The_Identity()
    {
        for (var x = 0.0; x <= 1.0; x += 0.05)
            Assert.Equal(x, Easing.CubicBezier(0, 0, 1, 1, x), 1e-6);
    }

    [Fact]
    public void Curves_Start_At_Zero_End_At_One_And_Never_Run_Backwards_Except_For_Overshoot()
    {
        Assert.Equal(0, Easing.Fade(0));
        Assert.Equal(1, Easing.Fade(1));
        Assert.Equal(0, Easing.Move(-1));
        Assert.Equal(1, Easing.Move(2));
        var last = 0.0;
        for (var x = 0.0; x <= 1.0; x += 0.01)
        {
            Assert.InRange(Easing.Fade(x), last - 1e-9, 1 + 1e-9);
            last = Easing.Fade(x);
        }
    }

    [Fact]
    public void Move_Curve_Overshoots_Slightly_Then_Settles_At_One()
    {
        var peak = Enumerable.Range(0, 101).Max(i => Easing.Move(i / 100.0));
        Assert.InRange(peak, 1.001, 1.08);
        Assert.Equal(1, Easing.Move(1));
    }

    [Fact]
    public void Css_Ease_Matches_Known_Midpoint()
    {
        // cubic-bezier(.25,.1,.25,1) at x = 0.5 is about 0.8024.
        Assert.Equal(0.8024, Easing.Ease(0.5), 0.002);
    }

    [Fact]
    public void Each_Element_Starts_32_Ms_After_The_Previous()
    {
        var r = new RevealTimeline(4);
        r.Set(true, 1000);
        Assert.Equal(0, r.At(1000 + 5, 0).Opacity, 0.2);
        Assert.True(r.At(1000 + 40, 0).Opacity > 0);
        Assert.Equal(0, r.At(1000 + 32, 1).Opacity, 1e-12);
        Assert.True(r.At(1000 + 33, 1).Opacity > 0);
        Assert.Equal(0, r.At(1000 + 96, 3).Opacity, 1e-12);
        Assert.True(r.At(1000 + 100, 3).Opacity > 0);
    }

    [Fact]
    public void Entrance_Lasts_280_Ms_For_Opacity_And_440_Ms_For_Rise()
    {
        var r = new RevealTimeline(1);
        r.Set(true, 0);
        Assert.True(r.At(279, 0).Opacity < 1);
        Assert.Equal(1, r.At(281, 0).Opacity, 1e-9);
        Assert.NotEqual(1, r.At(300, 0).Move, 1e-4);
        Assert.Equal(1, r.At(441, 0).Move, 1e-9);
        Assert.Equal(0, r.At(281, 0).Blur, 1e-9);
    }

    [Fact]
    public void Exit_Lasts_110_Ms_And_Starts_Everything_Together()
    {
        var r = new RevealTimeline(3);
        r.Set(true, 0);
        r.Set(false, 2000);
        Assert.True(r.At(2050, 0).Opacity < 1);
        Assert.True(r.At(2050, 2).Opacity < 1);
        Assert.Equal(0, r.At(2111, 2).Opacity, 1e-9);
        Assert.Equal(LookConstants.ContentsBlurStart, r.At(2111, 0).Blur, 1e-9);
    }

    [Fact]
    public void A_Change_In_The_Middle_Of_Another_Does_Not_Jump()
    {
        var r = new RevealTimeline(3);
        r.Set(true, 0);
        var before = r.At(150, 1);
        r.Set(false, 150);
        var after = r.At(150, 1);
        Assert.Equal(before.Opacity, after.Opacity, 1e-12);
        Assert.Equal(before.Move, after.Move, 1e-12);
        Assert.Equal(before.Blur, after.Blur, 1e-12);

        var mid = r.At(180, 1);
        r.Set(true, 180);
        Assert.Equal(mid.Opacity, r.At(180, 1).Opacity, 1e-12);
    }

    [Fact]
    public void Reset_Gives_Hidden_Elements_Without_A_Transition()
    {
        var r = new RevealTimeline(2);
        r.Set(true, 0);
        r.Reset(5);
        Assert.Equal(5, r.Count);
        Assert.False(r.Visible);
        for (var i = 0; i < 5; i++) Assert.Equal(RevealValues.Hidden, r.At(10_000, i));
    }

    [Fact]
    public void Colour_Changes_Linearly_Over_350_Ms_And_Retargets_From_Where_It_Is()
    {
        var red = Rgb.FromHex(LookConstants.MediaColor);
        var blue = Rgb.FromHex(LookConstants.AppsColor);
        var c = new ColourTransition(red);
        c.Retarget(blue, 1000, snap: false);
        Assert.Equal(red, c.At(1000));
        Assert.Equal(blue, c.At(1350));
        var mid = c.At(1175);
        Assert.Equal((red.R + blue.R) / 2, mid.R, 1e-9);

        c.Retarget(Rgb.FromHex(LookConstants.VibeColor), 1175, snap: false);
        Assert.Equal(mid, c.At(1175));
    }

    [Fact]
    public void Colour_Snap_Has_No_Transition()
    {
        var c = new ColourTransition(Rgb.FromHex(LookConstants.MediaColor));
        var green = Rgb.FromHex(LookConstants.VibeColor);
        c.Retarget(green, 500, snap: true);
        Assert.Equal(green, c.At(500));
    }
}
