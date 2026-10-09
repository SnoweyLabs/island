using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

public class StarterPicksTests
{
    [Fact]
    public void Restore_Replaces_Only_That_Page_Through_The_Editor()
    {
        // Arrange: Dan removed Notepad from Apps and added Paint to Vibe coding; a session holds his picks.
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var installed = Fixtures.EverythingInstalled;
        var starters = StarterPicks.Build(installed);
        var mine = new PickStore(starters).Remove("program:notepad").Add(Pick.ForProgram("Paint", PageIds.Vibe, "mspaint.exe", null), out _);
        var session = SessionFixtures.Open(files, picks: mine, installed: installed);
        var changes = new List<SettingsArea>();
        session.Changed += changes.Add;

        // Act: ask, then say yes for the Vibe coding page only.
        var question = session.AskRestore(PageIds.Vibe)!;
        var result = session.RestorePage(PageIds.Vibe);

        // Assert: Vibe coding is the starter list again, Apps keeps its removal, nothing else moved.
        Assert.Equal(mine.ForPage(PageIds.Vibe).Count, question.YourPicks);
        Assert.True(result.Ok && result.Changed);
        Assert.DoesNotContain(session.Picks.Picks, p => p.Id == "program:mspaint");
        Assert.Equal(starters.Where(p => p.PageId == PageIds.Vibe), session.Picks.ForPage(PageIds.Vibe));
        Assert.DoesNotContain(session.Picks.Picks, p => p.Id == "program:notepad");
        Assert.Equal(mine.ForPage(PageIds.Apps), session.Picks.ForPage(PageIds.Apps));
        Assert.Equal(mine.ForPage(PageIds.Media), session.Picks.ForPage(PageIds.Media));
        Assert.Equal([SettingsArea.Picks], changes);

        // It was saved: a restart sees the same picks.
        Assert.Equal(session.Picks.Picks, PickStore.Load(files.PicksPath).Store.Picks);
    }

    [Fact]
    public void A_Custom_Page_Has_No_Starter_List_To_Restore()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));
        session.CreatePage("Games", "#7CE04A");
        var id = session.Pages.Pages[^1].Id;

        Assert.Null(session.AskRestore(id));
        Assert.False(session.RestorePage(id).Ok);
    }
}
