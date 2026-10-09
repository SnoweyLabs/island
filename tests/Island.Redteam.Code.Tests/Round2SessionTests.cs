using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 2 (code-2-*): the switched-off picks that SettingsSession keeps for the visit (repair of ease-1-3, 19a61f0) and what "Restore the original keys" counts (85452c5). Invented names only.</summary>
public sealed class Round2SessionTests
{
    private sealed class QuietRegistrar : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            return true;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }

    private static (SettingsSession Session, Scratch Dir) Make(PageStore pages, PickStore picks, Settings? settings = null)
    {
        var dir = new Scratch();
        var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
        var session = new SettingsSession(files, new SettingsLoad(settings ?? Settings.Defaults, SettingsStatus.Loaded, null), new PageStoreLoad(pages, PageStoreStatus.Loaded, null),
            new PickStoreLoad(picks, PickStoreStatus.Loaded, null), new QuietRegistrar(), () => [], () => false);
        return (session, dir);
    }

    [Fact]
    public void A_Hand_Added_Pick_Switched_Off_Is_A_Faint_Row_Until_Switched_On_Again_And_Is_Not_Doubled()
    {
        var pages = PageStore.Default.Create("Alpha page", "#FFAA33").Store;
        var picks = new PickStore([Pick.ForSite("Alpha", "alpha.example.org", "page-1")]);
        var (session, dir) = Make(pages, picks);
        using (dir)
        {
            var on = session.PickRows("page-1").Single();
            Assert.True(on.On);
            Assert.True(session.SetPick(on, false).Changed);
            var off = session.PickRows("page-1").Single();
            Assert.False(off.On);
            session.SetPick(off, false); // switching a faint row off again must not add a second one
            Assert.Single(session.PickRows("page-1"));
            Assert.True(session.SetPick(session.PickRows("page-1").Single(), true).Changed);
            var back = session.PickRows("page-1").Single();
            Assert.True(back.On);
        }
    }

    [Fact]
    public void Defect_A_Switched_Off_Pick_Of_A_Deleted_Page_Shows_Again_On_The_Next_Page_That_Gets_The_Same_Id()
    {
        // code-2-5 (LOW). SettingsSession._switchedOff keeps the Pick (with its PageId) for the visit and PickRows(pageId) adds every kept pick whose PageId equals the page asked for
        // and that is not in the store. A page's id is the first free "page-N" (PageStore.NextId), so after DeletePage("page-1") the next CreatePage gets "page-1" again and the new,
        // empty page shows the faint chip of a pick that was removed with its page (the person confirmed "your picks on this page will be removed"). Switching it on puts a pick
        // that was deleted back, on a page that is not the one it was on. Expected: a deleted page's kept picks are dropped with the page.
        var pages = PageStore.Default.Create("Alpha page", "#FFAA33").Store;
        var picks = new PickStore([Pick.ForSite("Alpha", "alpha.example.org", "page-1")]);
        var (session, dir) = Make(pages, picks);
        using (dir)
        {
            Assert.True(session.SetPick(session.PickRows("page-1").Single(), false).Changed);
            Assert.True(session.DeletePage("page-1").Changed);
            Assert.True(session.CreatePage("Beta page", "#33AAFF").Changed);
            Assert.NotNull(session.Pages.ById("page-1"));
            Assert.Empty(session.PickRows("page-1"));
        }
    }

    [Fact]
    public void Keys_Set_By_You_Counts_What_Restore_All_Keys_Takes_Away_And_Nothing_Else()
    {
        var combo = HotkeyCombo.Parse("Ctrl+Alt+Space");
        var none = Make(PageStore.Default, PickStore.Empty);
        using (none.Dir) Assert.Equal(0, none.Session.KeysSetByYou);

        var settings = Settings.Defaults with { ShowHide = combo, ModeKey = HotkeyCombo.Parse("Ctrl+Alt+M") };
        settings = settings.WithPickKey("program:alpha", HotkeyCombo.Parse("Ctrl+Alt+A"));
        var picks = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);
        var some = Make(PageStore.Default, picks, settings);
        using (some.Dir)
        {
            var counted = some.Session.KeysSetByYou;
            Assert.True(some.Session.RestoreAllKeys().Changed);
            Assert.Equal(0, some.Session.KeysSetByYou);
            Assert.Equal(Settings.Defaults.ShowHide, some.Session.Settings.ShowHide);
            Assert.Empty(some.Session.Settings.PickKeys);
            Assert.Null(some.Session.Settings.ModeKey);
            Assert.Equal(3, counted);
        }
    }
}
