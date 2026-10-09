using Island.Core;

namespace Island.Tests;

public class PickEditTests
{
    [Fact]
    public void The_Same_Thing_Cannot_Be_Picked_Twice()
    {
        var notepad = Pick.ForProgram("Notepad", PageIds.Apps, "notepad.exe", null);
        var store = new PickStore([]).Add(notepad, out var first);
        var again = store.Add(notepad with { PageId = PageIds.Vibe }, out var second);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(again.Picks);
        Assert.Equal(PageIds.Apps, again.Picks[0].PageId);

        // A pick that points at a path is not added at all.
        var rejected = store.Add(new Pick("program:x", PickKind.Program, "X", PageIds.Apps, ExeName: "C:\\x.exe"), out var bad);
        Assert.False(bad);
        Assert.Single(rejected.Picks);
    }

    [Fact]
    public void Add_Goes_To_The_Suggested_Page_Unless_Dan_Chooses()
    {
        Assert.Equal(PageIds.Media, PickSuggest.PageFor(PickKind.Site, null, "music.youtube.com"));
        Assert.Equal(PageIds.Media, PickSuggest.PageFor(PickKind.Site, null, "open.spotify.com"));
        Assert.Equal(PageIds.Browser, PickSuggest.PageFor(PickKind.Site, null, "example.org"));
        Assert.Equal(PageIds.Vibe, PickSuggest.PageFor(PickKind.Program, "powershell.exe", null));
        Assert.Equal(PageIds.Vibe, PickSuggest.PageFor(PickKind.Program, "Code.exe", null));
        Assert.Equal(PageIds.Folders, PickSuggest.PageFor(PickKind.Folder, null, null));
        Assert.Equal(PageIds.Apps, PickSuggest.PageFor(PickKind.Program, "mspaint.exe", null));

        var suggested = PickSuggest.PageFor(PickKind.Program, "powershell.exe", null);
        var chosen = PageIds.Apps;
        var store = new PickStore([]).Add(Pick.ForProgram("PowerShell", chosen, "powershell.exe", null), out _);
        Assert.NotEqual(suggested, store.Picks[0].PageId);
        Assert.Equal(chosen, store.Picks[0].PageId);
    }

    [Fact]
    public void Remove_Deletes_The_Pick_And_Nothing_Else()
    {
        var a = Pick.ForProgram("A", PageIds.Apps, "a.exe", null);
        var b = Pick.ForProgram("B", PageIds.Apps, "b.exe", null);
        var world = new PretendWorld { Windows = [Fixtures.Window(1, "a.exe")] };
        var recording = new RecordingOutside();
        var store = new PickStore([a, b]);

        var after = store.Remove(a.Id);

        Assert.Equal([b], after.Picks);
        Assert.Equal([a, b], store.Picks);              // the old store is untouched
        Assert.Single(world.Windows);                    // the window is still open: nothing was closed
        Assert.Empty(recording.Calls);                   // and nothing was asked of the outside world
        Assert.Equal(after.Picks, after.Remove("program:nothing-like-this").Picks);
    }

    [Fact]
    public void A_Pick_Is_On_Exactly_One_Page()
    {
        var a = Pick.ForProgram("A", PageIds.Apps, "a.exe", null);
        var store = new PickStore([a, Pick.ForProgram("B", PageIds.Vibe, "b.exe", null)]);

        var moved = store.Move(a.Id, PageIds.Browser);

        Assert.Equal(1, moved.Picks.Count(p => p.Id == a.Id));
        Assert.Equal(PageIds.Browser, moved.ById(a.Id)!.PageId);
        Assert.Equal(PageIds.Vibe, moved.ById("program:b")!.PageId);
        Assert.Equal(2, moved.Picks.Count);
    }

    [Fact]
    public void Dans_Choice_Beats_The_Suggestion()
    {
        var terminal = Pick.ForProgram("PowerShell", PageIds.Vibe, "powershell.exe", null); // suggested page: Vibe coding
        var store = new PickStore([terminal]).Move(terminal.Id, PageIds.Apps);               // Dan puts it on Apps

        // Starting again with the same list (a restart) and adding other things never moves it back.
        var restarted = PickStore.Parse(store.ToJson()).Store.Add(Pick.ForProgram("Code", PageIds.Vibe, "Code.exe", null), out _);
        Assert.Equal(PageIds.Apps, restarted.ById(terminal.Id)!.PageId);
        Assert.Equal(PageIds.Vibe, PickSuggest.PageFor(PickKind.Program, "powershell.exe", null));
    }

    [Fact]
    public void Restore_Replaces_Only_That_Page()
    {
        var starters = StarterPicks.Build(Fixtures.EverythingInstalled);
        var mine = new PickStore(starters)
            .Remove("program:notepad")
            .Add(Pick.ForProgram("Paint", PageIds.Vibe, "mspaint.exe", null), out _);

        var restored = mine.ReplacePage(PageIds.Vibe, starters);

        Assert.DoesNotContain(restored.Picks, p => p.Id == "program:mspaint");                        // the addition on that page is gone
        Assert.Equal(starters.Count(p => p.PageId == PageIds.Vibe), restored.ForPage(PageIds.Vibe).Count);
        Assert.DoesNotContain(restored.Picks, p => p.Id == "program:notepad");                        // another page keeps the removal
        Assert.Equal(mine.ForPage(PageIds.Apps), restored.ForPage(PageIds.Apps));
    }
}

public class PlusListTests
{
    [Fact]
    public void Lists_Open_Things_That_Are_Not_Picked()
    {
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForFolder("Documents", PageIds.Folders), Pick.ForSite("Site", "example.org", PageIds.Media)]);
        var open = new OpenSnapshot(
            [Fixtures.Window(1, "alpha.exe", 0), Fixtures.Window(2, "beta.exe", 1), Fixtures.Window(3, "beta.exe", 2), Fixtures.Window(4, "gamma.exe", 3)],
            [new FolderWindow(20, "Documents", "x", 0), new FolderWindow(21, "Downloads", "x", 1)],
            [Fixtures.Tab(1, "example.org", 5), Fixtures.Tab(2, "other.example", 6), Fixtures.Tab(3, "other.example", 7)],
            true);

        var list = PlusList.Candidates(open, store, []);

        Assert.DoesNotContain(list, e => e.Key == "program:alpha.exe");   // picked
        Assert.DoesNotContain(list, e => e.Key == "folder:Documents");    // picked
        Assert.DoesNotContain(list, e => e.Key == "site:example.org");    // picked
        var beta = Assert.Single(list, e => e.Key == "program:beta.exe");
        Assert.Equal(2, beta.Count);
        Assert.Equal("2 windows", beta.Detail);
        Assert.Equal([2L, 3L], beta.Targets);
        Assert.Contains(list, e => e.Key == "program:gamma.exe");
        Assert.Contains(list, e => e.Key == "folder:Downloads");
        var other = Assert.Single(list, e => e.Key == "site:other.example");
        Assert.Equal("2 tabs", other.Detail);
        Assert.Equal([7L, 6L], other.Targets);                              // newest tab first
    }

    [Fact]
    public void Tabs_Are_Offered_Only_While_The_Addon_Is_Connected_And_The_Own_Program_Never()
    {
        var open = new OpenSnapshot([Fixtures.Window(1, "island.app.exe"), Fixtures.Window(2, "beta.exe", 1)], [], [Fixtures.Tab(1, "example.org", 1)], false);

        var list = PlusList.Candidates(open, PickStore.Empty, [], ownExe: "Island.App.exe");

        Assert.Equal(["program:beta.exe"], list.Select(e => e.Key).ToArray());
    }

    [Fact]
    public void An_Entry_Becomes_A_Storable_Pick_On_The_Page_Asked_For()
    {
        var open = new OpenSnapshot([new OpenWindow(1, "pwsh.exe", null, "t", 0)], [], [], false);
        var entry = PlusList.Candidates(open, PickStore.Empty, [new InstalledProgram("PowerShell", "pwsh.exe", null, "launch")])[0];

        Assert.Equal("PowerShell", entry.Name);
        Assert.Equal(PageIds.Vibe, entry.SuggestedPage);
        var pick = entry.ToPick(PageIds.Apps)!;
        Assert.True(pick.IsStorable(out _));
        Assert.Equal(PageIds.Apps, pick.PageId);
        Assert.DoesNotContain("\\", pick.Id);
    }

    [Fact]
    public void Adding_From_The_List_Makes_The_Entry_Leave_The_List()
    {
        var open = new OpenSnapshot([Fixtures.Window(2, "beta.exe", 1)], [], [], false);
        var store = PickStore.Empty;
        var entry = PlusList.Candidates(open, store, [])[0];

        store = store.Add(entry.ToPick(PageIds.Apps)!, out var added);

        Assert.True(added);
        Assert.Empty(PlusList.Candidates(open, store, []));
        Assert.True(PickStates.For(store.Picks[0], open).IsOpen); // and the pick reads open at once
    }
}
