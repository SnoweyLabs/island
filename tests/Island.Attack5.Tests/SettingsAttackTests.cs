using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack5.Tests;

/// <summary>WORK-ORDER-5 section 6: SettingsSession.MovePick, on invented picks in a temporary folder.</summary>
public class SettingsAttackTests
{
    private static readonly Pick Alpha = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
    private static readonly Pick Beta = Pick.ForSite("Beta", "example.org", PageIds.Browser);
    private static readonly Pick Gamma = Pick.ForFolder("Downloads", PageIds.Folders);

    private static SettingsFiles Files(TempFolder dir) => new(dir.File("settings.json"), dir.File("pages.json"), dir.File("picks.json"));

    private static SettingsSession Open(TempFolder dir, PickStoreStatus status = PickStoreStatus.Loaded, PageStore? pages = null, IEnumerable<Pick>? picks = null) =>
        new(Files(dir),
            new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(pages ?? PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(new PickStore(picks ?? [Alpha, Beta, Gamma]), status, null),
            new HoldingRegistrar(),
            () => [],
            () => false);

    // ---- what held ----

    [Fact]
    public void Holds_A_Pick_Moves_To_Exactly_One_Other_Page_Is_Saved_And_Raises_One_Event()
    {
        using var dir = new TempFolder();
        var session = Open(dir);
        var events = new List<SettingsArea>();
        session.Changed += events.Add;

        var result = session.MovePick(Alpha.Id, PageIds.Vibe);

        Assert.True(result.Ok);
        Assert.True(result.Changed);
        Assert.Equal([SettingsArea.Picks], events);
        Assert.Equal(PageIds.Vibe, session.Picks.ById(Alpha.Id)!.PageId);
        Assert.Equal(3, session.Picks.Picks.Count);
        Assert.Equal(1, session.Picks.Picks.Count(p => p.Id == Alpha.Id)); // on exactly one page
        Assert.Equal(Alpha with { PageId = PageIds.Vibe }, PickStore.Load(dir.File("picks.json")).Store.ById(Alpha.Id)); // saved at once
        Assert.Equal([Beta, Gamma], session.Picks.Picks.Where(p => p.Id != Alpha.Id)); // nothing else changed

        Assert.True(session.MovePick(Alpha.Id, PageIds.Apps).Changed); // and back
        Assert.Equal(Alpha, session.Picks.ById(Alpha.Id));
    }

    [Fact]
    public void Holds_A_Missing_Page_Or_Pick_Is_Refused_With_Words_And_Nothing_Is_Written()
    {
        using var dir = new TempFolder();
        var session = Open(dir);
        var events = new List<SettingsArea>();
        session.Changed += events.Add;

        var noPage = session.MovePick(Alpha.Id, "no-such-page");
        Assert.False(noPage.Ok);
        Assert.Equal(SettingsText.NoSuchPage, noPage.Refusal);
        var noPick = session.MovePick("program:nobody", PageIds.Vibe);
        Assert.Equal(SettingsText.NoSuchPick, noPick.Refusal);
        Assert.Equal(SettingsText.NoSuchPage, session.MovePick("program:nobody", "no-such-page").Refusal); // the page is checked first
        Assert.Equal(SettingsText.NoSuchPage, session.MovePick("", "").Refusal);
        Assert.Equal(SettingsText.NoSuchPick, session.MovePick("", PageIds.Vibe).Refusal);

        Assert.Empty(events);
        Assert.False(dir.Exists("picks.json")); // a refused move never touches the file
        Assert.Equal([Alpha, Beta, Gamma], session.Picks.Picks);
    }

    [Fact]
    public void Holds_A_Locked_Picks_File_Refuses_Every_Move_And_Writes_Nothing()
    {
        using var dir = new TempFolder();
        var session = Open(dir, PickStoreStatus.Unreadable);
        var result = session.MovePick(Alpha.Id, PageIds.Vibe);
        Assert.False(result.Ok);
        Assert.False(result.Changed);
        Assert.Equal(PageIds.Apps, session.Picks.ById(Alpha.Id)!.PageId);
        Assert.False(dir.Exists("picks.json"));
        Assert.False(session.MovePick("program:nobody", "no-such-page").Ok); // the file's state is reported before anything else
    }

    [Fact]
    public void Holds_A_File_That_Cannot_Be_Written_Refuses_And_Keeps_The_Pick_Where_It_Was()
    {
        using var dir = new TempFolder();
        var session = Open(dir);
        Assert.True(session.MovePick(Alpha.Id, PageIds.Vibe).Ok); // the file now exists
        File.SetAttributes(dir.File("picks.json"), FileAttributes.ReadOnly);

        var events = new List<SettingsArea>();
        session.Changed += events.Add;
        var result = session.MovePick(Beta.Id, PageIds.Media);

        Assert.False(result.Ok);
        Assert.Contains("could not be saved", result.Refusal);
        Assert.Equal(PageIds.Browser, session.Picks.ById(Beta.Id)!.PageId); // nothing changed in memory either
        Assert.Empty(events);
    }

    [Fact]
    public void Holds_Moving_A_Thousand_Picks_Keeps_Them_All_And_Their_Order()
    {
        using var dir = new TempFolder();
        var picks = Enumerable.Range(0, PickStore.MaxPicks).Select(i => Pick.ForProgram($"P{i}", PageIds.Apps, $"p{i}.exe", null)).ToList();
        var session = Open(dir, picks: picks);
        for (var i = 0; i < picks.Count; i += 100) Assert.True(session.MovePick(picks[i].Id, PageIds.Folders).Changed);
        Assert.Equal(picks.Select(p => p.Id), session.Picks.Picks.Select(p => p.Id));
        Assert.Equal(10, session.Picks.ForPage(PageIds.Folders).Count);
        Assert.Equal(PickStore.MaxPicks, PickStore.Load(dir.File("picks.json")).Store.Picks.Count);
    }

    // ---- what broke ----

    [Fact]
    public void Defect_A_Move_To_The_Page_The_Pick_Is_Already_On_Fails_When_The_File_Is_Read_Only()
    {
        // Moving a pick to the page it is on changes nothing, but ApplyPicks saves the file anyway, so with a file that
        // cannot be written the person is told "picks.json could not be saved" for a move that needed no saving.
        using var dir = new TempFolder();
        var session = Open(dir);
        Assert.True(session.MovePick(Alpha.Id, PageIds.Vibe).Ok);
        File.SetAttributes(dir.File("picks.json"), FileAttributes.ReadOnly);

        var result = session.MovePick(Alpha.Id, PageIds.Vibe);

        Assert.True(result.Ok, result.Refusal);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Defect_A_Refused_Save_Leaves_A_Temporary_File_Beside_The_Picks()
    {
        // PickStore.Save writes picks.json.tmp and then moves it over picks.json; when the move fails (a read-only target)
        // the temporary file stays behind, holding the pick list that was refused.
        using var dir = new TempFolder();
        var session = Open(dir);
        Assert.True(session.MovePick(Alpha.Id, PageIds.Vibe).Ok);
        File.SetAttributes(dir.File("picks.json"), FileAttributes.ReadOnly);

        Assert.False(session.MovePick(Beta.Id, PageIds.Media).Ok);

        Assert.False(dir.Exists("picks.json.tmp"), "picks.json.tmp was left behind by the refused save");
    }

    [Fact]
    public void Defect_A_Move_That_Would_Make_The_Pick_Unstorable_Reports_Success_With_Nothing_Changed()
    {
        // A page whose id the picks cannot hold (the page store's own rules forbid it, the constructor does not) makes
        // PickStore.Move keep the pick where it was. The session then answers "ok, nothing changed" with no refusal and no words,
        // so the settings screen shows no problem for a move that did not happen.
        using var dir = new TempFolder();
        var odd = new Page("odd/page", "Odd", "#123456", "dot", null, false);
        var session = Open(dir, pages: new PageStore([.. Pages.BuiltIn, odd]));

        var result = session.MovePick(Alpha.Id, odd.Id);

        Assert.Equal(PageIds.Apps, session.Picks.ById(Alpha.Id)!.PageId);
        Assert.False(result.Changed);
        Assert.False(result.Ok, "the move did nothing, yet the result is Ok with no refusal");
    }
}
