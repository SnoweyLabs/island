using System.Windows.Automation;
using System.Windows.Automation.Peers;
using Island.App.Visuals;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 4: the island's real root element (linked from src/Island.App/Visuals/IslandRoot.cs, plain WPF) run through a hide and a summon, which the earlier rounds could only read. No window is shown; the peer is
/// made by hand (what a screen reader does by asking), and what it would read is read from the peer. A change that raises an event cannot be counted without a client, so what is checked is what the name is
/// and when <c>Describe</c> takes the change as new.
/// </summary>
public class Round4IslandRootTests
{
    private static string Told(IslandRoot root) => UIElementAutomationPeer.CreatePeerForElement(root).GetName();

    /// <summary>Held: the words are what the peer reads; the same words again change nothing; other words replace them.</summary>
    [Fact]
    public void The_Peer_Reads_The_Words_And_The_Same_Words_Again_Change_Nothing()
    {
        Sta.Run(() =>
        {
            var root = new IslandRoot();
            Assert.Equal("Island", Told(root));
            root.Describe("Island, Apps: Alpha, open, 1 of 5", alert: false);
            Assert.Equal("Island, Apps: Alpha, open, 1 of 5", Told(root));
            root.Describe("Island, Apps: Alpha, open, 1 of 5", alert: false);
            Assert.Equal("Island, Apps: Alpha, open, 1 of 5", root.Description);
            root.Describe("   ", alert: false);
            Assert.Equal("Island", Told(root)); // empty words are the plain name
        });
    }

    /// <summary>Held (ease-3-4): after the island hides and is summoned again, the same words are taken as new (the description is the plain name in between), so a change is raised again.</summary>
    [Fact]
    public void After_A_Hide_The_Same_Words_Are_A_Change_Again()
    {
        Sta.Run(() =>
        {
            var root = new IslandRoot();
            root.Describe("Island, Apps: Alpha, open, 1 of 5", alert: false);
            root.Forget();
            Assert.Equal("Island", Told(root));
            root.Describe("Island, Apps: Alpha, open, 1 of 5", alert: false);
            Assert.Equal("Island, Apps: Alpha, open, 1 of 5", Told(root));
        });
    }

    /// <summary>Held: a notice (assertive) followed, after a hide, by the page's words (polite) leaves the root polite, so a later page change is not read out over what the person is doing.</summary>
    [Fact]
    public void The_Live_Setting_Follows_The_Last_Words_After_A_Notice_And_A_Hide()
    {
        Sta.Run(() =>
        {
            var root = new IslandRoot();
            root.Describe("alpha has finished", alert: true);
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(root));
            root.Forget();
            root.Describe("Island, Apps: Alpha, open, 1 of 5", alert: false);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(root));
        });
    }

    /// <summary>
    /// Held, a remark for the register: after <c>Forget</c> the peer reads "Island" but <c>AutomationProperties.Name</c> of the element still holds the old words until the next <c>Describe</c>. A client that reads the
    /// attached property and not the peer (nothing in WPF does) would see the old words in a hidden island; the island is hidden and not in the tree of a client then, so nothing is told.
    /// </summary>
    [Fact]
    public void After_A_Forget_The_Attached_Name_Still_Holds_The_Old_Words_And_The_Peer_Does_Not()
    {
        Sta.Run(() =>
        {
            var root = new IslandRoot();
            root.Describe("Island, Apps: Alpha, open, 1 of 5", alert: false);
            root.Forget();
            Assert.Equal("Island, Apps: Alpha, open, 1 of 5", AutomationProperties.GetName(root));
            Assert.Equal("Island", Told(root));
        });
    }
}
