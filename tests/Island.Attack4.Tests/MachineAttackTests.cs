using Island.Core;

namespace Island.Attack4.Tests;

/// <summary>
/// Defect 5: the machine keeps a selection that is out of range for the page it shows. The existing storm tests
/// (Island.Tests IslandMachineTests.Random_Input_Storm_Never_Jumps, AttackTests) hold SelectedItem within the page's
/// items as an invariant; changing pages or picks while the island is not Open breaks it.
/// </summary>
public class MachineAttackTests
{
    private const double Frame = 1000.0 / 60;

    private static readonly Page Games = new("page-1", "Games", "#12AB34", "dot", null, false);

    private static void Run(IslandMachine m, ref double now, double ms)
    {
        var end = now + ms;
        while (now < end)
        {
            now += Frame;
            m.Tick(now);
        }
    }

    private static IReadOnlyList<Item> Items(int n) => [.. Enumerable.Range(0, n).Select(i => new Item($"Item {i}", "open", "It", 0))];

    [Fact]
    public void Selection_Is_Out_Of_Range_After_Picks_Are_Removed_While_The_Island_Is_Hidden()
    {
        // Apps shows 5 picks; Dan selects the 5th; the island leaves; in the settings screen he switches 3 picks off;
        // the main key brings Apps back with 2 picks and the selection still at index 4.
        var count = 5;
        var m = new IslandMachine(30, Pages.BuiltIn, p => p.Id == PageIds.Apps ? Items(count) : Items(1));
        double now = 0;
        m.PageKey(PageIds.Apps, now);
        Run(m, ref now, 2000);
        m.ItemClick(4, now);
        m.ShowHideKey(now);
        Run(m, ref now, 2000);
        Assert.Equal(IslandPhase.Hidden, m.Phase);

        count = 2;
        m.ContentsChanged(now);
        m.MainKey(now);
        Run(m, ref now, 2000);

        Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
    }

    [Fact]
    public void Selection_Is_Out_Of_Range_After_The_Selected_Page_Is_Removed_While_The_Island_Is_Hidden()
    {
        var m = new IslandMachine(30, [.. Pages.BuiltIn, Games], p => p.Id == Games.Id ? Items(6) : Items(2));
        double now = 0;
        m.PageKey(Games.Id, now);
        Run(m, ref now, 2000);
        m.ItemClick(5, now);
        m.ShowHideKey(now);
        Run(m, ref now, 2000);

        m.SetPages(Pages.BuiltIn, now); // Games deleted in the settings screen
        m.ShowHideKey(now);
        Run(m, ref now, 2000);

        Assert.Equal(PageIds.Media, m.ContentsPageId);
        Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
    }
}
