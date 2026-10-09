using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests;

/// <summary>
/// WORK-ORDER-5 §6, "Can a removed pick be put back?": found out with temporary stores, for a program, a folder and a site, each
/// open and closed, by the routes that exist in the app as built: the second row ("Open now": offers what is open and not on the
/// island), the settings screen's "On the island" (a starter pick that was switched off shows as an off chip) and "Restore
/// starter list". Nothing new is built for it; these tests record the answer, so that the close-out and "De hotărât de Dan" say
/// what is true.
/// </summary>
public class RemovedPickRouteTests
{
    private static readonly IReadOnlyList<InstalledProgram> Installed = Fixtures.EverythingInstalled;

    private static OpenSnapshot Open(Pick pick) => pick.Kind switch
    {
        PickKind.Program => new OpenSnapshot([new OpenWindow(1, pick.ExeName, null, "invented", 0)], [], [], false),
        PickKind.Folder => new OpenSnapshot([], [new FolderWindow(2, pick.KnownFolder, null, 0)], [], false),
        _ => new OpenSnapshot([], [], [new TabInfo("p:1", 1, 1, "invented", pick.Host!, false, true, 1, null)], true),
    };

    private static readonly OpenSnapshot NothingOpen = new([], [], [], true);

    /// <summary>The routes back for a pick that was removed: through the second row (only while the thing is open), in the settings as an off chip, by restoring the page's starter list.</summary>
    private static (bool SecondRow, bool SettingsChip, bool RestoreList) Routes(Pick removed, bool open)
    {
        var storeWithout = new PickStore(StarterPicks.Build(Installed).Where(p => p.Id != removed.Id));
        var viaRow = PlusRow.Entries(open ? Open(removed) : NothingOpen, storeWithout, Installed).Any(e => e.ToPick(removed.PageId)?.Id == removed.Id);
        var chip = PicksOnIsland.RowsFor(removed.PageId, storeWithout, Installed).Any(r => !r.On && r.Pick.Id == removed.Id);
        var restore = PicksOnIsland.Restore(storeWithout, removed.PageId, Installed).ById(removed.Id) is not null;
        return (viaRow, chip, restore);
    }

    [Fact]
    public void A_Starter_Pick_Comes_Back_Through_The_Settings_Whether_It_Is_Open_Or_Closed()
    {
        var starter = StarterPicks.Build(Installed);
        var program = starter.First(p => p.Kind == PickKind.Program);
        var folder = starter.First(p => p.Kind == PickKind.Folder);
        var site = starter.First(p => p.Kind == PickKind.Site);

        foreach (var pick in new[] { program, folder, site })
        {
            foreach (var open in new[] { true, false })
            {
                var routes = Routes(pick, open);
                Assert.True(routes.SettingsChip, $"{pick.Kind} open={open}: the settings show it as an off chip");
                Assert.True(routes.RestoreList, $"{pick.Kind} open={open}: restoring the starter list brings it back");
            }
        }
    }

    [Fact]
    public void A_Pick_That_Is_Not_On_The_Starter_List_Comes_Back_Only_While_It_Is_Open()
    {
        var custom = new[]
        {
            Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null),
            Pick.ForFolder("Music", PageIds.Folders),
            Pick.ForSite("Example", "example.org", PageIds.Browser),
        };

        foreach (var pick in custom)
        {
            // Open: the second row offers it again (a site only with the add-on connected).
            var whileOpen = Routes(pick, open: true);
            Assert.True(whileOpen.SecondRow, $"{pick.Kind} open: the second row offers it");
            Assert.False(whileOpen.SettingsChip, $"{pick.Kind}: not on the starter list, so no off chip");
            Assert.False(whileOpen.RestoreList, $"{pick.Kind}: not on the starter list, so restoring does not bring it back");

            // Closed: nothing shows it. It is gone until it is opened.
            var whileClosed = Routes(pick, open: false);
            Assert.False(whileClosed.SecondRow || whileClosed.SettingsChip || whileClosed.RestoreList, $"{pick.Kind} closed: no route back");
        }
    }
}
