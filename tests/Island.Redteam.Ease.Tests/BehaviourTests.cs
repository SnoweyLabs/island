using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>What the settings screen does with the things a person has saved, and the questions it asks (or does not) before it throws something away.</summary>
public class BehaviourTests
{
    private const int VkOne = 0x31;

    private static KeyPress CtrlAlt(int vk) => new(vk, HotkeyModifiers.Control | HotkeyModifiers.Alt, false);

    // ---- ease-1-3 ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-1-3 (MEDIUM). In "What goes on the island" every pick is a chip whose name says "Press to switch" and whose off state is a faint chip, as the first-start sentence says "Tap to switch
    /// any of them off". For a starter pick that is true. For a pick the person added by hand (a program, a folder, a site, a file) switching it "off" deletes it: no question, no faint chip
    /// left to switch it on again (PicksOnIsland's own comment: "a pick that is not on the starter list and was switched off is gone"). Expected: it stays on the page as an "off" row, or
    /// the screen asks first, as it does for removing a page, restoring a starter list or deleting a scene.
    /// </summary>
    [Fact]
    public void Defect_A_Hand_Added_Pick_Switched_Off_Can_Be_Switched_On_Again()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            var page = session.Pages.Pages[^1].Id;
            var row = session.PickRows(page).Single(r => r.Pick.Name == "Alpha");
            Assert.True(row.On);

            session.SetPick(row, on: false);

            Assert.Contains(session.PickRows(page), r => r.Pick.Name == "Alpha" && !r.On);
        });
    }

    [Fact]
    public void A_Starter_Pick_Switched_Off_Stays_As_A_Faint_Row_And_Comes_Back()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            var row = session.PickRows(PageIds.Apps).First(r => r.Pick.Name == "Discord");
            session.SetPick(row, on: false);
            var off = session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Discord");
            Assert.False(off.On);
            session.SetPick(off, on: true);
            Assert.True(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Discord").On);
        });
    }

    // ---- ease-1-4 ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-1-4 (MEDIUM). "Restore the original keys" (a small link under the page keys) clears the main key's change, every page key, every pick key, every scene key and the mode key at
    /// once, with no question and no way back; the labels beside it ("Restore default", "Clear") are one key each. Every other thing that throws away several saved things asks first.
    /// Expected: a yes/no question (the keyboard on "No"), or the keys still there.
    /// </summary>
    [Fact]
    public void Defect_Restore_The_Original_Keys_Asks_Before_It_Clears_Every_Key_The_Person_Set()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            Assert.True(session.PressKey("media", CtrlAlt(VkOne)).Changed);
            Assert.True(session.PressKey("folders", CtrlAlt(VkOne + 1)).Changed);
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);

            Tree.Click(view, "key:all");

            var asked = view.WantsEscape; // a question open (or a capture, or a panel): the screen is waiting for something
            var kept = session.Settings.KeyFor("media") is not null && session.Settings.KeyFor("folders") is not null;
            Assert.True(asked || kept, "two page keys were cleared at one press and nothing was asked");
        });
    }

    [Fact]
    public void Removing_A_Page_And_Restoring_A_Starter_List_Ask_First_And_Esc_Says_No()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var session = fixture.Session;
            var view = fixture.NewView();
            view.Section = SettingsSection.Pages;
            Tree.Layout(view, 1920, 1080);
            var count = session.Pages.Pages.Count;

            Tree.Click(view, "remove:page-1");
            Assert.True(view.WantsEscape);
            Assert.True(view.HandleEscape()); // Esc is "No"
            Assert.Equal(count, session.Pages.Pages.Count);

            view.Section = SettingsSection.OnTheIsland;
            Tree.Layout(view, 1920, 1080);
            var before = session.Picks.ForPage(PageIds.Media).Count;
            Tree.Click(view, "restore:media");
            Assert.True(view.WantsEscape);
            Assert.True(view.HandleEscape());
            Assert.Equal(before, session.Picks.ForPage(PageIds.Media).Count);
        });
    }

    // ---- time and findability ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Time_Before_The_Island_Leaves_Is_Adjustable_Findable_And_Reaches_Ten_Times_The_Default()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 1080);
            Assert.Equal(1, view.CountTextContaining("Idle time")); // the card's heading
            Assert.True(view.CountTextContaining("how long the island waits") >= 1); // the section's own sentence says where to look
            Assert.True(Settings.MaxSetIdleSeconds >= 10 * LookConstants.IdleSeconds, "WCAG 2.2.1: adjustable up to ten times the default");
            Assert.Equal(LookConstants.IdleSeconds, fixture.Session.Settings.IdleSeconds);
            Assert.True(fixture.Session.SetIdleSeconds(Settings.MaxSetIdleSeconds).Changed);
        });
    }

    [Fact]
    public void Esc_Is_The_Hosts_At_Rest_And_The_Screens_While_A_Key_Is_Being_Chosen()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Key;
            Tree.Layout(view, 1920, 1080);
            Assert.False(view.HandleEscape()); // nothing to cancel: the host closes the screen
            Tree.Click(view, "key:main"); // begins the wait for a combination
            Assert.True(view.WantsEscape);
            Assert.True(view.HandleEscape());
            Assert.False(view.WantsEscape);
            Assert.False(view.HandleEscape());
        });
    }
}
