using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

public class PicksOnIslandTests
{
    private static IReadOnlyList<InstalledProgram> Installed => Fixtures.EverythingInstalled;

    private static PickStore Starter => new(StarterPicks.Build(Installed));

    [Fact]
    public void Rows_Show_The_Page_Picks_On()
    {
        var rows = PicksOnIsland.RowsFor(PageIds.Vibe, Starter, Installed);

        Assert.Equal(Starter.ForPage(PageIds.Vibe), rows.Select(r => r.Pick));
        Assert.All(rows, r => Assert.True(r.On));
        Assert.Empty(PicksOnIsland.RowsFor("page-1", Starter, Installed));
    }

    [Fact]
    public void Off_Removes_The_Pick_And_On_Puts_A_Starter_Pick_Back()
    {
        // Arrange
        var store = Starter;
        var row = PicksOnIsland.RowsFor(PageIds.Vibe, store, Installed)[0];

        // Act: off, then the page still lists it, as off.
        var off = PicksOnIsland.Switch(store, row, on: false);
        var rows = PicksOnIsland.RowsFor(PageIds.Vibe, off, Installed);

        // Assert
        Assert.Null(off.ById(row.Pick.Id));
        Assert.Equal(store.Picks.Count - 1, off.Picks.Count);
        Assert.Contains(rows, r => r.Pick.Id == row.Pick.Id && !r.On);
        Assert.Equal(store.ForPage(PageIds.Vibe).Count, rows.Count);

        // On again: the pick is back on its page; other pages never moved.
        var back = PicksOnIsland.Switch(off, rows.First(r => r.Pick.Id == row.Pick.Id), on: true);
        Assert.Equal(row.Pick, back.ById(row.Pick.Id));
        Assert.Equal(store.ForPage(PageIds.Apps), back.ForPage(PageIds.Apps));
        Assert.Equal(store.Picks.Count, back.Picks.Count);
    }

    [Fact]
    public void A_Pick_That_Is_Not_On_The_Starter_List_Is_Gone_When_Switched_Off()
    {
        var mine = Pick.ForProgram("Paint", PageIds.Vibe, "mspaint.exe", null);
        var store = Starter.Add(mine, out _);

        var rows = PicksOnIsland.RowsFor(PageIds.Vibe, store, Installed);
        var off = PicksOnIsland.Switch(store, rows.First(r => r.Pick.Id == mine.Id), on: false);

        Assert.DoesNotContain(PicksOnIsland.RowsFor(PageIds.Vibe, off, Installed), r => r.Pick.Id == mine.Id);
    }

    [Fact]
    public void A_Starter_Pick_That_Dan_Moved_To_Another_Page_Is_Not_Offered_Again()
    {
        var store = Starter.Move("program:notepad", PageIds.Vibe);

        Assert.DoesNotContain(PicksOnIsland.RowsFor(PageIds.Apps, store, Installed), r => r.Pick.Id == "program:notepad");
        Assert.Contains(PicksOnIsland.RowsFor(PageIds.Vibe, store, Installed), r => r.Pick.Id == "program:notepad" && r.On);
    }

    [Fact]
    public void The_Restore_Question_Counts_What_Is_Thrown_Away_And_What_Comes_Back()
    {
        var store = Starter.Remove("program:notepad").Add(Pick.ForProgram("Paint", PageIds.Apps, "mspaint.exe", null), out _);
        var apps = Pages.Get(PageIds.Apps);

        var question = PicksOnIsland.AskRestore(apps, store, Installed);

        Assert.Equal(store.ForPage(PageIds.Apps).Count, question.YourPicks);
        Assert.Equal(Starter.ForPage(PageIds.Apps).Count, question.StarterPicks);
        Assert.Contains($"Your {question.YourPicks} picks", question.Text);
        Assert.Contains(question.StarterPicks.ToString(), question.Text);
        Assert.True(PicksOnIsland.CanRestore(apps));
        Assert.False(PicksOnIsland.CanRestore(PageStore.Default.Create("Games", "#7CE04A").Page!));
    }
}
