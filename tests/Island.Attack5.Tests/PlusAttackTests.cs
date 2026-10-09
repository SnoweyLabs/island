using Island.Core;

namespace Island.Attack5.Tests;

/// <summary>WORK-ORDER-5 section 5: PlusRow and PlusList, fed windows, folders and tabs of invented names.</summary>
public class PlusAttackTests
{
    private static OpenWindow Window(int n, string? exe, string? package = null, int? z = null) => new(1000 + n, exe, package, "invented", z ?? n);

    private static OpenSnapshot Snapshot(IEnumerable<OpenWindow>? windows = null, IEnumerable<FolderWindow>? folders = null, IEnumerable<TabInfo>? tabs = null, bool connected = false) =>
        new([.. windows ?? []], [.. folders ?? []], [.. tabs ?? []], connected);

    private static TabInfo Tab(int n, string host, long order) => new($"k{n}", 1, n, "invented", host, false, false, order, null);

    // ---- what held ----

    [Fact]
    public void Holds_Thousands_Of_Windows_Give_At_Most_Forty_Tiles_Top_Most_First()
    {
        var windows = Enumerable.Range(0, 3000).Select(i => Window(i, $"p{i}.exe", z: 3000 - i)).ToList(); // the last one is top-most
        var entries = PlusRow.Entries(Snapshot(windows), PickStore.Empty, []);
        Assert.Equal(PlusRow.MaxEntries, entries.Count);
        Assert.Equal("program:p2999.exe", entries[0].Key);
        Assert.Equal(entries.Count, entries.Select(e => e.Key).Distinct().Count());
        Assert.Equal($"{PlusRow.MaxEntries} open", PlusRow.AddSubtitle(entries.Count));
    }

    [Fact]
    public void Holds_A_Picked_Program_Is_Not_Offered_Whatever_The_Case_And_Windows_Group_Into_One_Tile()
    {
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);
        var open = Snapshot([Window(0, "ALPHA.EXE"), Window(1, "Beta.exe"), Window(2, "beta.EXE"), Window(3, "Gamma.exe")]);
        var entries = PlusRow.Entries(open, store, []);
        Assert.Equal(["program:beta.exe", "program:gamma.exe"], entries.Select(e => e.Key));
        Assert.Equal(2, entries[0].Count);
        Assert.Equal("2 windows", entries[0].Detail);
        Assert.Equal("open", entries[1].Detail);
        Assert.Equal(1000 + 1, PlusRow.JumpTarget(entries[0])); // the top-most window of that program
    }

    [Fact]
    public void Holds_A_Package_Only_Window_Joins_The_Group_Of_Its_Package_And_Nameless_Windows_Are_Skipped()
    {
        var open = Snapshot([Window(0, "alpha.exe", "Alpha_pub1"), Window(1, null, "Alpha_pub1"), Window(2, null, "Solo_pub2"), Window(3, null, null), Window(4, "own.exe")]);
        var entries = PlusRow.Entries(open, PickStore.Empty, [], ownExe: "OWN.exe");
        Assert.Equal(["program:alpha.exe", "program:solo_pub2"], entries.Select(e => e.Key));
        Assert.Equal(2, entries[0].Count);
        Assert.Equal(1, entries[1].Count);
    }

    [Fact]
    public void Holds_Names_That_Cannot_Be_Stored_Are_Not_Offered()
    {
        var open = Snapshot([Window(0, "C:\\x\\alpha.exe"), Window(1, "notanexe"), Window(2, new string('a', 300) + ".exe"), Window(3, "fine.exe")]);
        Assert.Equal(["program:fine.exe"], PlusRow.Entries(open, PickStore.Empty, []).Select(e => e.Key));
    }

    [Fact]
    public void Holds_Sites_Group_By_Normalized_Host_Newest_First_And_Only_When_The_Add_On_Is_Connected()
    {
        var tabs = new[] { Tab(0, "www.Example.org", 5), Tab(1, "example.org.", 9), Tab(2, "other.example", 7), Tab(3, "localhost", 100), Tab(5, "", 100), Tab(6, "picked.example", 1) };
        var store = new PickStore([Pick.ForSite("Picked", "www.picked.example", PageIds.Browser)]);

        Assert.Empty(PlusRow.Entries(Snapshot(tabs: tabs, connected: false), store, [])); // no add-on: tabs are not known
        var entries = PlusRow.Entries(Snapshot(tabs: tabs, connected: true), store, []);
        Assert.Equal(["site:example.org", "site:other.example"], entries.Select(e => e.Key)); // newest tab first; localhost, an empty host and the picked site are left out
        Assert.Equal(2, entries[0].Count);
        Assert.Equal("2 tabs", entries[0].Detail);
        Assert.Equal([9L, 5L], entries[0].Targets);
        Assert.Equal(9, PlusRow.JumpTarget(entries[0]));
    }

    [Fact]
    public void Holds_Tabs_With_Ordinary_And_Odd_Order_Numbers_Never_Throw_And_Sort_Newest_First()
    {
        foreach (var order in new[] { long.MinValue, -5, -1, 0, 1, int.MaxValue / 4, int.MaxValue / 4 + 1, long.MaxValue })
            Assert.Single(PlusRow.Entries(Snapshot(tabs: [Tab(0, "a.example", order)], connected: true), PickStore.Empty, []));

        var tabs = Enumerable.Range(0, 50).Select(i => Tab(i, $"s{i}.example", i * 3)).ToList();
        var keys = PlusRow.Entries(Snapshot(tabs: tabs, connected: true), PickStore.Empty, []).Select(e => e.Key).ToList();
        Assert.Equal(Enumerable.Range(0, 40).Select(i => $"site:s{49 - i}.example"), keys);
    }

    [Fact]
    public void Holds_Sites_Come_Last_In_One_List_And_One_Folder_Open_Twice_Is_One_Tile()
    {
        var open = Snapshot([Window(0, "beta.exe", z: 4)], [new FolderWindow(7, "Downloads", null, 2), new FolderWindow(8, "downloads", null, 1)], [Tab(0, "site.example", 3)], connected: true);
        var entries = PlusRow.Entries(open, PickStore.Empty, []);
        Assert.Equal(3, entries.Count); // one program, the folder (open twice, one tile) and one site
        Assert.Equal("site:site.example", entries[^1].Key); // sites last
        Assert.Equal(2, entries.Single(e => e.Kind == PickKind.Folder).Count);
        var downloadsPick = new PickStore([Pick.ForFolder("DOWNLOADS", PageIds.Folders)]);
        Assert.DoesNotContain(PlusRow.Entries(open, downloadsPick, []), e => e.Kind == PickKind.Folder);
    }

    [Fact]
    public void Holds_Add_Never_Stores_What_Cannot_Be_Stored_And_Never_Touches_The_Old_Store()
    {
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);
        var entry = PlusRow.Entries(Snapshot([Window(0, "beta.exe")]), store, []).Single();

        var added = PlusRow.AddedTo(store, entry, PageIds.Apps);
        Assert.Equal(2, added.Picks.Count);
        Assert.Single(store.Picks); // the old store is untouched
        Assert.Same(added, PlusRow.AddedTo(added, entry, PageIds.Apps)); // adding it again changes nothing
        Assert.Equal(2, PlusRow.AddedTo(added, entry, PageIds.Folders).Picks.Count); // the same thing cannot be picked twice, on any page

        foreach (var page in new[] { "", " ", "a\\b", "a/b", "a:b", new string('x', 201) })
            Assert.Same(store, PlusRow.AddedTo(store, entry, page));

        var full = new PickStore(Enumerable.Range(0, PickStore.MaxPicks).Select(i => Pick.ForProgram($"P{i}", PageIds.Apps, $"p{i}.exe", null)));
        Assert.Equal(PickStore.MaxPicks, full.Picks.Count);
        Assert.Same(full, PlusRow.AddedTo(full, entry, PageIds.Apps)); // a full store takes nothing more

        var local = new PlusEntry("site:localhost", PickKind.Site, "localhost", null, null, null, "localhost", 1, [1]);
        Assert.Same(store, PlusRow.AddedTo(store, local, PageIds.Browser));
        var noHost = new PlusEntry("site:x", PickKind.Site, "x", null, null, null, null, 1, [1]);
        Assert.Same(store, PlusRow.AddedTo(store, noHost, PageIds.Browser));
        var noFolder = new PlusEntry("folder:x", PickKind.Folder, "x", null, null, null, null, 1, [1]);
        Assert.Same(store, PlusRow.AddedTo(store, noFolder, PageIds.Folders));
    }

    [Fact]
    public void Holds_Add_And_Drag_Off_In_Quick_Alternation_Keep_The_Store_Consistent()
    {
        // The pick that is open is offered in the second row, added to a page, dragged off (removed) and offered again, many times over.
        var open = Snapshot([Window(0, "alpha.exe"), Window(1, "beta.exe")]);
        var store = PickStore.Empty;
        for (var i = 0; i < 2000; i++)
        {
            var entries = PlusRow.Entries(open, store, []);
            if (entries.Count > 0) store = PlusRow.AddedTo(store, entries[i % entries.Count], i % 2 == 0 ? PageIds.Apps : PageIds.Vibe);
            Assert.Equal(store.Picks.Count, store.Picks.Select(p => p.Id).Distinct().Count());
            Assert.DoesNotContain(PlusRow.Entries(open, store, []), e => store.Picks.Any(p => p.Id == e.ToPick("x")?.Id)); // never offered while it is a pick
            if ((i % 3 == 0 || entries.Count == 0) && store.Picks.Count > 0) store = store.Remove(store.Picks[i % store.Picks.Count].Id);
        }

        Assert.InRange(store.Picks.Count, 0, 2);
        Assert.Equal(2, PlusRow.Entries(open, PickStore.Empty, []).Count);
    }

    [Fact]
    public void Holds_Jump_Goes_To_The_First_Target_Or_Nowhere_And_Adds_Nothing()
    {
        Assert.Equal(7, PlusRow.JumpTarget(new PlusEntry("k", PickKind.Program, "Alpha", "alpha.exe", null, null, null, 2, [7, 8])));
        Assert.Null(PlusRow.JumpTarget(new PlusEntry("k", PickKind.Program, "Alpha", "alpha.exe", null, null, null, 0, [])));
        Assert.Equal(-1, PlusRow.JumpTarget(new PlusEntry("k", PickKind.Site, "a.example", null, null, null, "a.example", 1, [-1])));
    }

    // ---- what broke ----

    [Fact]
    public void Defect_A_Folder_That_Cannot_Be_Stored_Is_Offered_Although_Its_Small_Plus_Does_Nothing()
    {
        // Programs and sites that cannot become a pick are left out of the second row (ToPick is checked); a folder is not checked.
        // A folder window whose name is not one of the six known folders is offered as a tile, and its small + silently adds nothing.
        // (Latent: the folder reader only sets KnownFolder to one of the six.)
        var open = Snapshot(folders: [new FolderWindow(7, "Gaming", null, 0)]);
        var entries = PlusRow.Entries(open, PickStore.Empty, []);
        foreach (var entry in entries) Assert.NotNull(entry.ToPick(PageIds.Folders));
    }

    [Fact]
    public void Defect_An_Ip_Address_Tab_Is_Offered_As_A_Site()
    {
        // PlusList says "a host that cannot be a pick (localhost, an address) is not offered", but Pick.IsStorable only asks that a
        // host contain a dot, so a tab on 127.0.0.1 or 192.168.0.1 is offered as a "site" and its small + stores it as a website pick.
        var tabs = new[] { Tab(0, "127.0.0.1", 1), Tab(1, "192.168.0.1", 2), Tab(2, "real.example", 3) };
        var keys = PlusRow.Entries(Snapshot(tabs: tabs, connected: true), PickStore.Empty, []).Select(e => e.Key);
        Assert.Equal(["site:real.example"], keys);
    }
}
