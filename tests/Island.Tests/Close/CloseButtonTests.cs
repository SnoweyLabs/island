using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-6 section 5: the close button. Pure rules, facts given by a pretend machine.</summary>
public class CloseButtonTests
{
    private sealed class Facts : ICloseFacts
    {
        public HashSet<long> Existing { get; } = [10, 20, 30];

        public HashSet<long> Island { get; } = [999];

        public HashSet<long> Admin { get; } = [];

        public HashSet<long> Disabled { get; } = [];

        public bool AddonConnected { get; set; } = true;

        public HashSet<string> Tabs { get; } = ["p1:7"];

        public bool WindowExists(long window) => Existing.Contains(window);

        public bool IsIslandWindow(long window) => Island.Contains(window);

        public bool NeedsMoreRights(long window) => Admin.Contains(window);

        public bool IsEnabled(long window) => !Disabled.Contains(window);

        public bool TabExists(string tabKey) => Tabs.Contains(tabKey);
    }

    private static readonly Pick Program = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
    private static readonly Pick Folder = Pick.ForFolder("Downloads", PageIds.Folders);
    private static readonly Pick Site = Pick.ForSite("Example", "example.org", PageIds.Browser);

    private static ClickPlan Forward(long window) => new(ClickKind.BringForward, window);

    [Fact]
    public void Dimmed_Until_A_Pick_Was_Clicked_In_This_Showing()
    {
        var button = new CloseButton(new Facts());

        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action); // pressed to send the island away: no window is lost

        // A click that only started a program, or found nothing, arms nothing.
        button.PickClicked(Program, new ClickPlan(ClickKind.Start), null);
        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);

        button.PickClicked(Program, Forward(10), null);
        Assert.Equal(CloseState.Ready, button.State);
    }

    [Fact]
    public void Target_Is_The_Window_The_Click_Brought_Forward()
    {
        var button = new CloseButton(new Facts());
        button.PickClicked(Program, Forward(10), null);
        button.PickClicked(Program, Forward(20), null); // the next click went to another window

        var outcome = button.Press();

        Assert.Equal(CloseAction.CloseWindow, outcome.Action);
        Assert.Equal(20, outcome.Window);
        Assert.Equal(CloseState.Dimmed, button.State); // used up: a second press sends nothing
        Assert.Equal(CloseAction.Nothing, button.Press().Action);

        // A website pick with the add-on closes the tab the click went to, through the add-on, and without the add-on is dimmed.
        button.PickClicked(Site, Forward(5), "p1:7");
        var tab = button.Press();
        Assert.Equal((CloseAction.CloseTab, "p1:7"), (tab.Action, tab.TabKey));

        var offline = new Facts { AddonConnected = false };
        var without = new CloseButton(offline);
        without.PickClicked(Site, Forward(5), "p1:7");
        Assert.Equal(CloseState.Dimmed, without.State);
        Assert.Equal(CloseAction.Nothing, without.Press().Action);

        // The island's own window is never a target.
        var own = new CloseButton(new Facts());
        own.PickClicked(Program, Forward(999), null);
        Assert.Equal(CloseState.Dimmed, own.State);
    }

    [Fact]
    public void A_New_Summon_Dims_It_Again()
    {
        var button = new CloseButton(new Facts());
        button.PickClicked(Program, Forward(10), null);
        Assert.Equal(CloseState.Ready, button.State);

        button.NewShowing();

        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Administrator_Window_Is_Refused_With_A_Reason()
    {
        var facts = new Facts();
        facts.Admin.Add(10);
        var button = new CloseButton(facts);

        button.PickClicked(Program, Forward(10), null);

        // Dimmed for a reason the person can read on the second text line; pressing it does nothing but say so.
        Assert.Equal(CloseState.NeedsAdmin, button.State);
        Assert.Equal("runs as administrator", button.Line);
        var outcome = button.Press();
        Assert.Equal(CloseAction.Refused, outcome.Action);
        Assert.Equal("CLOSE_NEEDS_ADMIN", outcome.Refusal!.Code);
        Assert.Contains("Alpha runs as administrator", outcome.Refusal.Message);
        Assert.Contains("Close it from its own window", outcome.Refusal.Message);

        // A window that became an administrator's after the click is checked again at the press.
        var late = new Facts();
        var after = new CloseButton(late);
        after.PickClicked(Program, Forward(20), null);
        late.Admin.Add(20);
        Assert.Equal(CloseAction.Refused, after.Press().Action);
    }

    [Fact]
    public void A_Disabled_Window_Is_Sent_Nothing()
    {
        var facts = new Facts();
        var button = new CloseButton(facts);
        button.PickClicked(Program, Forward(10), null);
        facts.Disabled.Add(10); // its own question is open

        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Equal(CloseState.Ready, button.State); // still armed: when the question is answered the press works

        facts.Disabled.Remove(10);
        Assert.Equal(CloseAction.CloseWindow, button.Press().Action);

        // A window that vanished meanwhile is not asked; the button is dimmed.
        button.PickClicked(Program, Forward(30), null);
        facts.Existing.Remove(30);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Equal(CloseState.Dimmed, button.State);
    }

    [Fact]
    public void Folder_Pick_Has_No_Close()
    {
        var button = new CloseButton(new Facts());

        button.PickClicked(Folder, Forward(10), null);

        Assert.Equal(CloseState.Dimmed, button.State); // closing an Explorer window closes every tab in it
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }
}
