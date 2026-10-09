using Island.Core;

namespace Island.Tests.Front;

/// <summary>WORK-ORDER-13, Dan's Q2: the notice goes to a second screen when the first has a fullscreen program in front (WORK-ORDER-7 section 1, EVALS X8).</summary>
public class NoticeScreensTests
{
    private static ScreenInfo Screen(int left, int top, int right, int bottom, bool primary = false) =>
        new(new PixelRect(left, top, right, bottom), new PixelRect(left, top, right, bottom - 40), 1.0, primary);

    private static readonly ScreenInfo Main = Screen(0, 0, 1920, 1080, primary: true);
    private static readonly ScreenInfo Right = Screen(1920, 0, 3840, 1080);
    private static readonly ScreenInfo Left = Screen(-1280, 0, 0, 1024);

    [Fact]
    public void With_One_Screen_There_Is_No_Other()
    {
        Assert.Equal(-1, NoticeScreens.OtherScreenIndex([Main], Main.Full));
        Assert.Equal(-1, NoticeScreens.OtherScreenIndex([], Main.Full));
        Assert.Equal(-1, NoticeScreens.OtherScreenIndex(null, Main.Full));
    }

    [Fact]
    public void The_Screen_Without_The_Program_In_Front_Is_The_Other()
    {
        Assert.Equal(1, NoticeScreens.OtherScreenIndex([Main, Right], Main.Full));
        Assert.Equal(0, NoticeScreens.OtherScreenIndex([Main, Right], Right.Full));
    }

    [Fact]
    public void The_Main_Screen_Is_Preferred_When_It_Is_Not_The_One_In_Front()
    {
        Assert.Equal(1, NoticeScreens.OtherScreenIndex([Right, Main, Left], Right.Full));
        Assert.Equal(2, NoticeScreens.OtherScreenIndex([Right, Left, Main], Left.Full));
    }

    [Fact]
    public void A_Foreground_That_Is_On_No_Screen_Or_Not_Known_Gives_Nothing_Because_Nothing_Can_Be_Told()
    {
        Assert.Equal(-1, NoticeScreens.OtherScreenIndex([Main, Right], null));
        Assert.Equal(-1, NoticeScreens.OtherScreenIndex([Main, Right], new PixelRect(5, 5, 6, 6)));
    }

    [Fact]
    public void A_Screen_With_No_Pixels_Is_Not_A_Second_Screen()
    {
        var empty = new ScreenInfo(new PixelRect(0, 0, 0, 0), new PixelRect(0, 0, 0, 0), 1.0);
        Assert.Equal(-1, NoticeScreens.OtherScreenIndex([Main, empty], Main.Full));
    }

    [Fact]
    public void The_Middle_Of_A_Screen_Is_Inside_It_Whatever_Its_Place()
    {
        foreach (var s in new[] { Main, Right, Left })
            Assert.True(s.Full.Contains(NoticeScreens.CentreOf(s.Full)));
    }

    [Fact]
    public void The_Fallback_Names_The_Second_Screen_Step_Before_The_Sound_And_Not_In_Do_Not_Disturb()
    {
        var now = DateTimeOffset.UtcNow;
        var wait = NoticeWait.Arrive(now);
        var facts = new NoticeFacts(Mode.Vibe, FrontState.FullscreenProgram, SecondScreenWithoutFullscreen: true, now);
        Assert.Equal(NoticeAction.ShowOnOtherScreen, NoticeFallback.Next(wait, facts).Action);
        Assert.Equal(NoticeAction.Drop, NoticeFallback.Next(wait, facts with { Mode = Mode.DND }).Action);
        Assert.Equal(NoticeAction.PlaySound, NoticeFallback.Next(wait, facts with { Mode = Mode.Focus, Front = FrontState.ExclusiveFullscreen, SecondScreenWithoutFullscreen = false }).Action);
    }
}
