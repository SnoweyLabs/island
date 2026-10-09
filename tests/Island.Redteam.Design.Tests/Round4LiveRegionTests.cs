using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Island.SettingsUi;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 4: the section heading as a live region (ease-3-13's repair, <c>SettingsView.Rebuild</c>): no pixel changes (the pictures say so); whether the event it raises can reach anyone.</summary>
public class Round4LiveRegionTests(ITestOutputHelper output)
{
    /// <summary>Learn (UIElementAutomationPeer.FromElement, read 2026-10-07): it returns null "if the peer was not created by the CreatePeerForElement method". A heading is made again by every Rebuild.</summary>
    [Fact]
    public void Record_That_A_Heading_Built_By_A_Rebuild_Has_No_Peer_Until_Someone_Creates_One()
    {
        var (fresh, created, headingPeer) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.General;
            SettingsWorld.Layout(stage);
            var heading = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => t.FontSize >= 24);
            var viaFrom = UIElementAutomationPeer.FromElement(heading);
            var tb = new TextBlock { Text = "x" };
            var before = UIElementAutomationPeer.FromElement(tb);
            var made = UIElementAutomationPeer.CreatePeerForElement(tb);
            output.WriteLine($"heading \"{heading.Text}\": FromElement is {(viaFrom is null ? "null" : "a peer")}; a fresh TextBlock: FromElement {(before is null ? "null" : "a peer")}, CreatePeerForElement {(made is null ? "null" : "a peer")}, FromElement afterwards {(UIElementAutomationPeer.FromElement(tb) is null ? "null" : "the same peer")}");
            return (before is null, made is not null, viaFrom is null);
        });
        Assert.True(fresh && created);
        Assert.True(headingPeer, "a heading already has a peer without a client: then the event of the repair is raised");
    }

    /// <summary>
    /// FINDING design-4-6 (LOW, UNVERIFIED without a screen reader; EASE's ground, found because the brief names the heading). Expected: the repair of ease-3-13 raises LiveRegionChanged for a heading that is new on every Rebuild,
    /// so it makes its peer (<c>CreatePeerForElement</c>) when a client listens, as the documented pattern for a peer that may not exist does. Today it reads <c>FromElement(heading) is { } peer</c>: null for the new heading, so nothing is raised.
    /// </summary>
    [Fact]
    public void Defect_The_New_Heading_Gets_A_Peer_Before_The_Live_Region_Event_Is_Raised()
    {
        var src = Src.Read("Island.SettingsUi/SettingsView.cs");
        Assert.Contains("CreatePeerForElement(heading)", src);
    }
}
