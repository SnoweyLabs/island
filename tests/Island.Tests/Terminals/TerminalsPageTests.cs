using Island.Core;
using Island.Core.SettingsEdit;
using Island.Core.Terminals;
using Island.Tests.SettingsEdit;

namespace Island.Tests.Terminals;

/// <summary>WORK-ORDER-11 section 1, the parts the main session builds: how the machine treats a page that fills itself, and that no pick can live on it.</summary>
public class TerminalsPageTests
{
    private static Item Tile(long key, string title = "t") => new(title, "terminal", "Te", 100, WindowKey: key);

    private static (IslandMachine M, Clock C, List<Item> Items) OnTerminalsPage(params long[] keys)
    {
        var items = keys.Select(k => Tile(k)).ToList();
        var machine = new IslandMachine(60, Pages.BuiltIn, page => page.Id == PageIds.Terminals ? items : Pages.PlaceholderItems(page));
        var clock = new Clock(machine);
        machine.MainKey(clock.Now);
        clock.Run(2000);
        machine.PageKey(PageIds.Terminals, clock.Now);
        clock.Run(2000);
        Assert.Equal(PageIds.Terminals, machine.ContentsPageId);
        Assert.True(machine.IsAtRest);
        return (machine, clock, items);
    }

    [Fact]
    public void The_Selection_Follows_Its_Window_Not_Its_Place()
    {
        var (m, c, items) = OnTerminalsPage(1, 2, 3);
        m.MoveSelection(1, c.Now);
        Assert.Equal(2, m.ContentsItems[m.SelectedItem].WindowKey);

        items.Insert(0, Tile(9)); // a window came on the left of the selected one
        m.SelfFillingItemsChanged(c.Now);
        Assert.Equal(2, m.ContentsItems[m.SelectedItem].WindowKey);
        Assert.Equal(2, m.SelectedItem);

        items.RemoveAt(2); // the selected window itself closed: the tile now in its place (key 3)
        m.SelfFillingItemsChanged(c.Now);
        Assert.Equal(2, m.SelectedItem);
        Assert.Equal(3, m.ContentsItems[m.SelectedItem].WindowKey);

        items.RemoveAt(2); // and it was the last one: the selection goes to the last tile
        m.SelfFillingItemsChanged(c.Now);
        Assert.Equal(1, m.SelectedItem);
        Assert.Equal(1, m.ContentsItems[m.SelectedItem].WindowKey);
    }

    [Fact]
    public void A_New_Window_Widens_The_Capsule_Without_A_Page_Swap()
    {
        var (m, c, items) = OnTerminalsPage(1, 2);
        m.MoveSelection(1, c.Now);
        var before = m.CapsuleTargetWidth;
        var swaps = m.PageChangeCount;
        var changedAt = m.ContentsChangedAtMs;

        items.Add(Tile(3));
        m.SelfFillingItemsChanged(c.Now);
        Assert.Equal(before + LookConstants.WidthItemPitch, m.CapsuleTargetWidth, 3);
        Assert.Equal(swaps, m.PageChangeCount);
        Assert.True(m.ContentsVisible); // the contents did not go out and come back
        Assert.Equal(changedAt, m.ContentsChangedAtMs);
        Assert.Equal(1, m.SelectedItem); // the selection did not jump to the first tile

        var width = m.DrawnWidth;
        c.Run(2000);
        Assert.True(m.DrawnWidth > width, "the width goes to its new value through the spring");
        Assert.Equal(m.CapsuleTargetWidth, m.DrawnWidth, 1);
        Assert.True(m.IsAtRest);
    }

    [Fact]
    public void A_Change_Does_Not_Count_As_Use_So_The_Island_Still_Leaves_By_Itself()
    {
        var items = new List<Item> { Tile(1) };
        var machine = new IslandMachine(2, Pages.BuiltIn, page => page.Id == PageIds.Terminals ? items : Pages.PlaceholderItems(page));
        var clock = new Clock(machine);
        machine.MainKey(clock.Now);
        clock.Run(1500);
        machine.PageKey(PageIds.Terminals, clock.Now);
        clock.Run(1000);
        for (var i = 0; i < 6; i++)
        {
            items.Add(Tile(10 + i));
            machine.SelfFillingItemsChanged(clock.Now);
            clock.Run(300);
        }

        clock.Run(6000);
        Assert.Equal(IslandPhase.Hidden, machine.Phase);
    }

    [Fact]
    public void A_Live_Tile_Is_Never_Taken_For_A_Pick()
    {
        var (m, c, _) = OnTerminalsPage(1, 2);
        Assert.False(m.AskRemove(c.Now)); // a first Delete asks about nothing: this tile is no pick
        Assert.Null(m.PendingDeleteId);
        Assert.Null(m.ConfirmRemove(c.Now));
        Assert.All(m.ContentsItems, i => Assert.Null(i.PickId));
    }

    [Fact]
    public void No_Pick_Can_Live_On_This_Page()
    {
        var onIt = new Pick("program:alpha", PickKind.Program, "Alpha", PageIds.Terminals, ExeName: "alpha.exe");
        var elsewhere = new Pick("program:beta", PickKind.Program, "Beta", PageIds.Apps, ExeName: "beta.exe");

        // A store never holds one, however it is made.
        Assert.Single(new PickStore([onIt, elsewhere]).Picks);
        var store = new PickStore([elsewhere]);
        store.Add(onIt, out var added);
        Assert.False(added);
        Assert.Same(store, store.Add(onIt, out _));
        Assert.Equal(PageIds.Apps, store.Move("program:beta", PageIds.Terminals).ById("program:beta")!.PageId);
        Assert.Equal(store.Picks, store.ReplacePage(PageIds.Terminals, [onIt]).Picks);

        // A file that names the page loads without that pick, and loading changes nothing on disk.
        var json = new PickStore([elsewhere]).ToJson().Replace("\"page\": \"apps\"", "\"page\": \"terminals\"");
        var load = PickStore.Parse(json);
        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Empty(load.Store.Picks);
        Assert.False(PageIds.CanHoldPicks(PageIds.Terminals));
        Assert.True(PageIds.CanHoldPicks(PageIds.Apps));

        // The settings session refuses to put or move anything there, and offers no starter list for it.
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));
        var pick = session.Picks.Picks[0];
        Assert.False(session.MovePick(pick.Id, PageIds.Terminals).Ok);
        Assert.Equal(PageIds.Terminals, "terminals");
        Assert.Equal(pick.PageId, session.Picks.ById(pick.Id)!.PageId);
        Assert.False(session.AddSite(PageIds.Terminals, "example.org").Added);
        Assert.Null(session.AskRestore(PageIds.Terminals));
        Assert.Empty(StarterPicks.Build(Fixtures.EverythingInstalled).Where(p => p.PageId == PageIds.Terminals));
        Assert.NotEqual(PageIds.Terminals, PickSuggest.PageFor(PickKind.Program, "WindowsTerminal.exe", null));
    }

    [Fact]
    public void The_Page_Has_No_Plus_And_No_Second_Row()
    {
        var (m, c, _) = OnTerminalsPage(1, 2);
        m.ToggleSecondRow(c.Now);
        c.Run(500);
        Assert.False(m.SecondRowOpen);
        Assert.All(m.ContentsItems, i => Assert.False(i.IsPlus));
        Assert.Empty(PlusRowItemsOf(m));
    }

    private static IEnumerable<Item> PlusRowItemsOf(IslandMachine m) => m.ContentsItems.Where(i => i.IsPlus);

    [Fact]
    public void Delete_Space_And_Down_Do_Nothing_Here()
    {
        // The app fills the context like this on the Terminals page: a tile that is no pick, no media, and a + that does nothing.
        var context = new KeyContext
        {
            HasKeyboard = true, RowIsCurrentPage = true, Selected = SelectedTile.Pick, PageCount = 6, PlusClickActs = false, MediaCanPlayPause = false,
        };
        foreach (var key in new[] { KeyCodes.Delete, KeyCodes.Space, KeyCodes.Down })
        {
            var decision = IslandKeys.Decide(new KeyInput(key), context);
            Assert.True(decision.Action is KeyAction.CountsAsUse or KeyAction.AskRemove, $"{key}: {decision.Action}");
            Assert.NotEqual(KeyAction.OpenSecondRow, decision.Action);
            Assert.NotEqual(KeyAction.OpenSecondRowAndEnter, decision.Action);
            Assert.NotEqual(KeyAction.PlayPause, decision.Action);
            Assert.NotEqual(KeyAction.ConfirmRemove, decision.Action);
        }

        // Delete asks about nothing (see A_Live_Tile_Is_Never_Taken_For_A_Pick); Enter and the arrows work as on any page.
        Assert.Equal(KeyAction.Activate, IslandKeys.Decide(new KeyInput(KeyCodes.Enter), context).Action);
    }
}
