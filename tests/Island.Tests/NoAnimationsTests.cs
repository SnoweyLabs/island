using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-13, Dan's P1: with Windows' "Show animations" off the island comes and goes without spring, delay or stagger.</summary>
public class NoAnimationsTests
{
    private static Clock NewClock() => new(new IslandMachine(60) { Animations = false });

    [Fact]
    public void The_Main_Key_Opens_The_Island_At_Rest_On_The_Next_Frame_With_The_Contents_Shown()
    {
        var c = NewClock();
        c.M.ShowHideKey(c.Now);
        c.Run(Clock.Frame);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.True(c.M.ContentsVisible);
        Assert.True(c.M.IsAtRest);
        Assert.Equal(LookConstants.TopGap, c.M.Y.Value);
        Assert.Equal(c.M.Width.Target, c.M.Width.Value);
        Assert.Equal(c.M.Height.Target, c.M.Height.Value);
    }

    [Fact]
    public void The_Main_Key_Again_Takes_It_Away_On_The_Next_Frame_Without_A_Flight()
    {
        var c = NewClock();
        c.M.ShowHideKey(c.Now);
        c.Run(Clock.Frame * 2);
        c.M.ShowHideKey(c.Now);
        c.Run(Clock.Frame);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
    }

    [Fact]
    public void With_Animations_On_The_Same_Key_Still_Takes_A_Moment_To_Open()
    {
        var c = new Clock(new IslandMachine(60));
        c.M.ShowHideKey(c.Now);
        c.Run(Clock.Frame);
        Assert.NotEqual(IslandPhase.Open, c.M.Phase);
        Assert.False(c.M.IsAtRest);
    }

    [Fact]
    public void A_Page_Key_While_Open_Changes_The_Page_At_Once()
    {
        var c = NewClock();
        c.M.ShowHideKey(c.Now);
        c.Run(Clock.Frame * 2);
        c.M.PageKey(PageIds.Browser, c.Now);
        c.Run(Clock.Frame);
        Assert.Equal(PageIds.Browser, c.M.ContentsPageId);
        Assert.True(c.M.ContentsVisible);
        Assert.True(c.M.IsAtRest);
    }

    [Fact]
    public void An_Instant_Reveal_Shows_Every_Element_Where_It_Is_Going_At_The_Moment_It_Is_Asked()
    {
        var reveal = new RevealTimeline(5) { Instant = true };
        reveal.Set(true, 1000);
        for (var i = 0; i < 5; i++) Assert.Equal(RevealValues.Shown, reveal.At(1000, i));
        reveal.Set(false, 2000);
        for (var i = 0; i < 5; i++) Assert.Equal(RevealValues.Hidden, reveal.At(2000, i));
    }

    [Fact]
    public void The_App_Reads_Windows_Setting_For_The_Island_And_For_The_Settings_Screen_And_A_Self_Test_Always_Has_Animations_On()
    {
        var host = File.ReadAllText(RepoPaths.File("src", "Island.App", "AppHost.cs"));
        Assert.Contains("Runtime.Controller.AnimationsProbe = () => WindowsAnimations.On;", host);
        Assert.Contains("WindowsTextSize.Start();", host);
        var screen = File.ReadAllText(RepoPaths.File("src", "Island.App", "SettingsScreen.cs"));
        Assert.Contains("var animations = WindowsAnimations.On;", screen);
        var probe = File.ReadAllText(RepoPaths.File("src", "Island.App", "WindowsAnimations.cs"));
        Assert.Contains("OutsideGate.Current.SelfTest || SystemParameters.ClientAreaAnimation", probe);
        Assert.Contains("OutsideGate.Current.SelfTest", File.ReadAllText(RepoPaths.File("src", "Island.App", "WindowsTextSize.cs")));
    }
}
