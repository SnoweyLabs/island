using Island.Core;

namespace Island.Tests;

public class ModeTipTests
{
    [Theory]
    [InlineData(Mode.Focus, "Focus")]
    [InlineData(Mode.Vibe, "Vibe")]
    [InlineData(Mode.DND, "Do not disturb")]
    public void Resting_The_Pointer_On_The_Island_Names_The_Mode(Mode mode, string name)
    {
        Assert.Contains(name, ModeTip.Of(mode));
        Assert.Equal(name, ModeTip.Name(mode));
    }

    [Fact]
    public void The_Island_Carries_The_Tip_And_Never_Takes_The_Keyboard_With_It()
    {
        var runtime = File.ReadAllText(RepoPaths.File("src", "Island.App", "IslandRuntime.cs"));
        Assert.Contains("ToolTipOpening += (_, _) => tip.Content = ModeTip.Of(Controller.Mode);", runtime);
        Assert.Contains("IsHitTestVisible = false", runtime[runtime.IndexOf("var tip =", StringComparison.Ordinal)..][..300]);
    }
}
