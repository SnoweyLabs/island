using System.Text.Json;
using System.Text.Json.Nodes;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack11.Tests;

/// <summary>A pretend Windows for keys: grants everything.</summary>
internal sealed class PretendRegistrar : IHotkeyRegistrar
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

/// <summary>A temporary folder under the system's temp path that is removed afterwards.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("island-attack11-");

    public string Root => _dir.FullName;

    public string File(string name) => Path.Combine(_dir.FullName, name);

    public SettingsFiles Files() => new(File("settings.json"), File("pages.json"), File("picks.json"), File("scenes.json"));

    public void Dispose()
    {
        foreach (var f in _dir.GetFiles("*", SearchOption.AllDirectories)) f.Attributes = FileAttributes.Normal;
        _dir.Delete(recursive: true);
    }
}

/// <summary>WORK-ORDER-11 section 1, the pages file and the picks: the sixth built-in page against every shape of file and every way to put a pick on it.</summary>
public class PagesAttackTests
{
    private static readonly string[] OldFive = ["media", "folders", "apps", "vibe", "browser"];

    private static string PagesFile(IEnumerable<(string Id, string Name)> pages)
    {
        var rows = new JsonArray();
        foreach (var (id, name) in pages) rows.Add(new JsonObject { ["id"] = id, ["name"] = name, ["color"] = "#336699" });
        return new JsonObject { [FileSchema.Key] = FileSchema.Current, ["pages"] = rows }.ToJsonString();
    }

    private static (string Id, string Name)[] Old() => [("media", "Media"), ("folders", "Folders"), ("apps", "Apps"), ("vibe", "Vibe coding"), ("browser", "Browser")];

    private static (string Id, string Name)[] Custom(int count, string prefix = "Mine") => [.. Enumerable.Range(1, count).Select(i => ($"page-{i}", $"{prefix} {i}"))];

    // ---- A pages file with `terminals` in every position --------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_An_Old_File_Gains_Terminals_At_The_End_And_Is_Not_Rewritten()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("pages.json"), PagesFile(Old()));
        var before = File.ReadAllText(temp.File("pages.json"));
        var load = PageStore.Load(temp.File("pages.json"));
        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.Equal([.. OldFive, "terminals"], load.Store.Pages.Select(p => p.Id));
        Assert.Equal(before, File.ReadAllText(temp.File("pages.json")));
        Assert.False(load.TerminalsNoRoom);
    }

    [Fact]
    public void Holds_Terminals_Twice_Is_Unreadable_And_The_Defaults_Are_Used()
    {
        var load = PageStore.Parse(PagesFile([.. Old(), ("terminals", "Terminals"), ("terminals", "Other")]));
        Assert.Equal(PageStoreStatus.Unreadable, load.Status);
        Assert.Equal(6, load.Store.Pages.Count);
    }

    [Fact]
    public void Holds_Terminals_Twice_Under_One_Name_Is_Unreadable_Too()
    {
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse(PagesFile([.. Old(), ("terminals", "Terminals"), ("terminals", "Terminals")])).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Holds_Terminals_In_The_Middle_Keeps_Its_Place(int at)
    {
        var pages = Old().ToList();
        pages.Insert(at, ("terminals", "Terminals"));
        var load = PageStore.Parse(PagesFile(pages));
        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.Equal(pages.Select(p => p.Item1), load.Store.Pages.Select(p => p.Id));
        Assert.Equal(at + 1, load.Store.DigitFor("terminals"));
    }

    [Fact]
    public void Holds_Terminals_Renamed_Keeps_Its_Name_And_Stays_Built_In()
    {
        var load = PageStore.Parse(PagesFile([.. Old(), ("terminals", "Shells")]));
        var page = load.Store.ById("terminals")!;
        Assert.Equal("Shells", page.Name);
        Assert.True(page.IsBuiltIn);
        Assert.Equal("terminal", page.Glyph);
        Assert.Null(page.Keybind);
    }

    [Fact]
    public void Holds_Terminals_Under_The_Name_Of_Another_Built_In_Is_Unreadable()
    {
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse(PagesFile([.. Old(), ("terminals", "media")])).Status);
    }

    [Fact]
    public void Holds_Terminals_With_Another_Case_Of_Its_Id_Is_Not_The_Built_In_Page_And_Is_Unreadable()
    {
        // an id is lower case only: "Terminals" as an id is refused, as any id with a capital is
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse(PagesFile([.. Old(), ("Terminals", "Terminals")])).Status);
    }

    [Fact]
    public void Holds_Another_Built_In_Missing_Beside_Terminals_Is_Still_Unreadable()
    {
        var pages = Old().Where(p => p.Id != "browser");
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse(PagesFile(pages)).Status);
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse(PagesFile(pages.Append(("terminals", "Terminals")))).Status);
    }

    // ---- Nine pages --------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Nine_Pages_Without_Terminals_Says_There_Is_No_Room_And_Adds_Nothing()
    {
        var load = PageStore.Parse(PagesFile([.. Old(), .. Custom(4)]));
        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.True(load.TerminalsNoRoom);
        Assert.Equal(9, load.Store.Pages.Count);
        Assert.Null(load.Store.ById("terminals"));
    }

    [Fact]
    public void Holds_Eight_Pages_Without_Terminals_Leave_Exactly_One_Place_For_It()
    {
        var load = PageStore.Parse(PagesFile([.. Old(), .. Custom(3)]));
        Assert.False(load.TerminalsNoRoom);
        Assert.Equal(9, load.Store.Pages.Count);
        Assert.Equal("terminals", load.Store.Pages[^1].Id);
        Assert.Equal(9, load.Store.DigitFor("terminals"));
    }

    [Fact]
    public void Holds_Nine_Pages_With_Terminals_Is_Fine_And_Ten_Is_Unreadable()
    {
        Assert.Equal(PageStoreStatus.Loaded, PageStore.Parse(PagesFile([.. Old(), ("terminals", "Terminals"), .. Custom(3)])).Status);
        var ten = PageStore.Parse(PagesFile([.. Old(), ("terminals", "Terminals"), .. Custom(4)]));
        Assert.Equal(PageStoreStatus.Unreadable, ten.Status);
    }

    [Fact]
    public void Holds_Room_Is_Looked_At_Again_At_Every_Load()
    {
        using var temp = new TempFolder();
        var full = PagesFile([.. Old(), .. Custom(4)]);
        File.WriteAllText(temp.File("pages.json"), full);
        Assert.True(PageStore.Load(temp.File("pages.json")).TerminalsNoRoom);
        File.WriteAllText(temp.File("pages.json"), PagesFile([.. Old(), .. Custom(3)]));
        Assert.False(PageStore.Load(temp.File("pages.json")).TerminalsNoRoom);
    }

    [Fact]
    public void Holds_A_Store_With_No_Room_Refuses_A_New_Page_And_Cannot_Save_A_Tenth()
    {
        var store = PageStore.Parse(PagesFile([.. Old(), .. Custom(4)])).Store;
        Assert.True(store.Create("Tenth", "#112233").Refused);
    }

    // ---- A page of his named "Terminals" ------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Terminals")]
    [InlineData("terminals")]
    [InlineData("TERMINALS")]
    [InlineData("  Terminals  ")]
    public void Holds_A_Page_Of_His_Named_Terminals_Makes_The_Built_In_Name_Terminals_2(string name)
    {
        var load = PageStore.Parse(PagesFile([.. Old(), ("page-1", name)]));
        Assert.Equal("Terminals 2", load.Store.ById("terminals")!.Name);
        Assert.Equal(7, load.Store.Pages.Count);
    }

    [Fact]
    public void Holds_Pages_Named_Terminals_And_Terminals_2_Make_The_Built_In_Terminals_3()
    {
        var load = PageStore.Parse(PagesFile([.. Old(), ("page-1", "Terminals"), ("page-2", "terminals 2")]));
        Assert.Equal("Terminals 3", load.Store.ById("terminals")!.Name);
    }

    [Fact]
    public void Holds_Names_Never_Clash_After_Adding()
    {
        var load = PageStore.Parse(PagesFile([.. Old(), ("page-1", "Terminals"), ("page-2", "TERMINALS 2"), ("page-3", "Terminals 3")]));
        var names = load.Store.Pages.Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Holds_A_Name_Made_Free_Is_Saved_And_Loaded_Back_As_A_Valid_File()
    {
        using var temp = new TempFolder();
        var load = PageStore.Parse(PagesFile([.. Old(), ("page-1", "Terminals")]));
        Assert.True(load.Store.Save(temp.File("pages.json")));
        var again = PageStore.Load(temp.File("pages.json"));
        Assert.Equal(PageStoreStatus.Loaded, again.Status);
        Assert.Equal(load.Store.Pages.Select(p => (p.Id, p.Name)), again.Store.Pages.Select(p => (p.Id, p.Name)));
    }

    [Fact]
    public void Holds_The_Built_In_Page_Cannot_Be_Deleted_Or_Renamed_Onto_A_Taken_Name()
    {
        var store = PageStore.Default;
        Assert.NotNull(store.Delete("terminals", PickStore.Empty, confirmed: true).Refusal);
        Assert.Null(store.AskDelete("terminals", PickStore.Empty));
        Assert.True(store.Rename("terminals", "Media").Refused);
        Assert.True(store.Create("Terminals", "#112233").Refused);
        Assert.True(store.Create("terminals", "#112233").Refused);
    }

    // ---- A pick that names the page --------------------------------------------------------------------------------------------------------------------

    private static Pick OnTerminals(string id = "program:alpha") => new(id, PickKind.Program, "Alpha", "terminals", ExeName: "alpha.exe");

    [Fact]
    public void Holds_A_Pick_That_Names_The_Page_Is_Not_Loaded_And_The_File_Is_Not_Rewritten()
    {
        using var temp = new TempFolder();
        var text = new JsonObject
        {
            [FileSchema.Key] = FileSchema.Current,
            ["picks"] = new JsonArray(
                new JsonObject { ["id"] = "program:alpha", ["kind"] = "program", ["name"] = "Alpha", ["page"] = "terminals", ["exe"] = "alpha.exe" },
                new JsonObject { ["id"] = "program:beta", ["kind"] = "program", ["name"] = "Beta", ["page"] = "apps", ["exe"] = "beta.exe" }),
        }.ToJsonString();
        File.WriteAllText(temp.File("picks.json"), text);
        var load = PickStore.Load(temp.File("picks.json"));
        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(["program:beta"], load.Store.Picks.Select(p => p.Id));
        Assert.Equal(text, File.ReadAllText(temp.File("picks.json")));
    }

    [Fact]
    public void Holds_The_Same_Thing_Named_Twice_Once_On_The_Page_Is_Still_Added_Elsewhere()
    {
        var store = new PickStore([OnTerminals(), new Pick("program:alpha", PickKind.Program, "Alpha", "apps", ExeName: "alpha.exe")]);
        // the first of two picks with one id wins in DistinctBy; the one on the page is dropped before, so the one on Apps stays
        Assert.Equal("apps", Assert.Single(store.Picks).PageId);
    }

    [Fact]
    public void Holds_No_Way_Puts_A_Pick_On_The_Page()
    {
        var store = PickStore.Empty;
        Assert.False(store.Add(OnTerminals(), out var added).Picks.Any() || added);
        var apps = store.Add(new Pick("program:alpha", PickKind.Program, "Alpha", "apps", ExeName: "alpha.exe"), out _);
        Assert.Equal("apps", apps.Move("program:alpha", "terminals").ById("program:alpha")!.PageId);
        Assert.Same(apps, apps.ReplacePage("terminals", [OnTerminals("program:beta")]));
        Assert.False(PicksOnIsland.CanRestore(Pages.Get("terminals")));
        Assert.False(PageIds.CanHoldPicks("terminals"));
        Assert.True(PageIds.CanHoldPicks("Terminals")); // only the one id: a different spelling is an unknown page, as any other
        Assert.True(PageIds.CanHoldPicks(null));
    }

    [Fact]
    public void Holds_Nothing_Suggests_Or_Starts_A_Pick_On_The_Page()
    {
        var exes = new[] { null, "", "WindowsTerminal.exe", "wt.exe", "cmd.exe", "pwsh.exe", "wezterm-gui.exe", "alacritty.exe", "mintty.exe", "Claude.exe", "Antigravity.exe", "Cursor.exe", "Windsurf.exe", "ChatGPT.exe", "alpha.exe" };
        foreach (var kind in Enum.GetValues<PickKind>())
        foreach (var exe in exes)
        foreach (var name in new[] { null, "Claude", "Terminal", "Terminals", "terminals", "Alpha" })
            Assert.NotEqual(PageIds.Terminals, PickSuggest.PageFor(kind, exe, "example.org", name));

        Assert.All(StarterPicks.Programs, p => Assert.NotEqual(PageIds.Terminals, p.PageId));
        var installed = StarterPicks.Programs.SelectMany(p => p.ExeCandidates.Select(e => new InstalledProgram(p.Name, e, null, @"Q:Invented\" + e))).ToList();
        Assert.All(StarterPicks.Build(installed), p => Assert.NotEqual(PageIds.Terminals, p.PageId));
        Assert.All(StarterPicks.Build([]), p => Assert.NotEqual(PageIds.Terminals, p.PageId));
    }

    private static SettingsSession Session(TempFolder temp, PickStore? picks = null)
    {
        var files = temp.Files();
        var store = picks ?? PickStore.Empty;
        store.Save(files.PicksPath);
        Settings.Defaults.Save(files.SettingsPath);
        return new SettingsSession(
            files,
            new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(store, PickStoreStatus.Loaded, null),
            new PretendRegistrar(),
            () => [],
            () => false,
            scenes: new SceneStoreLoad(SceneStore.Empty, SceneStoreStatus.Missing, null));
    }

    [Fact]
    public void Holds_The_Settings_Session_Refuses_Every_Door_To_The_Page_Of_A_Pick()
    {
        using var temp = new TempFolder();
        var apps = new PickStore([new Pick("program:alpha", PickKind.Program, "Alpha", "apps", ExeName: "alpha.exe")]);
        var session = Session(temp, apps);
        Assert.False(session.MovePick("program:alpha", "terminals").Changed);
        Assert.NotNull(session.MovePick("program:alpha", "terminals").Refusal);
        Assert.False(session.AddSite("terminals", "example.org").Added);
        Assert.Null(session.AskRestore("terminals"));
        Assert.False(session.RestorePage("terminals").Changed);
        Assert.False(session.DeletePage("terminals").Changed);
        Assert.NotNull(session.DeletePage("terminals").Refusal);
        Assert.Equal("apps", session.Picks.ById("program:alpha")!.PageId);
        Assert.Empty(session.PickRows("terminals"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("valid")]
    public void Holds_A_Key_Entry_For_Terminals_Is_Optional_And_An_Old_Settings_File_Loads(string how)
    {
        using var temp = new TempFolder();
        Settings.Defaults.Save(temp.File("settings.json"));
        var text = JsonNode.Parse(File.ReadAllText(temp.File("settings.json")))!.AsObject();
        var hotkeys = text["hotkeys"]!.AsObject();
        Assert.True(hotkeys.ContainsKey("terminals"), "the defaults write an entry for the page");
        switch (how)
        {
            case "missing": hotkeys.Remove("terminals"); break;
            case "empty": hotkeys["terminals"] = ""; break;
            default: hotkeys["terminals"] = "Ctrl+Alt+Shift+6"; break;
        }

        File.WriteAllText(temp.File("settings.json"), text.ToJsonString());
        var load = Settings.Load(temp.File("settings.json"));
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        if (how == "valid") Assert.NotNull(load.Settings.KeyFor("terminals")); else Assert.Null(load.Settings.KeyFor("terminals"));
    }

    [Fact]
    public void Holds_The_Same_Key_On_Terminals_And_Another_Page_Is_Not_Taken_Twice()
    {
        using var temp = new TempFolder();
        Settings.Defaults.Save(temp.File("settings.json"));
        var text = JsonNode.Parse(File.ReadAllText(temp.File("settings.json")))!.AsObject();
        text["hotkeys"]!["terminals"] = "Ctrl+Alt+Shift+4";
        text["hotkeys"]!["vibe"] = "Ctrl+Alt+Shift+4";
        File.WriteAllText(temp.File("settings.json"), text.ToJsonString());
        var load = Settings.Load(temp.File("settings.json"));
        Assert.True(load.Status == SettingsStatus.Unreadable || load.Settings.KeyFor("terminals") != load.Settings.KeyFor("vibe") || load.Settings.KeyFor("vibe") is null);
    }

    private static string Describe(JsonElement e) => e.ToString();
}
