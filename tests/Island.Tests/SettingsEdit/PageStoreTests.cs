using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

public class PageStoreTests
{
    private static PageStore Five => PageStore.Default;

    [Fact]
    public void Create_Adds_A_Page_With_Name_Colour_And_Key()
    {
        // Act
        var edit = Five.Create("  Games ", "#7CE04A");

        // Assert: the page is last, trimmed, coloured, not built in, and reached with the next number key.
        Assert.False(edit.Refused);
        Assert.Equal(7, edit.Store.Pages.Count);
        Assert.Equal(Five.Pages, edit.Store.Pages.Take(6)); // the six built-in pages (Terminals is the sixth)
        var page = edit.Page!;
        Assert.Equal("Games", page.Name);
        Assert.Equal("#7CE04A", page.Color);
        Assert.False(page.IsBuiltIn);
        Assert.Equal(page, edit.Store.Pages[^1]);
        Assert.Equal(7, edit.Store.DigitFor(page.Id));
        Assert.Equal(1, edit.Store.DigitFor(PageIds.Media));
        Assert.Equal(6, Five.Pages.Count);                                        // the old store is untouched

        // A second page takes key 8, and a removed page's number is free again.
        var more = edit.Store.Create("Reading", "#3FD0FF");
        Assert.Equal(8, more.Store.DigitFor(more.Page!.Id));
        Assert.NotEqual(page.Id, more.Page.Id);
    }

    [Fact]
    public void At_Most_Nine_Pages_Exist_Because_Only_Nine_Digits_Reach_Them()
    {
        var store = Five;
        foreach (var name in new[] { "One", "Two", "Three", "Four" }) store = store.Create(name, "#123456").Store;
        Assert.Equal(9, store.Pages.Count);
        Assert.Equal(9, store.DigitFor(store.Pages[^1].Id));

        var tenth = store.Create("Ten", "#654321");

        Assert.True(tenth.Refused);
        Assert.Equal(SettingsText.PageLimit, tenth.Refusal);
        Assert.Equal(9, tenth.Store.Pages.Count);
    }

    [Theory]
    [InlineData("#123456", "#123456")]
    [InlineData("abcdef", "#ABCDEF")]
    [InlineData(" #00ff7f ", "#00FF7F")]
    [InlineData("#000000", "#000000")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void Colour_Accepts_Any_Rgb_And_Round_Trips(string typed, string stored)
    {
        // Arrange
        using var dir = new TempDir();
        var path = dir.File("pages.json");

        // Act
        var edit = Five.Create("Mine", typed);
        Assert.True(edit.Store.Save(path));
        var load = PageStore.Load(path);

        // Assert
        Assert.Equal(stored, edit.Page!.Color);
        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.Equal(stored, load.Store.ById(edit.Page.Id)!.Color);

        var recoloured = Five.Recolour(PageIds.Folders, typed);
        Assert.Equal(stored, recoloured.Page!.Color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(1,2,3)")]
    public void A_Colour_That_Is_Not_Six_Hex_Digits_Is_Refused_With_A_Reason(string typed)
    {
        var create = Five.Create("Mine", typed);
        var recolour = Five.Recolour(PageIds.Apps, typed);

        Assert.Equal(SettingsText.PageColourBad, create.Refusal);
        Assert.Equal(SettingsText.PageColourBad, recolour.Refusal);
        Assert.Equal(Five.Pages, recolour.Store.Pages);
    }

    [Fact]
    public void A_Colour_Very_Close_To_Another_Page_Is_Allowed_With_A_Warning()
    {
        var close = Five.Create("Almost red", "#FF4157");          // one step from Media's #FF4055
        var far = Five.Create("Green", "#2EC4B6");
        var own = Five.Recolour(PageIds.Media, LookConstants.MediaColor); // a page is never close to itself

        Assert.False(close.Refused);
        Assert.Equal(SettingsText.PageColourClose("Media"), close.Warning);
        Assert.Null(far.Warning);
        Assert.Null(own.Warning);
        Assert.Contains(close.Page, close.Store.Pages);
    }

    [Fact]
    public void The_Five_Pinned_Colours_Are_Not_Close_To_Each_Other()
    {
        // If the threshold ever swallowed the approved colours, picking any of them would warn.
        foreach (var page in Five.Pages)
            Assert.Null(Five.Recolour(page.Id, page.Color).Warning);
    }

    [Fact]
    public void Delete_Removes_The_Page_And_Its_Picks_After_Confirming()
    {
        // Arrange: a custom page with two picks, and picks on other pages.
        var created = Five.Create("Games", "#7CE04A");
        var id = created.Page!.Id;
        var picks = new PickStore(
        [
            Pick.ForProgram("Alpha", id, "alpha.exe", null),
            Pick.ForSite("Beta", "example.org", id),
            Pick.ForProgram("Gamma", PageIds.Apps, "gamma.exe", null),
        ]);

        // The question says how many picks go with the page.
        var question = created.Store.AskDelete(id, picks)!;
        Assert.Equal(2, question.PickCount);
        Assert.Contains("2 picks", question.Text);
        Assert.Contains("Games", question.Text);
        Assert.Contains("Nothing that is open is closed", question.Text);

        // Without a yes, nothing changes.
        var asked = created.Store.Delete(id, picks, confirmed: false);
        Assert.Equal(created.Store.Pages, asked.Pages.Pages);
        Assert.Equal(picks.Picks, asked.Picks.Picks);
        Assert.Null(asked.Refusal);

        // With a yes, the page and exactly its picks are gone.
        var deleted = created.Store.Delete(id, picks, confirmed: true);
        Assert.Equal(Five.Pages, deleted.Pages.Pages);
        Assert.Equal(2, deleted.RemovedPicks);
        Assert.Equal(["program:gamma"], deleted.Picks.Picks.Select(p => p.Id));
        Assert.Equal(3, picks.Picks.Count);                          // the old store is untouched
    }

    [Fact]
    public void The_Delete_Question_Reads_Well_For_None_And_One()
    {
        var page = Five.Create("Games", "#7CE04A");
        var one = new PickStore([Pick.ForProgram("Alpha", page.Page!.Id, "alpha.exe", null)]);

        Assert.Contains("Nothing is on it", page.Store.AskDelete(page.Page.Id, PickStore.Empty)!.Text);
        Assert.Contains("The 1 pick on it goes with it", page.Store.AskDelete(page.Page.Id, one)!.Text);
    }

    [Fact]
    public void Built_In_Pages_Cannot_Be_Deleted()
    {
        var picks = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);

        foreach (var page in Pages.BuiltIn)
        {
            var result = Five.Delete(page.Id, picks, confirmed: true);

            Assert.Equal(SettingsText.BuiltInPageCannotBeDeleted, result.Refusal);
            Assert.Equal(Five.Pages, result.Pages.Pages);
            Assert.Equal(picks.Picks, result.Picks.Picks);
            Assert.Null(Five.AskDelete(page.Id, picks));
        }
    }

    [Fact]
    public void Empty_Or_Duplicate_Name_Is_Refused()
    {
        var withGames = Five.Create("Games", "#7CE04A").Store;

        Assert.Equal(SettingsText.PageNameEmpty, Five.Create("", "#123456").Refusal);
        Assert.Equal(SettingsText.PageNameEmpty, Five.Create("   ", "#123456").Refusal);
        Assert.Equal(SettingsText.PageNameTaken("Media"), Five.Create("media", "#123456").Refusal);      // not case sensitive
        Assert.Equal(SettingsText.PageNameTaken("Games"), withGames.Create("  Games ", "#123456").Refusal);
        Assert.Equal(SettingsText.PageNameTaken("Games"), withGames.Rename(PageIds.Apps, "GAMES").Refusal);
        Assert.Equal(SettingsText.PageNameEmpty, withGames.Rename(PageIds.Apps, " ").Refusal);
        Assert.Contains("at most", Five.Create(new string('x', 25), "#123456").Refusal);

        // A refusal changes nothing; renaming to the same name in another case is allowed.
        Assert.Equal(Five.Pages, Five.Create("", "#123456").Store.Pages);
        Assert.Equal("MEDIA", Five.Rename(PageIds.Media, "MEDIA").Page!.Name);
    }

    [Fact]
    public void Pages_And_Rules_Round_Trip()
    {
        // Arrange: rename and recolour a built-in page, add two of Dan's own, save.
        using var dir = new TempDir();
        var path = dir.File("pages.json");
        var store = Five.Rename(PageIds.Vibe, "Code").Store;
        store = store.Recolour(PageIds.Browser, "#336699").Store;
        store = store.Create("Games", "#7CE04A").Store;
        store = store.Create("Reading", "#3FD0FF").Store;
        Assert.True(store.Save(path));

        // Act
        var load = PageStore.Load(path);

        // Assert: same pages, same order, built-in flags and glyphs kept; and the rules still hold after the restart.
        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.Equal(store.Pages, load.Store.Pages);
        Assert.Equal(["media", "folders", "apps", "vibe", "browser"], load.Store.Pages.Take(5).Select(p => p.Id));
        Assert.True(load.Store.ById(PageIds.Vibe)!.IsBuiltIn);
        Assert.Equal("Code", load.Store.ById(PageIds.Vibe)!.Name);
        Assert.Equal(Pages.Get(PageIds.Vibe).Glyph, load.Store.ById(PageIds.Vibe)!.Glyph);
        Assert.Equal(8, load.Store.DigitFor(load.Store.Pages[^1].Id));
        Assert.True(load.Store.Create("games", "#123456").Refused);
        Assert.True(load.Store.Delete(PageIds.Media, PickStore.Empty, true).Refusal is not null);
        Assert.DoesNotContain("\\", File.ReadAllText(path));                  // nothing in the file is a path
    }

    [Fact]
    public void A_Missing_File_Gives_The_Five_Pages_And_Writes_Nothing()
    {
        using var dir = new TempDir();

        var load = PageStore.Load(dir.File("pages.json"));

        Assert.Equal(PageStoreStatus.Missing, load.Status);
        Assert.Equal(Pages.BuiltIn, load.Store.Pages);
        Assert.False(File.Exists(dir.File("pages.json")));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1]")]
    [InlineData("{\"pages\":5}")]
    [InlineData("{\"pages\":[]}")]
    [InlineData("{\"pages\":[{\"id\":\"media\",\"name\":\"Media\",\"color\":\"#FF4055\"}]}")]
    [InlineData("{\"pages\":[{\"id\":\"BAD ID\",\"name\":\"X\",\"color\":\"#FF4055\"}]}")]
    [InlineData("")]
    public void A_Broken_File_Is_Left_Untouched_And_The_Defaults_Are_Used(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("pages.json");
        File.WriteAllText(path, content);
        var before = File.GetLastWriteTimeUtc(path);

        var load = PageStore.Load(path);

        Assert.Equal(PageStoreStatus.Unreadable, load.Status);
        Assert.Equal(Pages.BuiltIn, load.Store.Pages);
        Assert.False(string.IsNullOrEmpty(load.Detail));
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_File_With_Two_Pages_Of_One_Name_Or_A_Bad_Colour_Is_Unreadable()
    {
        var builtIn = string.Join(",", Pages.BuiltIn.Select(p => $"{{\"id\":\"{p.Id}\",\"name\":\"{p.Name}\",\"color\":\"{p.Color}\"}}"));

        Assert.Equal(PageStoreStatus.Loaded, PageStore.Parse($"{{\"pages\":[{builtIn}]}}").Status);
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse($"{{\"pages\":[{builtIn},{{\"id\":\"x\",\"name\":\"media\",\"color\":\"#123456\"}}]}}").Status);
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse($"{{\"pages\":[{builtIn},{{\"id\":\"x\",\"name\":\"X\",\"color\":\"blue\"}}]}}").Status);
    }

    private static string PageJson(Page p, string? name = null) => $"{{\"id\":\"{p.Id}\",\"name\":\"{name ?? p.Name}\",\"color\":\"{p.Color}\"}}";

    private static IEnumerable<Page> OldFive => Pages.BuiltIn.Where(p => p.Id != PageIds.Terminals);

    [Fact]
    public void An_Old_Pages_File_Gains_Terminals_At_The_End()
    {
        var mine = "{\"id\":\"page-1\",\"name\":\"Games\",\"color\":\"#7CE04A\"}";
        var json = $"{{\"pages\":[{string.Join(",", OldFive.Select(p => PageJson(p)))},{mine}]}}";

        var load = PageStore.Parse(json);

        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.False(load.TerminalsNoRoom);
        Assert.Equal(["media", "folders", "apps", "vibe", "browser", "page-1", "terminals"], load.Store.Pages.Select(p => p.Id));
        Assert.Equal("Terminals", load.Store.Pages[^1].Name);
        Assert.True(load.Store.Pages[^1].IsBuiltIn);
        Assert.Equal(PageIds.Terminals, load.Store.Pages[^1].Id);

        // The file is not rewritten by loading it.
        using var dir = new TempDir();
        File.WriteAllText(dir.File("pages.json"), json);
        PageStore.Load(dir.File("pages.json"));
        Assert.Equal(json, File.ReadAllText(dir.File("pages.json")));
    }

    [Fact]
    public void A_Page_Already_Named_Terminals_Keeps_Its_Name()
    {
        var mine = "{\"id\":\"page-1\",\"name\":\"terminals\",\"color\":\"#7CE04A\"}"; // upper and lower case are ignored
        var mine2 = "{\"id\":\"page-2\",\"name\":\"Terminals 2\",\"color\":\"#3FD0FF\"}";
        var load = PageStore.Parse($"{{\"pages\":[{string.Join(",", OldFive.Select(p => PageJson(p)))},{mine},{mine2}]}}");

        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.Equal("terminals", load.Store.ById("page-1")!.Name);
        Assert.Equal("Terminals 3", load.Store.ById(PageIds.Terminals)!.Name); // the next number that is free
    }

    [Fact]
    public void A_Full_Pages_File_Says_There_Is_No_Room()
    {
        var extra = Enumerable.Range(1, 4).Select(n => $"{{\"id\":\"page-{n}\",\"name\":\"Mine {n}\",\"color\":\"#1234{n}6\"}}");
        var json = $"{{\"pages\":[{string.Join(",", OldFive.Select(p => PageJson(p)))},{string.Join(",", extra)}]}}";

        var load = PageStore.Parse(json);

        Assert.Equal(PageStoreStatus.Loaded, load.Status);
        Assert.Equal(PageStore.MaxPages, load.Store.Pages.Count);
        Assert.Null(load.Store.ById(PageIds.Terminals));
        Assert.True(load.TerminalsNoRoom);
        Assert.Equal("TERMINALS_PAGE_NO_ROOM", TerminalsPageRefusals.NoRoom.Code);
        Assert.Contains("Delete a page you do not use", TerminalsPageRefusals.NoRoom.Message);

        // Looked at again each time the pages are loaded: with a page less, it is added.
        var fewer = $"{{\"pages\":[{string.Join(",", OldFive.Select(p => PageJson(p)))},{string.Join(",", extra.Take(3))}]}}";
        var again = PageStore.Parse(fewer);
        Assert.False(again.TerminalsNoRoom);
        Assert.NotNull(again.Store.ById(PageIds.Terminals));
        Assert.Equal(PageStore.MaxPages, again.Store.Pages.Count);
    }

    [Fact]
    public void Any_Other_Missing_Built_In_Page_Is_Still_Unreadable()
    {
        foreach (var missing in OldFive)
        {
            var rest = Pages.BuiltIn.Where(p => p.Id != missing.Id).Select(p => PageJson(p));
            Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse($"{{\"pages\":[{string.Join(",", rest)}]}}").Status);
        }

        // Missing Terminals and one more: unreadable too.
        var three = OldFive.Skip(1).Select(p => PageJson(p));
        Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse($"{{\"pages\":[{string.Join(",", three)}]}}").Status);
    }
}
