using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-6 section 2's narrow table: it is the Vibe column of the full table, for the island.</summary>
public class ShowDecisionTests
{
    private static ShowAnswer Vibe(FrontState front, ShowOrigin origin) => ShowDecision.Decide(front, Appearer.Island, origin, Mode.Vibe);

    [Fact]
    public void Exclusive_Fullscreen_Never_Shows()
    {
        Assert.Equal(ShowAnswer.StayAway, Vibe(FrontState.ExclusiveFullscreen, ShowOrigin.Asked));
        Assert.Equal(ShowAnswer.StayAway, Vibe(FrontState.ExclusiveFullscreen, ShowOrigin.ByItself));

        // In every mode and for everything that can appear, however it was asked for.
        foreach (var mode in Enum.GetValues<Mode>())
            foreach (var thing in Enum.GetValues<Appearer>())
                foreach (var origin in Enum.GetValues<ShowOrigin>())
                    Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(FrontState.ExclusiveFullscreen, thing, origin, mode));
    }

    [Fact]
    public void Fullscreen_Program_Shows_Only_When_Asked()
    {
        Assert.Equal(ShowAnswer.Show, Vibe(FrontState.FullscreenProgram, ShowOrigin.Asked));
        Assert.Equal(ShowAnswer.StayAway, Vibe(FrontState.FullscreenProgram, ShowOrigin.ByItself));
    }

    [Fact]
    public void Presentation_Shows_Only_When_Asked()
    {
        Assert.Equal(ShowAnswer.Show, Vibe(FrontState.Presentation, ShowOrigin.Asked));
        Assert.Equal(ShowAnswer.StayAway, Vibe(FrontState.Presentation, ShowOrigin.ByItself));
    }

    [Fact]
    public void Clear_Always_Shows()
    {
        Assert.Equal(ShowAnswer.Show, Vibe(FrontState.Clear, ShowOrigin.Asked));
        Assert.Equal(ShowAnswer.Show, Vibe(FrontState.Clear, ShowOrigin.ByItself));
    }

    [Fact]
    public void A_Value_Outside_The_Lists_Answers_StayAway()
    {
        var bad = 99;
        Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide((FrontState)bad, Appearer.Island, ShowOrigin.Asked, Mode.Vibe));
        Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(FrontState.Clear, (Appearer)bad, ShowOrigin.Asked, Mode.Vibe));
        Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(FrontState.Clear, Appearer.Island, (ShowOrigin)bad, Mode.Vibe));
        Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(FrontState.Clear, Appearer.Island, ShowOrigin.Asked, (Mode)bad));
        Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide((FrontState)(-1), Appearer.Notice, ShowOrigin.ByItself, Mode.Focus));
        Assert.Equal(ShowAnswer.StayAway, default(ShowAnswer));
    }
}
