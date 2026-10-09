using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-7 section 1: the mark of a mode on the edge, as numbers that depend only on the mode and the time.</summary>
public class ModeMarkTests
{
    [Fact]
    public void Focus_Is_The_Approved_Edge()
    {
        // Every part at full strength, at every moment: nothing is added, nothing is taken away.
        foreach (var seconds in new[] { 0, 0.4, 1.1, 2.2, 1234.5 })
        {
            Assert.Equal(ModeMark.Look.Approved, ModeMark.LookOf(Mode.Focus, seconds));
            Assert.Equal(ModeMark.Look.Approved, ModeMark.LookOf(Mode.Focus, seconds, isPill: true));
        }

        Assert.Equal(new ModeMark.Look(1, 1, 1, 0), ModeMark.Look.Approved);
    }

    [Fact]
    public void Breathing_Depends_Only_On_Time()
    {
        // Starts at the approved strength, reaches 40% of it half a period later, is back after a whole one; the same time gives the same value.
        Assert.Equal(1, ModeMark.Breath(0), 1e-12);
        Assert.Equal(ModeMark.BreathLow, ModeMark.Breath(ModeMark.BreathSeconds / 2), 1e-12);
        Assert.Equal(1, ModeMark.Breath(ModeMark.BreathSeconds), 1e-9);
        Assert.Equal(ModeMark.Breath(7.3), ModeMark.Breath(7.3));
        Assert.Equal(ModeMark.Breath(0.8), ModeMark.Breath(0.8 + 5 * ModeMark.BreathSeconds), 1e-9);

        // Eased: it never leaves 40% to 100%, is symmetric about the low point, and moves slowest at both ends.
        for (var t = 0.0; t <= 22; t += 0.05)
        {
            var v = ModeMark.Breath(t);
            Assert.InRange(v, ModeMark.BreathLow - 1e-12, 1 + 1e-12);
        }

        Assert.Equal(ModeMark.Breath(0.3), ModeMark.Breath(ModeMark.BreathSeconds - 0.3), 1e-9);
        Assert.True(1 - ModeMark.Breath(0.05) < 0.02, "slow at the start");

        // Vibe breathes on the capsule; the rest of Vibe's edge is the approved one.
        var vibe = ModeMark.LookOf(Mode.Vibe, 1.1);
        Assert.Equal(ModeMark.BreathLow, vibe.Glow, 1e-12);
        Assert.Equal((1.0, 1.0, 0.0), (vibe.MovingLight, vibe.PageRim, vibe.DashedRim));

        // A time that is not a number reads as the start; never NaN.
        Assert.Equal(1, ModeMark.Breath(double.NaN));
        Assert.Equal(1, ModeMark.Breath(double.PositiveInfinity));
    }

    [Fact]
    public void Nothing_Breathes_On_The_Pill()
    {
        // The pill can stay for hours: in Vibe its glow stands still at the low end of the breath, whatever the time.
        foreach (var seconds in new[] { 0, 0.3, 1.1, 5, 99999 })
            Assert.Equal(ModeMark.BreathLow, ModeMark.LookOf(Mode.Vibe, seconds, isPill: true).Glow);
    }

    [Fact]
    public void Dnd_Has_No_Page_Colour_On_The_Edge_And_A_Dashed_Rim()
    {
        var dnd = ModeMark.LookOf(Mode.DND, 3);

        Assert.Equal(new ModeMark.Look(0, 0, 0, 1), dnd); // no glow behind, no moving light, no page-coloured rim, a dashed one
        Assert.Equal(1.6, ModeMark.DashedRimWidth);
        Assert.Equal(0.55, ModeMark.DashedRimAlpha);
        Assert.Equal(dnd, ModeMark.LookOf(Mode.DND, 0, isPill: true));
    }

    [Fact]
    public void A_Change_Of_Mode_Cross_Fades_Over_350_Ms()
    {
        var from = ModeMark.LookOf(Mode.Focus, 0);
        var to = ModeMark.LookOf(Mode.DND, 0);

        Assert.Equal(from, ModeMark.Blend(from, to, 0));
        Assert.Equal(from, ModeMark.Blend(from, to, -5));
        Assert.Equal(to, ModeMark.Blend(from, to, 350));
        Assert.Equal(to, ModeMark.Blend(from, to, 5000));
        var middle = ModeMark.Blend(from, to, 175);
        Assert.Equal(0.5, middle.DashedRim, 1e-9);
        Assert.Equal(0.5, middle.Glow, 1e-9);
        double last = 0;
        for (var ms = 0; ms <= 350; ms += 10)
        {
            var step = ModeMark.Blend(from, to, ms).DashedRim;
            Assert.True(step >= last - 1e-12, "the fade only goes one way");
            last = step;
        }

        Assert.Equal(to, ModeMark.Blend(from, to, double.NaN)); // nonsense settles on the new mode
    }

    [Fact]
    public void A_Value_That_Is_Not_One_Of_The_Three_Modes_Reads_As_Focus()
    {
        Assert.Equal(ModeMark.Look.Approved, ModeMark.LookOf((Mode)42, 1));
    }
}
