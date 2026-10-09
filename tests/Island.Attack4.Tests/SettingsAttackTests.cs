using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack4.Tests;

/// <summary>Defects in PageStore and SettingsSession.</summary>
public class SettingsAttackTests
{
    private static SettingsSession Open(SettingsFiles files, IHotkeyRegistrar registrar) => new(
        files,
        new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
        new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
        new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null),
        registrar,
        () => [],
        () => false);

    [Fact]
    public void Deleting_A_Page_While_Settings_Is_Read_Only_Says_Done_But_Keeps_Its_Key_For_The_Next_New_Page()
    {
        // Defect 10. DeletePage saves picks and pages, then DropPageKey ignores that settings.json could not be
        // saved: the result is Ok, Windows still holds the key, Settings still lists it, and the next page created
        // reuses the freed id "page-1" and silently inherits the deleted page's key.
        using var dir = new TempFolder();
        var files = new SettingsFiles(dir.File("settings.json"), dir.File("pages.json"), dir.File("picks.json"));
        var registrar = new HoldingRegistrar();
        var session = Open(files, registrar);
        var key = HotkeyCombo.Parse("Ctrl+Alt+G");
        Assert.True(session.CreatePage("Games", "#12AB34", key).Ok);
        var games = session.Pages.Pages[^1].Id;
        File.SetAttributes(files.SettingsPath, FileAttributes.ReadOnly);

        var deleted = session.DeletePage(games);
        var created = session.CreatePage("Other", "#AB1234");
        var other = session.Pages.Pages[^1].Id;

        Assert.True(created.Ok);
        Assert.True(!deleted.Ok || (!registrar.Held.Contains(key) && session.Settings.KeyFor(games) is null),
            $"delete said Ok, but the key is still held: {registrar.Held.Contains(key)}");
        Assert.Null(session.Settings.KeyFor(other)); // a new page has no key of its own
    }

    [Fact]
    public void Page_Name_That_Differs_Only_By_A_Double_Space_Is_Accepted_As_A_Second_Page()
    {
        // Defect 11 (low). EVALS P6: "a name already used" is refused. Case and outer spaces are caught; inner runs of
        // spaces are not, so "Side  Work" sits next to "Side Work" and the two look the same on the island.
        var pages = PageStore.Default.Create("Side Work", "#112233").Store;

        var second = pages.Create("Side  Work", "#445566");

        Assert.True(second.Refused);
    }

    [Fact]
    public void Pages_File_With_A_Newline_After_A_Page_Id_Is_Loaded()
    {
        // Defect 2, second input: the same "$" before a final newline in PageStore's id rule ("^[a-z0-9-]{1,40}$").
        const string json = """
            {"pages":[
             {"id":"media","name":"Media","color":"#111111"},{"id":"folders","name":"Folders","color":"#222222"},
             {"id":"apps","name":"Apps","color":"#333333"},{"id":"vibe","name":"Vibe coding","color":"#444444"},
             {"id":"browser","name":"Browser","color":"#555555"},{"id":"games\n","name":"Games","color":"#666666"}]}
            """;

        var load = PageStore.Parse(json);

        Assert.Equal(PageStoreStatus.Unreadable, load.Status);
    }
}
