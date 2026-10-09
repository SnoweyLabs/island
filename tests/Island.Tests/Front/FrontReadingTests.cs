using Island.Core;

namespace Island.Tests;

/// <summary>The pure half of WORK-ORDER-6 section 2: facts about the window in front become a FrontState.</summary>
public class FrontReadingTests
{
    private static readonly FrontRect Screen = new(0, 0, 1920, 1080);
    private static readonly FrontRect LeftScreen = new(-1920, 0, 0, 1080);

    private static FrontWindowFacts Facts(
        FrontRect? window = null, FrontRect? screen = null, bool title = false, bool sizing = false, string? cls = "AlphaWindow",
        bool island = false, bool desktop = false, bool tool = false) =>
        new(window ?? Screen, screen ?? Screen, title, sizing, cls, island, desktop, tool);

    [Fact]
    public void A_Window_Equal_To_Its_Screen_Without_A_Frame_Is_Fullscreen()
    {
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts()));
        // A screen to the left of the main one has negative coordinates.
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(window: LeftScreen, screen: LeftScreen)));
        // The screen is the window's own screen, not another one.
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: Screen, screen: LeftScreen)));
        // A null class name is just an unknown class.
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(cls: null)));
        // Windows' "busy" answer is not an input: the rectangle decides.
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Combine(2, Facts()));
    }

    [Fact]
    public void A_Maximised_Window_With_A_Frame_Is_Not()
    {
        // A maximised window with its frame reaches a little past the screen, or fills the work area.
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(-8, -8, 1928, 1088), title: true, sizing: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(0, 0, 1920, 1040), title: true, sizing: true)));
        // Even with the exact rectangle, a title bar or a sizing border is a frame.
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(title: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(sizing: true)));
        // A borderless window one pixel off in any direction is not fullscreen.
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(1, 0, 1920, 1080))));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(0, 0, 1919, 1080))));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(0, 0, 1920, 1081))));
        // A floating tool window is never a program in front.
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(tool: true)));
        // Windows' "busy" with a window that does not cover its screen is not enough (other programs' overlays cause it).
        Assert.Equal(FrontState.Clear, FrontClassifier.Combine(2, Facts(window: new(100, 100, 800, 600))));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("SysListView32")]
    [InlineData("Flip3D")]
    [InlineData("progman")]
    [InlineData("SHELL_TRAYWND")]
    public void Desktop_And_Taskbar_Are_Never_Fullscreen(string className)
    {
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(cls: className)));
    }

    [Fact]
    public void Desktop_And_Shell_Handles_Are_Never_Fullscreen()
    {
        // The desktop window and the shell window, whatever class Windows gives them.
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(desktop: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(desktop: true, cls: "AlphaWindow")));
        // A class that only starts with a desktop class name is an ordinary program.
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Classify(Facts(cls: "ProgmanLike")));
    }

    [Fact]
    public void The_Islands_Own_Windows_Are_Never_Fullscreen()
    {
        // The settings screen can fill a whole screen without a frame; it is still not "a program in front".
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(island: true)));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: LeftScreen, screen: LeftScreen, island: true)));
    }

    [Fact]
    public void Bad_Input_Gives_Clear()
    {
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(null));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(0, 0, 0, 0), screen: new(0, 0, 0, 0))));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(10, 10, 5, 5), screen: new(10, 10, 5, 5))));
        Assert.Equal(FrontState.Clear, FrontClassifier.Classify(Facts(window: new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue), screen: new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue))));
        Assert.Equal(FrontState.Clear, FrontClassifier.Combine(null, null));
        Assert.Equal(FrontState.Clear, FrontClassifier.Combine(-5, null));
        Assert.Equal(FrontState.Clear, FrontClassifier.Combine(int.MaxValue, null));
    }

    [Fact]
    public void Windows_Own_Answer_Maps_To_Hard_States_Only()
    {
        Assert.Equal(FrontState.ExclusiveFullscreen, FrontClassifier.FromNotificationState(3));
        Assert.Equal(FrontState.Presentation, FrontClassifier.FromNotificationState(4));
        // not present, busy, accepts, quiet time, a Store app, and numbers that are not in the list
        foreach (var other in new[] { 1, 2, 5, 6, 7, 0, -1, 8, 99, int.MinValue, int.MaxValue })
            Assert.Null(FrontClassifier.FromNotificationState(other));
    }

    [Fact]
    public void Windows_Hard_States_Win_Over_The_Rectangle()
    {
        Assert.Equal(FrontState.ExclusiveFullscreen, FrontClassifier.Combine(3, Facts(window: new(100, 100, 800, 600), title: true)));
        Assert.Equal(FrontState.ExclusiveFullscreen, FrontClassifier.Combine(3, null));
        Assert.Equal(FrontState.Presentation, FrontClassifier.Combine(4, Facts()));
        // Any other answer leaves the rectangle to decide.
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Combine(5, Facts()));
        Assert.Equal(FrontState.Clear, FrontClassifier.Combine(5, Facts(title: true)));
        Assert.Equal(FrontState.FullscreenProgram, FrontClassifier.Combine(null, Facts()));
    }
}
