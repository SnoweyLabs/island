using Island.Core;

namespace Island.Attack4.Tests;

/// <summary>Defects in the + list (PlusList) and in PickStore edits.</summary>
public class PickAttackTests
{
    private static TabInfo Tab(int id, string host, long order) => new($"p1:{id}", 1, id, "Alpha", host, false, false, order, null);

    [Fact]
    public void Plus_List_Offers_A_Localhost_Tab_That_Can_Never_Be_Added()
    {
        // Defect 3. The add-on sends url.hostname: "localhost" for http://localhost:3000, "[::1]" for http://[::1]/.
        // Neither can be stored as a site pick (no dot / a colon), so the row's add control silently does nothing
        // (IslandPanels.AddEntry drops a null ToPick). Program rows are filtered this way already; site rows are not.
        var open = new OpenSnapshot([], [], [Tab(1, "localhost", 1), Tab(2, "[::1]", 2)], true);

        var entries = PlusList.Candidates(open, PickStore.Empty, []);

        Assert.All(entries, e => Assert.NotNull(e.ToPick(PageIds.Browser)));
    }

    [Fact]
    public void Plus_List_Shows_One_Packaged_Program_Twice_When_One_Of_Its_Windows_Is_Minimised()
    {
        // Defect 4. WindowReader gives a minimised UWP window no exe, only the package family; an open window of the
        // same app has both. PlusList groups by the (exe, package) pair, so one program becomes two rows of
        // "open" each, while its pick on the island counts both windows (PickStates matches exe OR package).
        var open = new OpenSnapshot(
            [new OpenWindow(1, "alpha.exe", "Alpha.App_1", "Alpha", 0), new OpenWindow(2, null, "Alpha.App_1", "Alpha", 1)],
            [], [], false);

        var entries = PlusList.Candidates(open, PickStore.Empty, []);

        var entry = Assert.Single(entries);
        Assert.Equal(2, entry.Count);
    }

    [Fact]
    public void Move_To_An_Empty_Page_Id_Makes_Every_Later_Save_Fail()
    {
        // Defect 6. Add refuses a pick that cannot be stored; Move does not check the page it is given. After one
        // Move to "" (or "a/b", "x:y") the store holds an unstorable pick, Save refuses the whole file, and since
        // PickBook ignores Save's answer every later add, remove and move is silently lost at the next start.
        using var dir = new TempFolder();
        var alpha = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
        var store = PickStore.Empty.Add(alpha, out _);

        var moved = store.Move(alpha.Id, "");
        var beta = moved.Add(Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null), out _);

        Assert.True(beta.Save(dir.File("picks.json")), "a refused move must leave a store that can still be saved");
    }

    [Fact]
    public void Save_Writes_A_Pick_Twice_And_The_Next_Start_Cannot_Read_The_File()
    {
        // Defect 7. ReplacePage keeps duplicates inside the replacement and Save does not check ids, but Parse refuses
        // "The same pick appears twice": the app then starts with no picks at all (and leaves the file as it is).
        using var dir = new TempFolder();
        var site = Pick.ForSite("Example", "example.org", PageIds.Browser);
        var store = PickStore.Empty.ReplacePage(PageIds.Browser, [site, site]);
        var path = dir.File("picks.json");

        var saved = store.Save(path);
        var load = PickStore.Load(path);

        Assert.True(!saved || load.Status == PickStoreStatus.Loaded, $"saved: {saved}, read back: {load.Status} ({load.Detail})");
    }
}
