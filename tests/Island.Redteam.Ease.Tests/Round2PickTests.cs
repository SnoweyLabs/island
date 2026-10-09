using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 2: the hand-added pick that stays as a faint chip for the visit (commit 19a61f0), and what the repairs of round 1 did to the files the person's picks live in (b34a7d5).
/// Core only: no window. Invented names only.
/// </summary>
public class Round2PickTests
{
    private static KeyPress CtrlAlt(int vk) => new(vk, HotkeyModifiers.Control | HotkeyModifiers.Alt, false);

    private static Pick AlphaOf(SettingsSession session) => session.Picks.Picks.Single(p => p.Name == "Alpha");

    // ---- held -------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Held: a second visit (a new session over the same files) shows no faint chip: the chip lives in the session only, as its comment says.</summary>
    [Fact]
    public void A_Second_Visit_Shows_No_Faint_Chip_For_A_Pick_Switched_Off_In_The_First()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var page = session.Pages.Pages[^1].Id;
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: false);
        Assert.Contains(session.PickRows(page), r => r.Pick.Name == "Alpha" && !r.On);

        var again = new SettingsSession(fixture.Files, new SettingsLoad(session.Settings, SettingsStatus.Loaded, null), new PageStoreLoad(session.Pages, PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Load(fixture.Files.PicksPath).Store, PickStoreStatus.Loaded, null), new AcceptingRegistrar(), () => [], () => false, null, () => [], null, SceneStore.Load(fixture.Files.ScenesPath!), null, null);
        Assert.DoesNotContain(again.PickRows(page), r => r.Pick.Name == "Alpha");
    }

    /// <summary>Held: switched off and on again twice gives one chip, never two; a pick that is on is not also shown as a faint one.</summary>
    [Fact]
    public void Off_And_On_Again_Twice_Gives_One_Chip_And_Never_Two()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var page = session.Pages.Pages[^1].Id;
        for (var i = 0; i < 2; i++)
        {
            session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: false);
            Assert.Single(session.PickRows(page), r => r.Pick.Name == "Alpha");
            session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: true);
            Assert.Single(session.PickRows(page), r => r.Pick.Name == "Alpha");
        }

        Assert.True(session.PickRows(page).Single(r => r.Pick.Name == "Alpha").On);
    }

    /// <summary>Held: a scene that holds the pick keeps it through off and on (scenes keep copies of picks, so the scene is not touched by switching).</summary>
    [Fact]
    public void A_Scene_Keeps_The_Pick_Through_Off_And_On()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var page = session.Pages.Pages[^1].Id;
        Assert.True(session.CreateScene("Mix").Ok);
        var scene = session.Scenes.Items.Single();
        Assert.True(session.SetSceneThing(scene.Id, AlphaOf(session), on: true).Ok);
        var before = session.Scenes.ById(scene.Id)!.Things.Count;

        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: false);
        Assert.Equal(before, session.Scenes.ById(scene.Id)!.Things.Count);
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: true);
        Assert.Equal(before, session.Scenes.ById(scene.Id)!.Things.Count);
    }

    /// <summary>Held: a faint chip has no Move control and no key control (they are for a pick that is on), so it cannot be moved to a page while it is off.</summary>
    [Fact]
    public void A_Pick_That_Is_Off_Is_Not_Moved_By_Switching_It_On()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var page = session.Pages.Pages[^1].Id;
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: false);
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: true);
        Assert.Equal(page, AlphaOf(session).PageId);
    }

    // ---- ease-2-5 -----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-2-5 (LOW). A page's id is the first free "page-N". Switch a hand-added pick off (a faint chip stays in the session), remove its page (the question says the picks on it go with it;
    /// the faint one is not counted), make a new page: it gets the same id, and the faint chip of the page that is gone appears on it. Pressing it puts that pick on the new page.
    /// Expected: a page that is removed takes its faint chips with it.
    /// </summary>
    [Fact]
    public void Defect_A_Faint_Chip_Of_A_Removed_Page_Appears_On_A_New_Page_That_Gets_The_Same_Id()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var old = session.Pages.Pages[^1];
        session.SetPick(session.PickRows(old.Id).Single(r => r.Pick.Name == "Alpha"), on: false);
        Assert.True(session.DeletePage(old.Id).Ok);

        var created = session.CreatePage("Work", "#19E6B3");
        Assert.True(created.Ok, created.Refusal);
        var fresh = session.Pages.Pages[^1];
        Assert.Equal(old.Id, fresh.Id); // the id is reused (PageStore.NextId): the premise of the finding

        Assert.DoesNotContain(session.PickRows(fresh.Id), r => r.Pick.Name == "Alpha");
    }

    // ---- ease-2-6 -----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-2-6 (MEDIUM). The faint chip is the way back for a hand-added pick (ease-1-3), but the pick's own key is let go when it is switched off (ReleaseKeysOfMissingPicks) and does not come back
    /// when the pick does: the person set a key, pressed a chip that says "off, press to switch", pressed it again, and the key is gone with no word. Expected: switching it on again gives
    /// the key back when it is still free (or the way back says that the key is gone).
    /// </summary>
    [Fact]
    public void Defect_A_Pick_Switched_Off_And_On_Again_Gets_Its_Key_Back()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var page = session.Pages.Pages[^1].Id;
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        Assert.NotNull(session.Settings.PickKeyFor(id));

        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: false);
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: true);

        Assert.NotNull(session.Settings.PickKeyFor(id));
    }

    /// <summary>The premise of ease-2-6, as a plain fact: while the pick is off its key is gone (the key goes back to Windows).</summary>
    [Fact]
    public void While_A_Pick_Is_Off_Its_Key_Is_Let_Go()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var page = session.Pages.Pages[^1].Id;
        var id = AlphaOf(session).Id;
        Assert.True(session.PressKey(id, CtrlAlt(0x4A)).Changed);
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Name == "Alpha"), on: false);
        Assert.Null(session.Settings.PickKeyFor(id));
    }

    // ---- ease-2-7 -----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-2-7 (MEDIUM). Commit b34a7d5 made Pick.IsStorable refuse a name with U+FFFD, and PickStore.Load calls IsStorable for every pick: one pick with such a name in a picks.json written
    /// by an earlier build (a program whose name came through a lossy conversion, a pasted character) now makes the WHOLE file unreadable: the island starts with no picks at all and the
    /// screen locks the file. Expected: the file loads (the rule is for what is added, not for what is already kept).
    /// </summary>
    [Fact]
    public void Defect_One_Pick_With_A_Replacement_Character_In_Its_Name_Makes_The_Whole_Picks_File_Unreadable()
    {
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null)]);
        var json = store.ToJson().Replace("Beta", "Be�ta", StringComparison.Ordinal);

        var load = PickStore.Parse(json);

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(2, load.Store.Picks.Count);
    }

    /// <summary>The same file without the odd character loads whole (the control of the test above).</summary>
    [Fact]
    public void The_Same_File_Without_The_Odd_Character_Loads_Whole()
    {
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null)]);
        var load = PickStore.Parse(store.ToJson());
        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(2, load.Store.Picks.Count);
    }
}
