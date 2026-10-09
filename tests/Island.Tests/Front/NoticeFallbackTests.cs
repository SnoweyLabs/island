using Island.Core;

namespace Island.Tests;

/// <summary>The fallback of EVALS X8 for a notice that may not show, as a pure step function given facts and a clock.</summary>
public class NoticeFallbackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static NoticeFacts Facts(Mode mode, FrontState front, bool second = false, double t = 0) => new(mode, front, second, At(t));

    [Fact]
    public void Second_Screen_Then_Sound_Then_Later()
    {
        // Presentation in front, Focus: the second screen comes first, with no sound.
        var s = NoticeWait.Arrive(At(0));
        var onSecond = NoticeFallback.Next(s, Facts(Mode.Focus, FrontState.Presentation, second: true));
        Assert.Equal(NoticeAction.ShowOnOtherScreen, onSecond.Action);
        Assert.False(onSecond.NeedsTimer);

        // No second screen: one short sound, and the notice keeps waiting.
        var sound = NoticeFallback.Next(s, Facts(Mode.Focus, FrontState.Presentation));
        Assert.Equal(NoticeAction.PlaySound, sound.Action);
        Assert.True(sound.NeedsTimer);

        // Two seconds later: still no way to show; waiting, and no second sound.
        var wait = NoticeFallback.Next(sound.State, Facts(Mode.Focus, FrontState.Presentation, t: 2));
        Assert.Equal(NoticeAction.Wait, wait.Action);
        Assert.True(wait.NeedsTimer);
        Assert.Equal(NoticeAction.Wait, NoticeFallback.Next(wait.State, Facts(Mode.Focus, FrontState.ExclusiveFullscreen, t: 4)).Action);

        // A second screen turns up later: it still comes before waiting.
        var late = NoticeFallback.Next(wait.State, Facts(Mode.Focus, FrontState.Presentation, second: true, t: 6));
        Assert.Equal(NoticeAction.ShowOnOtherScreen, late.Action);

        // Or the table allows it later: shown where the island appears, and nothing waits any more.
        var later = NoticeFallback.Next(wait.State, Facts(Mode.Focus, FrontState.Clear, t: 30));
        Assert.Equal(NoticeAction.ShowHere, later.Action);
        Assert.False(later.NeedsTimer);
    }

    [Fact]
    public void The_Table_Allows_It_At_Once_Without_Any_Fallback()
    {
        var s = NoticeWait.Arrive(At(0));

        var clear = NoticeFallback.Next(s, Facts(Mode.Vibe, FrontState.Clear, second: true));
        Assert.Equal(NoticeAction.ShowHere, clear.Action);
        Assert.False(clear.NeedsTimer);

        // Focus lets the notice over a fullscreen program (a game, a film) without any fallback and without a sound.
        Assert.Equal(NoticeAction.ShowHere, NoticeFallback.Next(s, Facts(Mode.Focus, FrontState.FullscreenProgram)).Action);
    }

    [Fact]
    public void No_Sound_Outside_Focus()
    {
        foreach (var mode in new[] { Mode.Vibe, Mode.DND })
            foreach (var front in Enum.GetValues<FrontState>())
            {
                var step = new NoticeStep(NoticeWait.Arrive(At(0)), NoticeAction.Wait);
                for (var i = 0; i < 20 && step.NeedsTimer; i++)
                {
                    step = NoticeFallback.Next(step.State, Facts(mode, front, t: i * 2));
                    Assert.NotEqual(NoticeAction.PlaySound, step.Action);
                }
            }

        // Vibe over a presentation, no second screen: it waits, silently, and the timer runs.
        var vibe = NoticeFallback.Next(NoticeWait.Arrive(At(0)), Facts(Mode.Vibe, FrontState.Presentation));
        Assert.Equal(NoticeAction.Wait, vibe.Action);
        Assert.True(vibe.NeedsTimer);

        // Vibe over a fullscreen program: the table says stay away by itself, so it waits too.
        Assert.Equal(NoticeAction.Wait, NoticeFallback.Next(NoticeWait.Arrive(At(0)), Facts(Mode.Vibe, FrontState.FullscreenProgram)).Action);

        // In Focus the sound exists and is played once.
        Assert.Equal(NoticeAction.PlaySound, NoticeFallback.Next(NoticeWait.Arrive(At(0)), Facts(Mode.Focus, FrontState.Presentation)).Action);
    }

    [Fact]
    public void In_DND_A_Notice_Is_Dropped()
    {
        foreach (var front in Enum.GetValues<FrontState>())
            foreach (var second in new[] { false, true })
            {
                var step = NoticeFallback.Next(NoticeWait.Arrive(At(0)), Facts(Mode.DND, front, second));
                Assert.Equal(NoticeAction.Drop, step.Action);
                Assert.False(step.NeedsTimer);
            }

        // A waiting notice is dropped the moment the mode becomes DND.
        var waiting = NoticeFallback.Next(NoticeWait.Arrive(At(0)), Facts(Mode.Vibe, FrontState.Presentation)).State;
        Assert.Equal(NoticeAction.Drop, NoticeFallback.Next(waiting, Facts(Mode.DND, FrontState.Presentation, t: 2)).Action);
    }

    [Fact]
    public void A_Stale_Notice_Is_Dropped()
    {
        var s = NoticeWait.Arrive(At(0));
        Assert.Equal(TimeSpan.FromMinutes(10), NoticeFallback.StaleAfter);

        Assert.Equal(NoticeAction.Wait, NoticeFallback.Next(s, Facts(Mode.Vibe, FrontState.Presentation, t: 599)).Action);

        var stale = NoticeFallback.Next(s, Facts(Mode.Vibe, FrontState.Presentation, t: 600));
        Assert.Equal(NoticeAction.Drop, stale.Action);
        Assert.False(stale.NeedsTimer);

        // Stale wins even when the table would allow it now, and even with a second screen.
        Assert.Equal(NoticeAction.Drop, NoticeFallback.Next(s, Facts(Mode.Vibe, FrontState.Clear, t: 601)).Action);
        Assert.Equal(NoticeAction.Drop, NoticeFallback.Next(s, Facts(Mode.Focus, FrontState.Presentation, second: true, t: 3600)).Action);

        // A clock that went backwards is never stale.
        Assert.Equal(NoticeAction.Wait, NoticeFallback.Next(s, Facts(Mode.Vibe, FrontState.Presentation, t: -5000)).Action);
    }

    [Fact]
    public void Nothing_Waiting_Means_No_Timer()
    {
        Assert.False(NoticeWait.Idle.NeedsTimer);
        foreach (var mode in Enum.GetValues<Mode>())
            foreach (var front in Enum.GetValues<FrontState>())
            {
                var step = NoticeFallback.Next(NoticeWait.Idle, Facts(mode, front, second: true));
                Assert.Equal(NoticeAction.None, step.Action);
                Assert.False(step.NeedsTimer);
            }

        // Every way a notice ends leaves nothing waiting.
        var arrived = NoticeWait.Arrive(At(0));
        Assert.True(arrived.NeedsTimer);
        Assert.False(NoticeFallback.Next(arrived, Facts(Mode.Vibe, FrontState.Clear)).NeedsTimer);
        Assert.False(NoticeFallback.Next(arrived, Facts(Mode.DND, FrontState.Clear)).NeedsTimer);
        Assert.False(NoticeFallback.Next(arrived, Facts(Mode.Focus, FrontState.Presentation, second: true)).NeedsTimer);
        Assert.False(NoticeFallback.Next(arrived, Facts(Mode.Vibe, FrontState.Presentation, t: 700)).NeedsTimer);

        // While it waits, and only then, the timer is needed, every two seconds.
        Assert.True(NoticeFallback.Next(arrived, Facts(Mode.Vibe, FrontState.Presentation)).NeedsTimer);
        Assert.Equal(TimeSpan.FromSeconds(2), NoticeFallback.RecheckEvery);

        // A step taken after the notice ended does nothing and keeps no timer.
        var ended = NoticeFallback.Next(arrived, Facts(Mode.Vibe, FrontState.Clear));
        Assert.Equal(NoticeAction.None, NoticeFallback.Next(ended.State, Facts(Mode.Vibe, FrontState.Presentation, t: 2)).Action);
    }

    [Fact]
    public void An_Unknown_Mode_Drops_The_Notice_Silently()
    {
        var step = NoticeFallback.Next(NoticeWait.Arrive(At(0)), Facts((Mode)42, FrontState.Presentation));
        Assert.Equal(NoticeAction.Drop, step.Action);
        Assert.False(step.NeedsTimer);
    }
}
