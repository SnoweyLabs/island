using System.Diagnostics;
using System.Text;
using Island.Core;

namespace Island.Attack3.Tests;

/// <summary>ATTACK3 on the picks: files, ids, clicks, the + list and the tiles. Made-up names only.</summary>
public class PicksAttackTests
{
    // ---- Defects (each test fails because of one) --------------------------------------------------------

    [Fact]
    public void Cycling_Three_Windows_Reaches_The_Third_When_Each_Click_Brings_Its_Window_To_The_Top()
    {
        // Windows puts the window it brings forward on top, so the next snapshot is in a new z-order. Three
        // clicks on a pick with three windows must reach all three; the cycle remembers a handle and takes the
        // one after it in the NEW order, so it ping-pongs between the two newest windows.
        var pick = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var zOrder = new List<long> { 1, 2, 3 }; // top first
        var cycler = new ClickCycler();
        var reached = new List<long>();

        for (var click = 0; click < 3; click++)
        {
            var open = new OpenSnapshot([.. zOrder.Select((h, z) => new OpenWindow(h, "alpha.exe", null, "Alpha", z))], [], [], false);
            var plan = PickStates.Plan(pick, PickStates.For(pick, open), cycler);
            Assert.Equal(ClickKind.BringForward, plan.Kind);
            reached.Add(plan.Target);
            zOrder.Remove(plan.Target);
            zOrder.Insert(0, plan.Target);
        }

        Assert.Equal([1L, 2L, 3L], reached);
    }

    [Fact]
    public void Cycling_Three_Tabs_Reaches_Every_Tab_When_Activating_Makes_A_Tab_Newest()
    {
        // A site's click targets are the tabs' ordering numbers, and activating a tab gives it a new number
        // (TabModel.Activate: Order = ++_order). The cycle then cannot find the number it remembered and starts
        // again at the newest tab: the one it just activated.
        var pick = Pick.ForSite("Example", "example.org", PageIds.Browser);
        var orders = new Dictionary<string, long> { ["p:1"] = 3, ["p:2"] = 2, ["p:3"] = 1 };
        long counter = 3;
        var cycler = new ClickCycler();
        var reached = new List<string>();

        for (var click = 0; click < 3; click++)
        {
            var tabs = orders.Select(kv => new TabInfo(kv.Key, 1, int.Parse(kv.Key[2..]), "Example", "example.org", false, false, kv.Value, null)).ToList();
            var plan = PickStates.Plan(pick, PickStates.For(pick, new OpenSnapshot([], [], tabs, true)), cycler);
            Assert.Equal(ClickKind.BringForward, plan.Kind);
            var key = tabs.First(t => t.LastActiveOrder == plan.Target).Key; // what PickPages.Carry does
            reached.Add(key);
            orders[key] = ++counter;
        }

        Assert.Equal(3, reached.Distinct().Count());
    }

    [Fact]
    public void A_Store_That_Was_Allowed_To_Grow_Saves_A_File_That_Loads_Again()
    {
        // Add has no limit, Save writes everything, but Load refuses a file with more than 1000 picks: on the next
        // start the whole list is "unreadable" and the island is empty.
        var store = PickStore.Empty;
        for (var i = 0; i < 1001; i++) store = store.Add(Pick.ForProgram($"Program {i}", PageIds.Apps, $"p{i}.exe", null), out _);
        using var dir = new TempDir();
        var path = dir.File("picks.json");

        Assert.True(store.Save(path));
        var load = PickStore.Load(path);

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(store.Picks.Count, load.Store.Picks.Count);
    }

    [Theory]
    [InlineData("αλφα.exe", "βήτα.exe")] // letters outside ASCII are dropped from the id: both become "program:"
    [InlineData("al pha.exe", "alpha.exe")] // a space is dropped: both become "program:alpha"
    [InlineData("ålpha.exe", "lpha.exe")]
    public void Two_Different_Programs_Never_Share_An_Id(string exeA, string exeB)
    {
        var a = Pick.ForProgram("Alpha", PageIds.Apps, exeA, null);
        var b = Pick.ForProgram("Beta", PageIds.Apps, exeB, null);

        var store = PickStore.Empty.Add(a, out _).Add(b, out var addedB);

        Assert.NotEqual(a.Id, b.Id);
        Assert.True(addedB, "the second program was refused as 'the same thing picked twice'");
        Assert.Equal(2, store.Picks.Count);
    }

    [Theory]
    [InlineData("a\U0001F600")] // one word whose second character is outside the basic plane
    [InlineData("Alpha \U0001D539eta")] // a second word that starts outside the basic plane
    public void Tile_Mark_Never_Holds_Half_A_Character(string name)
    {
        var mark = PickItems.Mark(name);

        Assert.True(IsWellFormed(mark), $"mark has a lone surrogate: {string.Join(" ", mark.Select(c => ((int)c).ToString("X4")))}");
    }

    [Fact]
    public void Plus_List_Offers_Only_Sites_That_Can_Be_Added()
    {
        // A tab on a host without a dot (a local dev server) is offered, but its "Add" does nothing: ToPick is null.
        // Programs that cannot be stored are already left out (PlusList: "is not offered").
        var open = new OpenSnapshot([], [], [new TabInfo("p:1", 1, 1, "Dev", "localhost", false, true, 1, null)], true);

        var entries = PlusList.Candidates(open, PickStore.Empty, []);

        Assert.All(entries, e => Assert.NotNull(e.ToPick(PageIds.Browser)));
    }

    [Fact]
    public void Plus_List_Shows_One_Row_Per_Program_Even_When_Only_Some_Windows_Report_A_Package()
    {
        var open = new OpenSnapshot(
            [new OpenWindow(1, "alpha.exe", null, "Alpha", 0), new OpenWindow(2, "alpha.exe", "Alpha.App_x1", "Alpha", 1)], [], [], false);

        var entries = PlusList.Candidates(open, PickStore.Empty, []);

        Assert.Equal(entries.Count, entries.Select(e => e.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("2")] // the number of PickKind.Site
    [InlineData("Program, Site")] // a flags-style list, read as 0|2 = Site
    public void A_Kind_That_Is_Not_One_Of_The_Three_Words_Is_Refused(string kind)
    {
        var json = $$"""{ "picks": [ { "id": "site:example.org", "kind": "{{kind}}", "name": "Example", "page": "browser", "host": "example.org" } ] }""";

        Assert.Equal(PickStoreStatus.Unreadable, PickStore.Parse(json).Status);
    }

    // ---- Coverage that holds (what I could not break) ----------------------------------------------------

    [Fact]
    public void A_Pick_The_Store_Accepts_Survives_Save_And_Load()
    {
        // Holds: a name with a lone surrogate passes IsStorable, and what Save writes for it loads again (a store
        // that saves a file Load refuses would lose every pick on the next start, as the 1001-pick case does).
        var store = PickStore.Empty
            .Add(Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null), out _)
            .Add(Pick.ForProgram("Alpha\uD800", PageIds.Apps, "alpha.exe", null), out _);
        using var dir = new TempDir();
        var path = dir.File("picks.json");

        Assert.True(store.Save(path));
        var load = PickStore.Load(path);

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(store.Picks.Count, load.Store.Picks.Count);
    }

    public static TheoryData<string> HostileFiles() => new()
    {
        string.Empty,
        "null",
        "{}",
        "﻿{ \"picks\": [ { \"id\": \"x\" ",
        "{ \"picks\": [" + string.Concat(Enumerable.Repeat("[", 100_000)) + "] }",
        "{ \"picks\": [ { \"id\": \"program:alpha\", \"kind\": \"program\", \"name\": \"" + new string('a', 5_000_000) + "\", \"page\": \"apps\", \"exe\": \"alpha.exe\" } ] }",
        "{ \"picks\": [ " + string.Join(",", Enumerable.Range(0, 1001).Select(i => $"{{ \"id\": \"program:p{i}\", \"kind\": \"program\", \"name\": \"P{i}\", \"page\": \"apps\", \"exe\": \"p{i}.exe\" }}")) + " ] }",
        """{ "picks": [ { "id": "program:alpha", "kind": "program", "name": "A", "page": "apps", "exe": "alpha.exe" }, { "id": "program:alpha", "kind": "program", "name": "B", "page": "apps", "exe": "beta.exe" } ] }""",
        """{ "picks": [ { "id": "program:..\\..\\x", "kind": "program", "name": "A", "page": "apps", "exe": "alpha.exe" } ] }""",
        """{ "picks": [ { "id": "program:a", "kind": "program", "name": "A", "page": "..\\apps", "exe": "alpha.exe" } ] }""",
        """{ "picks": [ { "id": "program:a", "kind": "program", "name": "A", "page": "apps", "exe": "../alpha.exe" } ] }""",
        """{ "picks": [ { "id": "program:a", "kind": "program", "name": "A\u0000", "page": "apps", "exe": "alpha.exe" } ] }""",
        """{ "picks": [ { "id": "program:a", "kind": "program", "name": "A\uD800", "page": "apps", "exe": "alpha.exe" } ] }""",
        """{ "picks": [ { "id": "program:a", "kind": "program", "name": "A", "page": "apps", "exe": 5 } ] }""",
        """{ "picks": [ { "id": "folder:x", "kind": "folder", "name": "X", "page": "folders", "folder": "Downloads\\.." } ] }""",
        """{ "picks": [ { "id": "site:x", "kind": "site", "name": "X", "page": "media", "host": "EXAMPLE.org" } ] }""",
        """{ "picks": [ { "id": "site:x", "kind": "site", "name": "X", "page": "media", "host": "example.org:8080" } ] }""",
        """{ "picks": [ { "id": "x", "kind": "Banana", "name": "X", "page": "apps" } ] }""",
    };

    [Theory]
    [MemberData(nameof(HostileFiles))]
    public void Hostile_Picks_Files_Are_Refused_Quickly_And_Left_Untouched(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");
        File.WriteAllText(path, content);
        var bytes = File.ReadAllBytes(path);
        var clock = Stopwatch.StartNew();

        var load = PickStore.OpenOrStart(path, [], selfTest: false);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        Assert.Equal(PickStoreStatus.Unreadable, load.Status);
        Assert.Empty(load.Store.Picks);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Thousands_Of_Windows_Of_One_Program_Are_One_Pick_And_The_Cycle_Wraps()
    {
        var pick = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var windows = Enumerable.Range(0, 5000).Select(i => new OpenWindow(10_000 + i, i % 2 == 0 ? "ALPHA.EXE" : "alpha.exe", null, "Alpha", 4999 - i)).ToList();
        var open = new OpenSnapshot(windows, [], [], false);
        var cycler = new ClickCycler();

        var status = PickStates.For(pick, open);
        var first = PickStates.Plan(pick, status, cycler);
        for (var i = 1; i < 5000; i++) PickStates.Plan(pick, status, cycler);
        var wrapped = PickStates.Plan(pick, status, cycler);

        Assert.Equal(5000, status.Count);
        Assert.Equal(14_999, first.Target); // z-order 0 is the newest
        Assert.Equal(first, wrapped);
        var plus = PlusList.Candidates(open, PickStore.Empty, []);
        Assert.Single(plus);
        Assert.Equal(5000, plus[0].Count);
    }

    [Fact]
    public void A_Window_That_Closed_Before_The_Click_Is_Never_The_Target()
    {
        // Click planned on a fresh snapshot: a handle that is gone is not offered, and an empty list starts the program.
        var pick = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var cycler = new ClickCycler();
        var two = new OpenSnapshot([new OpenWindow(1, "alpha.exe", null, "A", 0), new OpenWindow(2, "alpha.exe", null, "A", 1)], [], [], false);
        Assert.Equal(1, PickStates.Plan(pick, PickStates.For(pick, two), cycler).Target);

        var oneLeft = new OpenSnapshot([new OpenWindow(1, "alpha.exe", null, "A", 0)], [], [], false);
        var none = OpenSnapshot.Empty;

        Assert.Equal(new ClickPlan(ClickKind.BringForward, 1), PickStates.Plan(pick, PickStates.For(pick, oneLeft), cycler));
        Assert.Equal(new ClickPlan(ClickKind.Start), PickStates.Plan(pick, PickStates.For(pick, none), cycler));
        Assert.Equal(new ClickPlan(ClickKind.BringForward, 1), PickStates.Plan(pick, PickStates.For(pick, oneLeft), cycler));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1e300)]
    [InlineData(0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e300)]
    public void PageFit_With_Absurd_Widths_Stays_Between_1_And_200(double width)
    {
        Assert.InRange(PageFit.MaxTiles(width, isMedia: false), 1, 200);
        Assert.InRange(PageFit.MaxTiles(width, isMedia: true), 1, 200);
    }

    [Fact]
    public void Plus_List_With_Hostile_Exe_Names_Offers_Only_Storable_Programs()
    {
        string[] exes = ["..\\evil.exe", "a/b.exe", "c:alpha.exe", "", ".exe", "alpha", new string('x', 300) + ".exe", "‮exe.ahpla.exe", "alpha\0.exe", "beta.exe"];
        var open = new OpenSnapshot([.. exes.Select((e, i) => new OpenWindow(i + 1, e, null, "<script>‮\n" + new string('t', 10_000), i))], [], [], false);

        var entries = PlusList.Candidates(open, PickStore.Empty, [new InstalledProgram("Beta", "beta.exe", null, "x")]);

        Assert.All(entries, e => Assert.NotNull(e.ToPick(PageIds.Apps)));
        Assert.Contains(entries, e => e.Name == "Beta");
        Assert.DoesNotContain(entries, e => e.ExeName is { } x && (x.Contains('\\') || x.Contains('/') || x.Contains(':')));
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

    private static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "island-attack3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
