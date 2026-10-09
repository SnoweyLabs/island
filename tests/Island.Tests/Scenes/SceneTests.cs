using Island.Core;

namespace Island.Tests;

public class SceneTests
{
    private static readonly Pick Alpha = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
    private static readonly Pick Beta = Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null);
    private static readonly Pick Gamma = Pick.ForProgram("Gamma", PageIds.Apps, "gamma.exe", null);
    private static readonly Pick Docs = Pick.ForFolder("Downloads", PageIds.Folders);
    private static readonly Pick SiteOne = Pick.ForSite("Example", "example.org", PageIds.Browser);
    private static readonly Pick SiteTwo = Pick.ForSite("Sample", "example.net", PageIds.Browser);

    private static readonly Func<Pick, bool> AllInstalled = _ => true;

    internal static Scene SceneOf(params Pick[] picks)
    {
        var made = SceneStore.Empty.Create("Work");
        return made.Store.SetThings(made.Scene!.Id, picks).Scene!;
    }

    private static string Shape(Scene s) => $"{s.Id}|{s.Name}|{s.Key}|{string.Join(",", s.Things.Select(t => t.Id + "/" + t.Kind + "/" + t.Name + "/" + t.ExeName + "/" + t.PackageFamily + "/" + t.KnownFolder + "/" + t.Host + "/" + t.PageId))}";

    private static List<string> Shapes(SceneStore store) => [.. store.Items.Select(Shape)];

    // ---- Running -----------------------------------------------------------

    [Fact]
    public void Closed_Things_Open_And_Open_Things_Come_Forward()
    {
        var scene = SceneOf(Alpha, Beta, Docs, SiteOne, SiteTwo);
        var open = Fixtures.Open(
            windows: [Fixtures.Window(7, "alpha.exe", z: 3), Fixtures.Window(8, "alpha.exe", z: 1)],
            folders: [new FolderWindow(9, "Downloads", null, 0)],
            tabs: [Fixtures.Tab(1, "example.org", order: 41), Fixtures.Tab(2, "example.org", order: 55)],
            connected: true);

        var plan = ScenePlans.For(scene, open, AllInstalled);

        Assert.Equal(["alpha.exe", "beta.exe", "downloads", "example.org", "example.net"], plan.Steps.Select(s => s.Thing.ExeName ?? s.Thing.KnownFolder?.ToLowerInvariant() ?? s.Thing.Host)); // the list's order
        Assert.Equal(
            [SceneAction.BringForward, SceneAction.Open, SceneAction.BringForward, SceneAction.BringForward, SceneAction.Open],
            plan.Steps.Select(s => s.Action));
        Assert.Equal(new ClickPlan(ClickKind.BringForward, 8), plan.Steps[0].Click);     // the top-most window of the program
        Assert.Equal(new ClickPlan(ClickKind.Start), plan.Steps[1].Click);
        Assert.Equal(new ClickPlan(ClickKind.BringForward, 9), plan.Steps[2].Click);
        Assert.Equal(new ClickPlan(ClickKind.BringForward, 55), plan.Steps[3].Click);    // the newest tab (its ordering number)
        Assert.Equal(new ClickPlan(ClickKind.OpenSite), plan.Steps[4].Click);
        Assert.Equal(2, plan.OpenedCount);
        Assert.Equal("2 opened", plan.OpenedText);
        Assert.Equal("Work", plan.SceneName);
        Assert.Empty(plan.SkippedNames);
        Assert.Null(plan.PartMissing);
    }

    [Fact]
    public void Everything_Closed_Opens_Everything_In_Order()
    {
        var scene = SceneOf(Docs, Alpha, SiteOne);

        var plan = ScenePlans.For(scene, OpenSnapshot.Empty, AllInstalled);

        Assert.Equal([ClickKind.OpenFolder, ClickKind.Start, ClickKind.OpenSite], plan.Steps.Select(s => s.Click!.Kind));
        Assert.Equal(3, plan.OpenedCount);
    }

    [Fact]
    public void A_Site_Without_The_Add_On_Is_Opened_As_A_Click_Would()
    {
        var scene = SceneOf(SiteOne);

        var plan = ScenePlans.For(scene, Fixtures.Open(connected: false), AllInstalled);

        Assert.Equal(SceneAction.Open, Assert.Single(plan.Steps).Action);
    }

    [Fact]
    public void Nothing_Is_Ever_Closed()
    {
        // No action of a plan, no kind of click and no door of the outside can mean "close".
        Assert.DoesNotContain(Enum.GetNames<SceneAction>(), n => n.Contains("Close", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(IOutsideActions).GetMethods(), m => m.Name.Contains("Close", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Enum.GetNames<ClickKind>(), n => n.Contains("Close", StringComparison.OrdinalIgnoreCase));

        var scene = SceneOf(Alpha, Beta, Docs, SiteOne);
        var snapshots = new[]
        {
            OpenSnapshot.Empty,
            Fixtures.Open(windows: [Fixtures.Window(1, "alpha.exe"), Fixtures.Window(2, "beta.exe")], folders: [new FolderWindow(3, "Downloads", null, 0)], tabs: [Fixtures.Tab(1, "example.org", 5)], connected: true),
            Fixtures.Open(windows: [Fixtures.Window(1, "gamma.exe")]),
        };

        foreach (var open in snapshots)
        foreach (var installed in new Func<Pick, bool>[] { AllInstalled, _ => false })
        {
            var plan = ScenePlans.For(scene, open, installed);
            Assert.All(plan.Steps, s => Assert.Contains(s.Action, new[] { SceneAction.Open, SceneAction.BringForward, SceneAction.Skip }));
            Assert.All(plan.Steps.Where(s => s.Click is not null), s => Assert.Contains(s.Click!.Kind, new[] { ClickKind.BringForward, ClickKind.Start, ClickKind.OpenFolder, ClickKind.OpenSite }));
        }
    }

    [Fact]
    public void A_Missing_Thing_Is_Skipped_And_Named()
    {
        var scene = SceneOf(Alpha, Beta, Gamma, Docs);
        var installed = ScenePlans.InstalledIn([Fixtures.Program("Alpha", "alpha.exe")]);

        var plan = ScenePlans.For(scene, OpenSnapshot.Empty, installed);

        Assert.Equal([SceneAction.Open, SceneAction.Skip, SceneAction.Skip, SceneAction.Open], plan.Steps.Select(s => s.Action)); // the rest still runs; a folder is always there
        Assert.Equal(["Beta", "Gamma"], plan.SkippedNames);
        Assert.Equal(2, plan.OpenedCount);
        Assert.Null(plan.Steps[1].Click);

        var refusal = plan.PartMissing!;
        Assert.Equal("SCENE_PART_MISSING", refusal.Code);
        Assert.Equal(
            "2 of the things in Work could not be opened: Beta, Gamma. They are no longer on this computer, so Island opened the rest. Remove them from the scene in the settings, or install them again.",
            refusal.Message);
    }

    [Fact]
    public void A_Program_Is_Installed_By_Exe_Or_By_Package_Family()
    {
        var byPackage = Pick.ForProgram("Delta", PageIds.Apps, null, "Vendor.Delta_abc");
        var installed = ScenePlans.InstalledIn([new InstalledProgram("Delta", null, "vendor.delta_ABC", "launch-delta"), Fixtures.Program("Alpha", "ALPHA.EXE")]);

        Assert.True(installed(byPackage));
        Assert.True(installed(Alpha));
        Assert.False(installed(Beta));
        Assert.True(installed(Docs));
        Assert.True(installed(SiteOne));
    }

    [Fact]
    public void A_Thing_That_Is_Open_And_Uninstalled_Is_Brought_Forward()
    {
        var scene = SceneOf(Alpha, Beta);
        var open = Fixtures.Open(windows: [Fixtures.Window(4, "beta.exe")]);

        var plan = ScenePlans.For(scene, open, ScenePlans.InstalledIn([])); // the installed list knows neither

        Assert.Equal([SceneAction.Skip, SceneAction.BringForward], plan.Steps.Select(s => s.Action));
        Assert.Equal(["Alpha"], plan.SkippedNames);
    }

    [Fact]
    public void A_Scene_Of_Two_Hundred_Things_Is_Planned_In_Order_And_Duplicates_Count_Once()
    {
        var things = Enumerable.Range(0, Scenes.MaxThingsPerScene).Select(i => Pick.ForProgram("P" + i, PageIds.Apps, $"p{i}.exe", null)).ToList();
        var scene = SceneOf([.. things]);
        Assert.Equal(200, scene.Things.Count);

        var plan = ScenePlans.For(scene, OpenSnapshot.Empty, AllInstalled);
        Assert.Equal(things.Select(t => t.Id), plan.Steps.Select(s => s.Thing.Id));

        // A scene built by hand, outside the store, with the same thing twice and too many: the plan still holds up.
        var rough = new Scene("x", "Rough", [.. things, .. things, Alpha, Alpha]);
        var roughPlan = ScenePlans.For(rough, OpenSnapshot.Empty, AllInstalled);
        Assert.Equal(200, roughPlan.Steps.Count);
        Assert.Equal(roughPlan.Steps.Count, roughPlan.Steps.Select(s => s.Thing.Id).Distinct().Count());
    }

    [Fact]
    public void An_Empty_Scene_Does_Nothing()
    {
        var plan = ScenePlans.For(SceneOf(), OpenSnapshot.Empty, AllInstalled);

        Assert.Empty(plan.Steps);
        Assert.Equal("0 opened", plan.OpenedText);
        Assert.Null(plan.PartMissing);
    }

    [Fact]
    public void Planning_Does_Not_Cycle_The_Windows_Of_A_Click()
    {
        var scene = SceneOf(Alpha);
        var open = Fixtures.Open(windows: [Fixtures.Window(1, "alpha.exe", z: 0), Fixtures.Window(2, "alpha.exe", z: 1)]);

        var first = ScenePlans.For(scene, open, AllInstalled).Steps[0].Click;
        var second = ScenePlans.For(scene, open, AllInstalled).Steps[0].Click;

        Assert.Equal(first, second); // always the newest, run after run
        Assert.Equal(1, first!.Target);
    }

    // ---- The message -------------------------------------------------------

    [Fact]
    public void The_Message_Stays_Under_The_Balloon_Limit_With_Many_Or_Long_Names()
    {
        var many = Enumerable.Range(1, 200).Select(i => "Program number " + i).ToList();
        var longOnes = new List<string> { new('Q', 200), new('R', 200) };
        var sceneName = new string('S', Scenes.MaxNameLength);

        foreach (var names in new[] { many, longOnes, ["Beta"], [new string('Z', 5000)] })
        {
            var message = SceneRefusals.PartMissing(sceneName, names)!.Message;
            Assert.True(message.Length <= 250, $"{names.Count} names gave {message.Length} characters");
            Assert.EndsWith("Remove them from the scene in the settings, or install them again.", message);
        }

        var shown = SceneRefusals.PartMissing("Work", many)!.Message;
        Assert.Contains(" and ", shown);
        Assert.Contains(" more:", shown.Replace(". They", ":")); // the count of the unlisted names ends the list
        Assert.StartsWith("200 of the things in Work could not be opened: Program number 1,", shown);
    }

    [Fact]
    public void A_Name_That_Looks_Like_A_Placeholder_Is_Shown_As_It_Is()
    {
        var message = SceneRefusals.PartMissing("<names>", ["<scene> {n}", "<n>"])!.Message;

        Assert.StartsWith("2 of the things in <names> could not be opened: <scene> {n}, <n>. They are", message);
    }

    [Fact]
    public void Hidden_Characters_In_A_Thing_Name_Do_Not_Reach_The_Message()
    {
        var message = SceneRefusals.PartMissing("Wo‮rk", ["Be​ta‮", "Ga\r\nmma"])!.Message;

        Assert.StartsWith("2 of the things in Work could not be opened: Beta, Ga mma. They are", message);
        Assert.DoesNotContain(message, c => c is '‮' or '​' or '\r' or '\n');
    }

    [Fact]
    public void A_Name_Cut_Short_Never_Splits_A_Character()
    {
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 400)); // 800 UTF-16 units of surrogate pairs

        var message = SceneRefusals.PartMissing("Work", [emoji])!.Message;

        Assert.True(message.Length <= 250);
        for (var i = 0; i < message.Length; i++)
        {
            if (char.IsHighSurrogate(message[i])) Assert.True(i + 1 < message.Length && char.IsLowSurrogate(message[i + 1]));
            if (char.IsLowSurrogate(message[i])) Assert.True(i > 0 && char.IsHighSurrogate(message[i - 1]));
        }
    }

    // ---- Storing -----------------------------------------------------------

    [Fact]
    public void Scenes_Round_Trip()
    {
        using var dir = new TempDir();
        var path = dir.File("scenes.json");
        var store = SceneStore.Empty;
        store = store.Create("Work").Store;
        store = store.Create("Evening").Store;
        store = store.SetThings("scene-1", [Alpha, Docs, SiteOne, Pick.ForProgram("Packaged", PageIds.Apps, null, "Vendor.App_abc")]).Store;
        store = store.SetKey("scene-1", HotkeyCombo.Parse("Ctrl+Alt+Shift+F5")).Store;

        Assert.True(store.Save(path));
        var load = SceneStore.Load(path);

        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Null(load.Detail);
        Assert.Equal(Shapes(store), Shapes(load.Store));
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+Shift+F5"), load.Store.ById("scene-1")!.Key);
        Assert.Null(load.Store.ById("scene-2")!.Key);
        Assert.Empty(load.Store.ById("scene-2")!.Things);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_Saved_File_Holds_No_Path_And_No_Page_Of_The_Island()
    {
        using var dir = new TempDir();
        var path = dir.File("scenes.json");
        var store = SceneStore.Empty.Create("Work").Store.SetThings("scene-1", [Alpha, Docs, SiteOne]).Store;
        Assert.True(store.Save(path));
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("\\\\", text);
        Assert.DoesNotContain(":/", text);
        Assert.DoesNotContain("\"page\": \"apps\"", text);
        Assert.Contains("\"schema\": 1", text); // changed with WO8 section 1: files carry "schema" now, the old "version" is still read
    }

    [Fact]
    public void Save_Refuses_A_Thing_That_Looks_Like_A_Path()
    {
        using var dir = new TempDir();
        var path = dir.File("scenes.json");
        var bad = new Pick("program:x", PickKind.Program, "X", Scenes.ThingPage, ExeName: @"C:\Tools\x.exe");
        var store = new SceneStore([new Scene("scene-1", "Work", [bad])]);

        Assert.False(store.Save(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_Removed_Pick_Stays_In_Its_Scene()
    {
        var picks = new PickStore([Alpha, Docs]);
        var store = SceneStore.Empty.Create("Work").Store.SetThings("scene-1", picks.Picks).Store;

        var afterRemoval = picks.Remove(Alpha.Id);

        Assert.Null(afterRemoval.ById(Alpha.Id));
        var scene = store.ById("scene-1")!;
        Assert.Equal(["alpha.exe", null], scene.Things.Select(t => t.ExeName));                // the scene owns its copy
        var plan = ScenePlans.For(scene, OpenSnapshot.Empty, AllInstalled);
        Assert.Equal(SceneAction.Open, plan.Steps[0].Action);                                    // and still runs it

        using var dir = new TempDir();
        Assert.True(store.Save(dir.File("scenes.json")));
        Assert.Equal(Shapes(store), Shapes(SceneStore.Load(dir.File("scenes.json")).Store));    // and still keeps it on disk
    }

    [Fact]
    public void A_Thing_Is_A_Copy_Not_A_Reference_And_Carries_No_Page()
    {
        var store = SceneStore.Empty.Create("Work").Store.AddThing("scene-1", Alpha).Store;

        var thing = Assert.Single(store.ById("scene-1")!.Things);

        Assert.Equal(Scenes.ThingPage, thing.PageId);
        Assert.Equal(PageIds.Apps, Alpha.PageId); // the pick itself is untouched
        Assert.Equal(Alpha.Id, thing.Id);
    }

    [Fact]
    public void No_Scene_Is_Shipped_Ready_Made()
    {
        using var dir = new TempDir();

        var load = SceneStore.Load(dir.File("scenes.json"));

        Assert.Equal(SceneStoreStatus.Missing, load.Status);
        Assert.Empty(load.Store.Items);
        Assert.Empty(SceneStore.Empty.Items);
        Assert.False(File.Exists(dir.File("scenes.json")), "Loading must not create the file.");
    }

    // ---- Editing -----------------------------------------------------------

    [Fact]
    public void Things_Are_Added_Moved_And_Removed_Without_Touching_The_Old_Store()
    {
        var one = SceneStore.Empty.Create("Work");
        var id = one.Scene!.Id;
        var a = one.Store.SetThings(id, [Alpha, Beta, Gamma]).Store;

        var moved = a.MoveThing(id, Gamma.Id, 0).Store;
        var removed = moved.RemoveThing(id, Beta.Id).Store;

        Assert.Equal(["gamma.exe", "alpha.exe", "beta.exe"], moved.ById(id)!.Things.Select(t => t.ExeName));
        Assert.Equal(["gamma.exe", "alpha.exe"], removed.ById(id)!.Things.Select(t => t.ExeName));
        Assert.Equal(["alpha.exe", "beta.exe", "gamma.exe"], a.ById(id)!.Things.Select(t => t.ExeName));
        Assert.Empty(one.Store.ById(id)!.Things);
        Assert.Equal("alpha.exe", a.MoveThing(id, Alpha.Id, 99).Store.ById(id)!.Things[^1].ExeName); // beyond the end is the end
        Assert.Equal("alpha.exe", a.MoveThing(id, Alpha.Id, -5).Store.ById(id)!.Things[0].ExeName);
    }

    [Fact]
    public void The_Same_Thing_Twice_Is_One_Thing()
    {
        var id = SceneStore.Empty.Create("Work").Scene!.Id;
        var store = SceneStore.Empty.Create("Work").Store;

        var set = store.SetThings(id, [Alpha, Beta, Alpha]).Store;
        var added = set.AddThing(id, Beta);

        Assert.Equal(["alpha.exe", "beta.exe"], set.ById(id)!.Things.Select(t => t.ExeName));
        Assert.False(added.Refused);
        Assert.Equal(2, added.Store.ById(id)!.Things.Count);
    }

    [Fact]
    public void At_Most_Two_Hundred_Things_Are_Accepted()
    {
        var big = Enumerable.Range(0, 201).Select(i => Pick.ForProgram("P" + i, PageIds.Apps, $"p{i}.exe", null)).ToList();
        var store = SceneStore.Empty.Create("Work").Store;

        var tooMany = store.SetThings("scene-1", big);
        var enough = store.SetThings("scene-1", big.Take(200));
        var oneMore = enough.Store.AddThing("scene-1", big[200]);

        Assert.Equal(SceneText.ThingLimit, tooMany.Refusal);
        Assert.Same(store, tooMany.Store);
        Assert.False(enough.Refused);
        Assert.Equal(SceneText.ThingLimit, oneMore.Refusal);
        Assert.Equal(200, oneMore.Store.ById("scene-1")!.Things.Count);
    }

    [Fact]
    public void A_Pick_With_A_Path_Cannot_Be_Put_In_A_Scene()
    {
        var store = SceneStore.Empty.Create("Work").Store;
        var bad = Alpha with { ExeName = @"C:\Tools\alpha.exe" };

        var edit = store.AddThing("scene-1", bad);

        Assert.Equal(SceneText.ThingNotStorable, edit.Refusal);
        Assert.Empty(edit.Store.ById("scene-1")!.Things);
    }

    [Fact]
    public void At_Most_A_Hundred_Scenes_Can_Be_Made()
    {
        var store = SceneStore.Empty;
        for (var i = 0; i < Scenes.MaxScenes; i++) store = store.Create("Scene " + i).Store;

        var more = store.Create("One more");

        Assert.Equal(100, store.Items.Count);
        Assert.Equal(SceneText.SceneLimit, more.Refusal);
    }

    [Fact]
    public void Deleting_A_Scene_Leaves_The_Others_And_Their_Keys()
    {
        var store = SceneStore.Empty.Create("One").Store.Create("Two").Store
            .SetKey("scene-1", HotkeyCombo.Parse("Ctrl+Alt+F6")).Store
            .SetKey("scene-2", HotkeyCombo.Parse("Ctrl+Alt+F7")).Store;

        var after = store.Delete("scene-1");

        Assert.Equal(["scene-2"], after.Items.Select(s => s.Id));
        Assert.Equal([HotkeyCombo.Parse("Ctrl+Alt+F7")], after.WithKeys.Select(s => s.Key!.Value));
        Assert.Equal(2, store.Items.Count);
        Assert.Same(after.Items[0], after.ById("scene-2")); // the editor reads what is left to release the deleted scene's key
        Assert.Equal("scene-1", Assert.Single(store.Items, s => s.Key == HotkeyCombo.Parse("Ctrl+Alt+F6")).Id);
        Assert.Equal("scene-1", after.Create("Three").Scene!.Id); // the first free number, as pages do: a deleted scene's id can come back, so the editor must release its key on delete
    }

    [Fact]
    public void Keys_Are_Carried_Not_Judged()
    {
        var store = SceneStore.Empty.Create("One").Store.Create("Two").Store;
        var key = HotkeyCombo.Parse("Ctrl+Alt+F6");

        var both = store.SetKey("scene-1", key).Store.SetKey("scene-2", key).Store; // the editor refuses duplicates (K5), not the store
        var cleared = both.SetKey("scene-1", null).Store;

        Assert.Equal(2, both.WithKeys.Count);
        Assert.Null(cleared.ById("scene-1")!.Key);
        Assert.True(store.SetKey("nope", key).Refused);
    }

    // ---- Names -------------------------------------------------------------

    [Theory]
    [InlineData("  Work   day  ", "Work day")]
    [InlineData("Night\t shift", "Night shift")]
    [InlineData("Line\r\nbreak", "Line break")]
    [InlineData("Ünïcode ✓", "Ünïcode ✓")]
    public void A_Name_Is_Trimmed_And_Its_White_Space_Tidied(string typed, string kept)
    {
        var edit = SceneStore.Empty.Create(typed);

        Assert.False(edit.Refused);
        Assert.Equal(kept, edit.Scene!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData(null)]
    public void An_Empty_Name_Is_Refused(string? typed)
    {
        Assert.Equal(SceneText.NameEmpty, SceneStore.Empty.Create(typed!).Refusal);
    }

    [Theory]
    [InlineData("Wor\u0000k")]             // a null
    [InlineData("Wor\u001Bk")]             // an escape
    [InlineData("Work\u202Egnp.exe")]      // a right-to-left override: the name would read backwards
    [InlineData("\u200FWork")]             // a right-to-left mark
    [InlineData("Wo\u200Brk")]             // a zero-width space
    [InlineData("Wo\uFEFFrk")]             // a byte order mark in the middle
    [InlineData("Work\uD800")]             // a lone surrogate
    [InlineData("Work\uE000")]             // a private-use character
    public void A_Name_With_Hidden_Or_Control_Characters_Is_Refused(string typed)
    {
        var edit = SceneStore.Empty.Create(typed);

        Assert.Equal(SceneText.NameBad, edit.Refusal);
        Assert.Empty(edit.Store.Items);
    }

    [Fact]
    public void A_Very_Long_Name_Is_Refused_Not_Cut()
    {
        Assert.Equal(SceneText.NameTooLong, SceneStore.Empty.Create(new string('a', 10_000)).Refusal);
        Assert.Equal(SceneText.NameTooLong, SceneStore.Empty.Create(new string('a', Scenes.MaxNameLength + 1)).Refusal);
        Assert.False(SceneStore.Empty.Create(new string('a', Scenes.MaxNameLength)).Refused);
    }

    [Fact]
    public void Two_Scenes_Cannot_Share_A_Name_Ignoring_Case_And_Spacing()
    {
        var store = SceneStore.Empty.Create("Work Day").Store;

        var clash = store.Create("  work   DAY ");
        var other = store.Create("Evening");
        var renameToClash = other.Store.Rename(other.Scene!.Id, "WORK day");
        var renameToItself = store.Rename("scene-1", "WORK DAY");

        Assert.Equal(SceneText.NameTaken("Work Day"), clash.Refusal);
        Assert.Equal(SceneText.NameTaken("Work Day"), renameToClash.Refusal);
        Assert.False(renameToItself.Refused);            // a scene may change its own case
        Assert.Equal("WORK DAY", renameToItself.Scene!.Name);
        Assert.True(store.Rename("nope", "X").Refused);
    }

    // ---- Files -------------------------------------------------------------

    private static string SceneJson(string things = "[]", string name = "Work", string extra = "") =>
        $$"""{ "version": 1, "scenes": [ { "id": "scene-1", "name": "{{name}}", "things": {{things}} {{extra}} } ] }""";

    private const string AlphaThing = """{ "id": "program:alpha", "kind": "program", "name": "Alpha", "page": "scene", "exe": "alpha.exe" }""";

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("""{ "scenes": 5 }""")]
    [InlineData("""{ "version": 1 }""")]
    [InlineData("""{ "version": "one", "scenes": [] }""")]
    [InlineData("""{ "version": 0, "scenes": [] }""")]
    [InlineData("""{ "version": 99, "scenes": [] }""")]
    [InlineData("""{ "version": 2, "scenes": [ { "id": "scene-1", "name": "Work", "things": [], "futureField": true } ] }""")]
    [InlineData("""{ "scenes": [ 5 ] }""")]
    [InlineData("""{ "scenes": [ { "id": "Bad Id", "name": "Work", "things": [] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "", "things": [] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Wo\u202Erk", "things": [] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work" } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work", "things": 3 } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work", "things": [ { "id": "x", "kind": "program", "name": "X", "page": "scene", "exe": "C:\\Tools\\x.exe" } ] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work", "things": [ { "id": "s", "kind": "site", "name": "S", "page": "scene", "host": "https://example.org/x" } ] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work", "things": [ 7 ] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work", "things": [] }, { "id": "scene-1", "name": "Other", "things": [] } ] }""")]
    [InlineData("""{ "scenes": [ { "id": "scene-1", "name": "Work", "things": [] }, { "id": "scene-2", "name": "WORK", "things": [] } ] }""")]
    public void A_Broken_Or_Newer_File_Is_Left_Untouched_And_Gives_An_Empty_List(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("scenes.json");
        File.WriteAllText(path, content);
        var before = File.GetLastWriteTimeUtc(path);

        var load = SceneStore.Load(path);

        Assert.Equal(SceneStoreStatus.Unreadable, load.Status);
        Assert.Empty(load.Store.Items);
        Assert.False(string.IsNullOrEmpty(load.Detail));
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_File_From_A_Newer_Version_Says_So()
    {
        var load = SceneStore.Parse("""{ "version": 2, "scenes": [] }""");

        Assert.Equal(SceneStoreStatus.Unreadable, load.Status);
        Assert.Contains("newer version", load.Detail);
    }

    [Fact]
    public void A_File_Without_A_Version_Is_The_First_Format()
    {
        var load = SceneStore.Parse("""{ "scenes": [] }""");

        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
    }

    [Fact]
    public void Ten_Thousand_Scenes_Are_Cut_To_A_Hundred_Without_A_Crash()
    {
        var scenes = string.Join(",", Enumerable.Range(0, 10_000).Select(i => $$"""{ "id": "scene-{{i}}", "name": "S{{i}}", "things": [] }"""));

        var load = SceneStore.Parse($$"""{ "version": 1, "scenes": [ {{scenes}} ] }""");

        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Equal(Scenes.MaxScenes, load.Store.Items.Count);
        Assert.Equal("scene-0", load.Store.Items[0].Id);
        Assert.Contains("9900 scenes", load.Detail);
    }

    [Fact]
    public void A_Hundred_Thousand_Things_Are_Cut_To_Two_Hundred_Without_A_Crash()
    {
        var things = string.Join(",", Enumerable.Range(0, 100_000).Select(i => $$"""{ "id": "program:p{{i}}", "kind": "program", "name": "P{{i}}", "page": "scene", "exe": "p{{i}}.exe" }"""));

        var load = SceneStore.Parse(SceneJson($"[ {things} ]"));

        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Equal(Scenes.MaxThingsPerScene, load.Store.Items[0].Things.Count);
        Assert.Contains("99800 things", load.Detail);
        Assert.All(load.Store.Items[0].Things, t => Assert.Equal(Scenes.ThingPage, t.PageId));
    }

    [Fact]
    public void A_File_Over_The_Size_Limit_Is_Not_Read()
    {
        var huge = SceneJson(name: new string('x', SceneStore.MaxFileChars));

        var load = SceneStore.Parse(huge);

        Assert.Equal(SceneStoreStatus.Unreadable, load.Status);
    }

    [Fact]
    public void Very_Deep_Nesting_Is_Unreadable_Not_A_Crash()
    {
        var deep = new string('[', 5000) + new string(']', 5000);

        var load = SceneStore.Parse($$"""{ "version": 1, "scenes": {{deep}} }""");

        Assert.Equal(SceneStoreStatus.Unreadable, load.Status);
    }

    [Fact]
    public void A_Key_That_Is_Not_Valid_Is_Dropped_And_The_Scene_Is_Kept()
    {
        var json = $$"""
            { "version": 1, "scenes": [
              { "id": "scene-1", "name": "One", "key": "Win+1", "things": [ {{AlphaThing}} ] },
              { "id": "scene-2", "name": "Two", "key": "Ctrl+Alt+F6", "things": [] },
              { "id": "scene-3", "name": "Three", "key": "ctrl + alt + f6", "things": [] },
              { "id": "scene-4", "name": "Four", "key": 7, "things": [] } ] }
            """;

        var load = SceneStore.Parse(json);

        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Equal(4, load.Store.Items.Count);
        Assert.Equal([null, HotkeyCombo.Parse("Ctrl+Alt+F6"), null, null], load.Store.Items.Select(s => s.Key));
        Assert.Equal("Alpha", load.Store.Items[0].Things[0].Name);
        Assert.Contains("2 keys", load.Detail);
    }

    [Fact]
    public void A_Scene_Name_In_A_File_Is_Tidied()
    {
        var load = SceneStore.Parse(SceneJson(name: "  Work    day "));

        Assert.Equal("Work day", load.Store.Items[0].Name);
    }

    [Fact]
    public void An_Unwritable_Path_Gives_False_Not_An_Exception()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("blocker"), "x");

        var saved = SceneStore.Empty.Create("Work").Store.Save(Path.Combine(dir.File("blocker"), "scenes.json")); // a file where a folder must be

        Assert.False(saved);
    }

    [Fact]
    public void A_Load_Of_A_Folder_Where_The_File_Should_Be_Does_Not_Throw()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("scenes.json"));

        var load = SceneStore.Load(dir.File("scenes.json"));

        Assert.NotEqual(SceneStoreStatus.Loaded, load.Status);
    }

    // ---- The held key ------------------------------------------------------

    [Fact]
    public void A_Held_Key_Runs_It_Once()
    {
        var guard = new HeldKeyGuard();
        var runs = 0;

        // One press held for ten seconds: the first message after 0 ms, then the keyboard repeats after 500 ms (the
        // slowest delay) and every 33 ms.
        if (guard.Accept("scene-1", 0)) runs++;
        for (var t = 500L; t <= 10_000; t += 33)
            if (guard.Accept("scene-1", t)) runs++;
        Assert.Equal(1, runs);

        // Released, then pressed again: runs again, even at once.
        guard.Released("scene-1");
        Assert.True(guard.Accept("scene-1", 10_010));
        Assert.False(guard.Accept("scene-1", 10_500)); // and holds again

        // Without a release notice, a pause longer than the gap is a new press.
        Assert.True(guard.Accept("scene-1", 10_500 + HeldKeyGuard.RepeatGapMs));
    }

    [Fact]
    public void A_Held_Key_Of_One_Scene_Does_Not_Swallow_Another()
    {
        var guard = new HeldKeyGuard();

        Assert.True(guard.Accept("scene-1", 0));
        Assert.True(guard.Accept("scene-2", 10));
        Assert.False(guard.Accept("scene-1", 40));
        Assert.False(guard.Accept("scene-2", 50));
    }

    [Fact]
    public void A_Clock_That_Goes_Backwards_Counts_As_A_New_Press()
    {
        var guard = new HeldKeyGuard();

        Assert.True(guard.Accept("scene-1", 5_000));
        Assert.True(guard.Accept("scene-1", 100));
    }

    [Fact]
    public void A_Key_That_Was_Never_Pressed_Can_Be_Released()
    {
        var guard = new HeldKeyGuard();

        guard.Released("never");

        Assert.True(guard.Accept("never", 0));
    }
}
