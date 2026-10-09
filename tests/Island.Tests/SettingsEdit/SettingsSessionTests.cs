using Island.Core;
using Island.Core.SettingsEdit;
using static Island.Tests.SettingsEdit.KeyFixtures;

namespace Island.Tests.SettingsEdit;

public class SettingsSessionTests
{
    [Fact]
    public void A_Key_Change_Is_In_Force_And_Saved_Before_The_Call_Returns()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var registrar = new FakeRegistrar();
        registrar.Held.Add(Combo("Ctrl+Q"));
        var session = SessionFixtures.Open(files, registrar: registrar);
        var areas = new List<SettingsArea>();
        session.Changed += areas.Add;

        var waiting = session.PressKey(KeybindEditor.MainId, Press(0x11, HotkeyModifiers.Control));
        var result = session.PressKey(KeybindEditor.MainId, Press(Space, HotkeyModifiers.Control | HotkeyModifiers.Alt));

        Assert.True(waiting.Waiting);
        Assert.True(result.Ok && result.Changed);
        Assert.Equal(Combo("Ctrl+Alt+Space"), session.Settings.ShowHide);
        Assert.Equal([Combo("Ctrl+Alt+Space")], registrar.Held.ToList());
        Assert.Equal(Combo("Ctrl+Alt+Space"), Settings.Load(files.SettingsPath).Settings.ShowHide);
        Assert.Equal([SettingsArea.Keys], areas);
    }

    [Fact]
    public void A_Refused_Key_Leaves_The_Old_Key_In_Force_And_Saves_Nothing()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var registrar = new FakeRegistrar();
        registrar.Held.Add(Combo("Ctrl+Q"));
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+Space"));
        var session = SessionFixtures.Open(files, registrar: registrar);
        var areas = new List<SettingsArea>();
        session.Changed += areas.Add;

        var result = session.PressKey(KeybindEditor.MainId, Press(Space, HotkeyModifiers.Control | HotkeyModifiers.Alt));

        Assert.False(result.Ok);
        Assert.Contains("another program", result.Refusal);
        Assert.Equal(Combo("Ctrl+Q"), session.Settings.ShowHide);
        Assert.Equal([Combo("Ctrl+Q")], registrar.Held.ToList());
        Assert.False(File.Exists(files.SettingsPath));
        Assert.Empty(areas);
    }

    [Fact]
    public void A_File_That_Could_Not_Be_Read_Is_Never_Overwritten()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        File.WriteAllText(files.SettingsPath, "{ broken");
        var session = new SettingsSession(
            files,
            Settings.Load(files.SettingsPath),
            new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null),
            new FakeRegistrar(), () => [], () => false);

        var key = session.PressKey(KeybindEditor.MainId, Press(Space, HotkeyModifiers.Control | HotkeyModifiers.Alt));
        var glass = session.SetGlass(GlassKind.Darker);

        Assert.False(key.Ok);
        Assert.False(glass.Ok);
        Assert.Contains("settings.json could not be read", key.Refusal);
        Assert.Equal("{ broken", File.ReadAllText(files.SettingsPath));
    }

    [Fact]
    public void A_New_Page_Is_Saved_And_Can_Have_Its_Own_Key()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var registrar = new FakeRegistrar();
        var session = SessionFixtures.Open(files, registrar: registrar);

        var result = session.CreatePage("Games", "#7CE04A", Combo("Ctrl+Alt+7"));

        Assert.True(result.Ok);
        var page = session.Pages.Pages[^1];
        Assert.Equal("Games", page.Name);
        Assert.Equal(Combo("Ctrl+Alt+7"), session.Settings.KeyFor(page.Id));
        Assert.Contains(Combo("Ctrl+Alt+7"), registrar.Held);
        Assert.Equal(page, PageStore.Load(files.PagesPath).Store.Pages[^1]);
        Assert.Equal(Combo("Ctrl+Alt+7"), Settings.Load(files.SettingsPath, session.Pages.Pages).Settings.KeyFor(page.Id));
    }

    [Fact]
    public void A_Refused_Key_Means_No_Page_Is_Made()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var registrar = new FakeRegistrar();
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+7"));
        var session = SessionFixtures.Open(files, registrar: registrar);

        var result = session.CreatePage("Games", "#7CE04A", Combo("Ctrl+Alt+7"));

        Assert.False(result.Ok);
        Assert.Equal(6, session.Pages.Pages.Count);
        Assert.False(File.Exists(files.PagesPath));
    }

    [Fact]
    public void Rename_And_Recolour_Are_Saved_And_Recolour_Warns_About_A_Close_Colour()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files);

        var renamed = session.RenamePage(PageIds.Vibe, "Code");
        var recoloured = session.RecolourPage(PageIds.Apps, "#FF4157");
        var duplicate = session.RenamePage(PageIds.Apps, "media");

        Assert.True(renamed.Ok);
        Assert.True(recoloured.Ok);
        Assert.Equal(SettingsText.PageColourClose("Media"), recoloured.Warning);
        Assert.False(duplicate.Ok);
        var saved = PageStore.Load(files.PagesPath).Store;
        Assert.Equal("Code", saved.ById(PageIds.Vibe)!.Name);
        Assert.Equal("#FF4157", saved.ById(PageIds.Apps)!.Color);
    }

    [Fact]
    public void Deleting_A_Page_Takes_Its_Picks_And_Its_Key_And_Closes_Nothing()
    {
        // Arrange: a page of Dan's with a key and two picks.
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var registrar = new FakeRegistrar();
        var session = SessionFixtures.Open(files, registrar: registrar);
        session.CreatePage("Games", "#7CE04A", Combo("Ctrl+Alt+7"));
        var id = session.Pages.Pages[^1].Id;
        session.SetPick(new OnIslandRow(Pick.ForProgram("Alpha", id, "alpha.exe", null), false), true);
        session.SetPick(new OnIslandRow(Pick.ForSite("Beta", "example.org", id), false), true);
        var before = session.Picks.Picks.Count;

        // Act
        var question = session.AskDeletePage(id)!;
        var result = session.DeletePage(id);

        // Assert
        Assert.Equal(2, question.PickCount);
        Assert.True(result.Ok);
        Assert.Equal(6, session.Pages.Pages.Count);
        Assert.Equal(before - 2, session.Picks.Picks.Count);
        Assert.Empty(session.Picks.ForPage(id));
        Assert.Null(session.Settings.KeyFor(id));
        Assert.DoesNotContain(Combo("Ctrl+Alt+7"), registrar.Held);
        Assert.Equal(6, PageStore.Load(files.PagesPath).Store.Pages.Count);
        Assert.DoesNotContain(id, File.ReadAllText(files.SettingsPath));          // no line is left behind for a page that is gone
        Assert.Equal(session.Picks.Picks, PickStore.Load(files.PicksPath).Store.Picks);
        Assert.Null(session.AskDeletePage(PageIds.Media));
        Assert.False(session.DeletePage(PageIds.Media).Ok);
    }

    [Fact]
    public void Blur_Is_Refused_When_It_Is_Not_Available_And_A_Glass_Choice_Is_Saved()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files, blurAvailable: false);

        var blur = session.SetGlass(GlassKind.Blur);
        var darker = session.SetGlass(GlassKind.Darker);

        Assert.False(blur.Ok);
        Assert.Equal(SettingsText.BlurUnavailable, blur.Refusal);
        Assert.True(darker.Ok);
        Assert.Equal(GlassKind.Darker, Settings.Load(files.SettingsPath).Settings.Glass);
        Assert.DoesNotContain(session.GlassOptions, o => o.Kind == GlassKind.Blur);
    }

    [Fact]
    public void A_Pick_Switch_Is_Saved_And_Raises_Changed_Once()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files);
        var areas = new List<SettingsArea>();
        session.Changed += areas.Add;
        var row = session.PickRows(PageIds.Media)[0];

        var off = session.SetPick(row, false);
        var offAgain = session.SetPick(row, false);

        Assert.True(off.Changed);
        Assert.False(offAgain.Changed);
        Assert.Equal([SettingsArea.Picks], areas);
        Assert.Null(PickStore.Load(files.PicksPath).Store.ById(row.Pick.Id));
    }

    [Fact]
    public void A_Hand_Added_Pick_Switched_Off_Stays_As_A_Faint_Row_For_The_Visit_And_Comes_Back()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files);
        var row = new OnIslandRow(Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), false);
        session.SetPick(row, true);

        session.SetPick(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha"), false);
        var off = session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha");
        Assert.False(off.On);
        Assert.Null(session.Picks.ById(off.Pick.Id));

        session.SetPick(off, true);
        Assert.True(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha").On);
        Assert.NotNull(session.Picks.ById(off.Pick.Id));
    }

    [Fact]
    public void A_Switched_Off_Pick_Gets_Its_Key_Back_When_It_Is_Free_And_The_Person_Is_Told_When_It_Is_Not()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));
        var alpha = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        session.SetPick(new OnIslandRow(alpha, false), true);
        var combo = new KeyPress(0x4A, HotkeyModifiers.Control | HotkeyModifiers.Alt, false);
        Assert.True(session.PressKey(alpha.Id, combo).Changed);

        session.SetPick(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha"), false);
        Assert.Null(session.Settings.PickKeyFor(alpha.Id));
        session.SetPick(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha"), true);
        Assert.NotNull(session.Settings.PickKeyFor(alpha.Id)); // free: given back

        session.SetPick(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha"), false);
        Assert.True(session.PressKey(KeybindEditor.MainId, combo).Changed); // the main key takes it meanwhile
        var back = session.SetPick(session.PickRows(PageIds.Apps).Single(r => r.Pick.Name == "Alpha"), true);
        Assert.Null(session.Settings.PickKeyFor(alpha.Id));
        Assert.Contains("Ctrl+Alt+J", back.Warning);
    }
}
