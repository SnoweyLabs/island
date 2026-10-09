using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

/// <summary>The order of the pages, changed by dragging in "Your pages" (Dan, 2026-10-09, version 1.0.1): the number keys follow it.</summary>
public class PageOrderTests
{
    [Fact]
    public void Moving_Terminals_To_The_Top_Makes_It_Page_One_And_Is_Saved()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files);
        var areas = new List<SettingsArea>();
        session.Changed += areas.Add;

        // Dan's example: Terminals on 1, Vibe coding on 2, Media on 3.
        Assert.True(session.MovePage(PageIds.Terminals, 0).Changed);
        Assert.True(session.MovePage(PageIds.Vibe, 1).Changed);
        Assert.False(session.MovePage(PageIds.Media, 2).Changed); // Media, first before, is third now already

        Assert.Equal([PageIds.Terminals, PageIds.Vibe, PageIds.Media], session.Pages.Pages.Take(3).Select(p => p.Id));
        Assert.Equal(1, session.Pages.DigitFor(PageIds.Terminals));
        Assert.Equal(2, session.Pages.DigitFor(PageIds.Vibe));
        Assert.Equal(3, session.Pages.DigitFor(PageIds.Media));
        Assert.Equal([SettingsArea.Pages, SettingsArea.Pages], areas);

        // Saved in that order, and read back in that order; every page is still there, once.
        var saved = PageStore.Load(files.PagesPath).Store.Pages.Select(p => p.Id).ToList();
        Assert.Equal(session.Pages.Pages.Select(p => p.Id), saved);
        Assert.Equal(Pages.BuiltIn.Select(p => p.Id).Order(), saved.Order());
    }

    [Fact]
    public void A_Move_To_Where_It_Is_Writes_Nothing_And_A_Missing_Page_Is_Refused()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files);
        var first = session.Pages.Pages[0].Id;

        var same = session.MovePage(first, 0);
        Assert.True(same.Ok);
        Assert.False(same.Changed);
        Assert.False(File.Exists(files.PagesPath));

        var missing = session.MovePage("no-such-page", 0);
        Assert.False(missing.Ok);
        Assert.False(File.Exists(files.PagesPath));
    }

    [Fact]
    public void An_Index_Past_Either_End_Means_That_End()
    {
        var store = PageStore.Default;
        var first = store.Pages[0].Id;
        var last = store.Pages[^1].Id;
        Assert.Equal(first, store.Move(first, 99).Store.Pages[^1].Id);
        Assert.Equal(last, store.Move(last, -5).Store.Pages[0].Id);
        Assert.Equal(store.Pages.Count, store.Move(last, -5).Store.Pages.Count);
    }
}
