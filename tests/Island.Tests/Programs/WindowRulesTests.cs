using Island.Core;

namespace Island.Tests.Programs;

public class WindowRulesTests
{
    /// <summary>A plain, ordinary top-level window: the one that must be listed.</summary>
    private static WindowFacts Normal() =>
        new(Visible: true, HasOwner: false, IsToolWindow: false, IsAppWindow: false, MarkedDeletedFromTaskList: false,
            ClassName: "AlphaWindowClass", Title: "Alpha", Width: 800, Height: 600, CloakFlags: 0, OnOtherDesktop: false);

    [Fact]
    public void A_Normal_Window_Is_Listed() => Assert.True(WindowRules.IsListed(Normal()));

    [Fact]
    public void Invisible_Window_Is_Dropped() => Assert.False(WindowRules.IsListed(Normal() with { Visible = false }));

    [Fact]
    public void Owned_Window_Is_Dropped() => Assert.False(WindowRules.IsListed(Normal() with { HasOwner = true }));

    [Fact]
    public void Tool_Window_Is_Dropped() => Assert.False(WindowRules.IsListed(Normal() with { IsToolWindow = true }));

    [Fact]
    public void App_Window_Style_Wins_Over_Owner_And_Tool_Window()
    {
        Assert.True(WindowRules.IsListed(Normal() with { IsAppWindow = true, HasOwner = true, IsToolWindow = true }));
    }

    [Fact]
    public void Window_Removed_From_Task_List_Is_Dropped() =>
        Assert.False(WindowRules.IsListed(Normal() with { MarkedDeletedFromTaskList = true }));

    [Fact]
    public void Core_Window_Class_Is_Dropped() =>
        Assert.False(WindowRules.IsListed(Normal() with { ClassName = "Windows.UI.Core.CoreWindow" }));

    [Theory]
    [InlineData(1)] // cloaked by its app
    [InlineData(4)] // inherited from its owner
    [InlineData(3)]
    public void Cloaked_Window_Is_Dropped(int flags) =>
        Assert.False(WindowRules.IsListed(Normal() with { CloakFlags = flags, OnOtherDesktop = true }));

    [Fact]
    public void Shell_Cloaked_Because_Of_Another_Desktop_Is_Kept() =>
        Assert.True(WindowRules.IsListed(Normal() with { CloakFlags = 2, OnOtherDesktop = true }));

    [Fact]
    public void Shell_Cloaked_On_This_Desktop_Is_Dropped() =>
        Assert.False(WindowRules.IsListed(Normal() with { CloakFlags = 2, OnOtherDesktop = false }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Title_Is_Dropped(string title) => Assert.False(WindowRules.IsListed(Normal() with { Title = title }));

    [Fact]
    public void Tiny_Window_Is_Dropped()
    {
        Assert.False(WindowRules.IsListed(Normal() with { Width = WindowRules.MinWidth - 1 }));
        Assert.False(WindowRules.IsListed(Normal() with { Height = WindowRules.MinHeight - 1 }));
        Assert.True(WindowRules.IsListed(Normal() with { Width = WindowRules.MinWidth, Height = WindowRules.MinHeight }));
    }

    [Fact]
    public void Frame_Host_Is_Recognised_By_Exe_Name_Only()
    {
        Assert.True(WindowRules.IsUwpFrameHost("applicationframehost.exe"));
        Assert.False(WindowRules.IsUwpFrameHost("alpha.exe"));
        Assert.False(WindowRules.IsUwpFrameHost(null));
    }

    [Fact]
    public void The_Real_Uwp_Program_Is_The_Child_Whose_Class_Starts_With_Windows_Ui_Core()
    {
        long? found = WindowRules.PickUwpChild([(1, "ApplicationFrameInputSinkWindow"), (2, "Windows.UI.Core.CoreWindow"), (3, "Windows.UI.Core.Other")]);
        Assert.Equal(2, found);
    }

    [Fact]
    public void A_Minimised_Uwp_Window_Has_No_Such_Child()
    {
        Assert.Null(WindowRules.PickUwpChild([(1, "ApplicationFrameInputSinkWindow")]));
        Assert.Null(WindowRules.PickUwpChild([]));
    }

    [Theory]
    [InlineData("Alpha.App_abc123!Main", "Alpha.App_abc123")]
    [InlineData("alpha.exe", null)]
    [InlineData("NoUnderscore!App", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Package_Family_Is_The_Part_Before_The_Bang(string? aumid, string? expected) =>
        Assert.Equal(expected, WindowRules.PackageFamilyOf(aumid));
}
