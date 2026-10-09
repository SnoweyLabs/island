using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

public class SettingsEditTests
{
    [Fact]
    public void A_Pick_Can_Be_Moved_To_Another_Page()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var alpha = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var beta = Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null);
        var session = SessionFixtures.Open(files, picks: new PickStore([alpha, beta]));
        var changes = new List<SettingsArea>();
        session.Changed += changes.Add;

        var result = session.MovePick(alpha.Id, PageIds.Vibe);

        Assert.True(result.Changed && result.Ok);
        Assert.Equal(PageIds.Vibe, session.Picks.ById(alpha.Id)!.PageId);
        Assert.Equal(PageIds.Apps, session.Picks.ById(beta.Id)!.PageId); // nothing else moves
        Assert.Equal(1, session.Picks.Picks.Count(p => p.Id == alpha.Id)); // still on exactly one page
        Assert.Equal([SettingsArea.Picks], changes);
        Assert.Equal(PageIds.Vibe, PickStore.Load(files.PicksPath).Store.ById(alpha.Id)!.PageId); // saved at once

        // A page or a pick that is not there is refused with a reason and changes nothing.
        Assert.False(session.MovePick(alpha.Id, "no-such-page").Ok);
        Assert.False(session.MovePick("program:nobody", PageIds.Media).Ok);
        Assert.Equal(PageIds.Vibe, session.Picks.ById(alpha.Id)!.PageId);

        // Moving to the page it is on changes nothing.
        Assert.False(session.MovePick(alpha.Id, PageIds.Vibe).Changed);
    }
}
