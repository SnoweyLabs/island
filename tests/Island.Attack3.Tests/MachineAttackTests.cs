using Island.Core;

namespace Island.Attack3.Tests;

/// <summary>ATTACK3 on IslandMachine: pages and contents changing under it, in every phase (EVALS M3).</summary>
public class MachineAttackTests
{
    private const double Step = 1000.0 / 120;

    // ---- Defects ---------------------------------------------------------------------------------------------

    [Fact]
    public void Selection_Is_Inside_The_Items_After_A_Summon_When_The_Page_Shrank_While_Hidden()
    {
        // Select the sixth pick, let the island go, remove picks (the settings screen, say) so the page has two,
        // summon it again with the main key: SelectedItem is still 5. The view clamps it when drawing, but the
        // machine's own output points past the end of its own ContentsItems.
        var count = 6;
        var m = new IslandMachine(5, Pages.BuiltIn, p => p.Id == PageIds.Apps
            ? [.. Enumerable.Range(0, count).Select(i => new Item($"Item {i}", "open", "It", 0))]
            : []);
        double now = 0;
        void RunFor(double ms)
        {
            for (var end = now + ms; now < end; now += Step) m.Tick(now);
        }

        m.PageKey(PageIds.Apps, now);
        RunFor(2000);
        m.ItemClick(5, now);
        Assert.Equal(5, m.SelectedItem);
        m.ShowHideKey(now);
        RunFor(3000);
        Assert.Equal(IslandPhase.Hidden, m.Phase);

        count = 2;
        m.MainKey(now);
        RunFor(2000);

        Assert.True(m.IsAtRest);
        Assert.InRange(m.SelectedItem, 0, m.ContentsItems.Count - 1);
    }

    // ---- Coverage that holds -------------------------------------------------------------------------------

    [Theory]
    [InlineData(3)]
    [InlineData(2026)]
    [InlineData(777777)]
    public void Pages_And_Contents_Changing_In_Every_Phase_Never_Jump(int seed)
    {
        // Like IslandMachineTests.Random_Input_Storm_Never_Jumps, plus what that storm leaves out: SetPages with an
        // empty list, a list without the current page, duplicate ids and ten thousand pages; ContentsChanged with item
        // counts from 0 to 40 changing under the machine.
        var rng = new Random(seed);
        var counts = new Dictionary<string, int>();
        IReadOnlyList<Item> ItemsOf(Page p) => [.. Enumerable.Range(0, counts.GetValueOrDefault(p.Id, 3)).Select(i => new Item($"Item {i}", "", "It", 0))];
        var custom = Enumerable.Range(0, 10_000).Select(i => new Page($"p{i}", $"Page {i}", "#336699", "grid", null, false)).ToList();
        IReadOnlyList<Page>[] pageLists =
        [
            [],
            Pages.BuiltIn,
            [Pages.BuiltIn[2]],
            [Pages.BuiltIn[0], Pages.BuiltIn[0], Pages.BuiltIn[3]],
            [.. Pages.BuiltIn.Skip(1)],
            custom,
            [.. custom.Take(3), Pages.BuiltIn[0] with { Name = "Renamed" }],
        ];
        IReadOnlyList<Page> current = Pages.BuiltIn;

        var m = new IslandMachine(2, Pages.BuiltIn, ItemsOf);
        var previous = Springs(m);
        var previousPhase = m.Phase;
        double now = 0;

        for (var n = 0; n < 20_000; n++)
        {
            var start = now;
            now = n * Step;
            if (rng.NextDouble() < 0.06)
            {
                var at = start + rng.NextDouble() * (now - start);
                switch (rng.Next(11))
                {
                    case 0: m.ShowHideKey(at); break;
                    case 1: m.MainKey(at); break;
                    case 2: m.DigitKey(rng.Next(-2, 12), at); break;
                    case 3: m.EscapeKey(at); break;
                    case 4: m.FocusLost(at); break;
                    case 5: m.PageKey(current[rng.Next(current.Count)].Id, at); break;
                    case 6: m.PageKey("no-such-page", at); break;
                    case 7:
                        var list = pageLists[rng.Next(pageLists.Length)];
                        m.SetPages(list, at);
                        if (list.Count > 0) current = list;
                        break;
                    case 8:
                        counts[m.ContentsPageId] = rng.Next(0, 41);
                        m.ContentsChanged(at);
                        break;
                    case 9: m.ItemClick(rng.Next(-3, 45), at); break;
                    default: m.OtherKey(at); break;
                }
            }

            m.Tick(now);
            var springs = Springs(m);
            if (previousPhase != IslandPhase.Hidden && m.Phase != IslandPhase.Hidden)
            {
                for (var i = 0; i < 4; i++)
                {
                    var (before, after) = (previous[i], springs[i]);
                    if (after.Steps < before.Steps)
                    {
                        // A snap is allowed only for an island that settled out of sight and was summoned in this frame.
                        Assert.True(Math.Abs(previous[0].Value - (-LookConstants.SpawnHeightAboveEdge)) < LookConstants.HiddenSettlePosition,
                            $"seed {seed} t={now:F1}: spring {i} was snapped while in sight (y={previous[0].Value})");
                        continue;
                    }

                    Assert.True(after.Steps - before.Steps is 0 or 1, $"seed {seed}: spring {i} took {after.Steps - before.Steps} steps in one frame");
                    if (after.Steps == before.Steps)
                        Assert.Equal(before.Value, after.Value);
                    else
                        Assert.True(Math.Abs(after.Value - (before.Value + after.Velocity * Spring.StepSeconds)) < 1e-9,
                            $"seed {seed} t={now:F1}: spring {i} jumped from {before.Value} to {after.Value}");
                }
            }

            Assert.Contains(current, p => p.Id == m.PageId);
            Assert.Contains(current, p => p.Id == m.ContentsPageId);
            Assert.True(double.IsFinite(m.DrawnWidth) && double.IsFinite(m.DrawnHeight) && double.IsFinite(m.DrawnY) && double.IsFinite(m.DrawnRadius));
            if (m.Phase is IslandPhase.Hidden or IslandPhase.Closing) Assert.False(m.HasKeyboard);
            if (m.Phase == IslandPhase.Hidden) Assert.False(m.ContentsVisible);
            previous = springs;
            previousPhase = m.Phase;
        }
    }

    [Fact]
    public void SetPages_With_An_Empty_List_Changes_Nothing_And_A_Missing_Current_Page_Falls_Back_To_The_First()
    {
        var m = new IslandMachine();
        m.PageKey(PageIds.Vibe, 0);
        for (double t = 0; t < 2000; t += Step) m.Tick(t);

        m.SetPages([], 2000);
        Assert.Equal(PageIds.Vibe, m.PageId);

        m.SetPages([Pages.BuiltIn[4], Pages.BuiltIn[1]], 2001);
        Assert.Equal(PageIds.Browser, m.PageId);
        for (double t = 2001; t < 4000; t += Step) m.Tick(t);
        Assert.Equal(PageIds.Browser, m.ContentsPageId);
        Assert.True(m.IsAtRest);

        m.DigitKey(2, 4000); // no keyboard: summoned by a page key
        Assert.Equal(PageIds.Browser, m.PageId);
    }

    private static Spring[] Springs(IslandMachine m) => [m.Y, m.Width, m.Height, m.Radius];
}
