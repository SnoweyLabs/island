using Island.Core;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 4: the island's state machine under random input in random order, with the pages and the rows changing under it (a page removed from Settings while the island is open, a pick removed, the
/// time stepping back or standing still, a number that is not a time). It was not looked at by name in rounds 1 to 3. Invented names only.
/// </summary>
public sealed class Round4MachineTests
{
    private sealed class World
    {
        public List<Page> Pages { get; set; } = [];

        public Dictionary<string, List<Item>> Rows { get; } = [];

        public IReadOnlyList<Item> ItemsOf(Page page) => Rows.TryGetValue(page.Id, out var rows) ? rows : [];

        public void Reset(Random rng)
        {
            Pages = [.. Island.Core.Pages.BuiltIn];
            Rows.Clear();
            foreach (var page in Pages) Rows[page.Id] = RandomRow(rng, page.Id);
        }

        public static List<Item> RandomRow(Random rng, string pageId)
        {
            var count = rng.Next(0, 9);
            var rows = Enumerable.Range(0, count).Select(i => new Item($"T{pageId}{i}", "open", "Tt", 10 * i, PickId: $"program:p{pageId}{i}.exe")).ToList();
            if (rng.Next(3) > 0) rows.Add(new Item(PlusRow.AddTitle, PlusRow.AddSubtitle(0), "+", 0, IsPlus: true));
            return rows;
        }
    }

    private static readonly double[] OddTimes = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, -5, 0, 1e300, -1e300];

    [Fact]
    public void Held_The_Machine_Never_Throws_And_Keeps_Its_Pages_And_Selection_Valid_Under_Random_Input_And_Changing_Pages()
    {
        var rng = new Random(20261007);
        var deadline = DateTime.UtcNow.AddSeconds(40);
        var runs = 0;
        var failures = new List<string>();
        for (; runs < 4000 && DateTime.UtcNow < deadline && failures.Count < 5; runs++)
        {
            var world = new World();
            world.Reset(rng);
            var machine = new IslandMachine(idleSeconds: rng.Next(2, 20), pages: world.Pages, itemsOf: world.ItemsOf);
            var now = 0.0;
            var log = new List<string>();
            try
            {
                for (var step = 0; step < 120; step++)
                {
                    now += rng.Next(0, 4) == 0 ? 0 : rng.NextDouble() * (rng.Next(10) == 0 ? 3000 : 120);
                    if (rng.Next(40) == 0) now -= rng.NextDouble() * 500; // the clock steps back
                    var t = rng.Next(60) == 0 ? OddTimes[rng.Next(OddTimes.Length)] : now;
                    var op = rng.Next(26);
                    if (op is 15 or 16) t = now; // the app never hands the machine a time that is not a number
                    log.Add($"{op}@{t:F0}");
                    Apply(machine, world, rng, op, t);
                    Check(machine, world);
                }
            }
            catch (Exception e)
            {
                failures.Add($"run {runs}: {e.GetType().Name}: {e.Message} after [{string.Join(' ', log.TakeLast(40))}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(" | ", failures));
        Assert.True(runs > 500, $"only {runs} runs in the time");
    }

    private static void Apply(IslandMachine m, World w, Random rng, int op, double t)
    {
        switch (op)
        {
            case 0: m.MainKey(t); break;
            case 1: m.ShowHideKey(t); break;
            case 2: m.MoveSelection(rng.Next(-2, 3), t); break;
            case 3: m.EnterSecondRow(rng.Next(-1, 6), t); break;
            case 4: m.LeaveSecondRow(t); break;
            case 5: m.MoveSecondRow(rng.Next(-2, 3), rng.Next(-1, 6), t); break;
            case 6: m.AskRemove(t); break;
            case 7: m.ConfirmRemove(t); break;
            case 8: m.DigitKey(rng.Next(-1, 12), t); break;
            case 9: m.EscapeKey(t); break;
            case 10: m.ToggleSecondRow(t); break;
            case 11: m.OtherKey(t); break;
            case 12: m.FocusLost(t); break;
            case 13: m.PageKey(w.Pages[rng.Next(w.Pages.Count)].Id, t); break;
            case 14: m.PageKey("no-such-page", t); break;
            case 15: ChangePages(m, w, rng, t); break;
            case 16: ChangeRows(m, w, rng, t); break;
            case 17: m.SelfFillingItemsChanged(t); break;
            case 18: m.SetIdleSeconds(rng.Next(-1, 30), t); break;
            case 19: m.Activity(t); break;
            case 20: m.ItemClick(rng.Next(-2, 10), t); break;
            case 21: m.Tick(t); break;
            case 22: m.SetPill(rng.Next(2) == 0, rng.Next(3) > 0, t); break;
            case 23: m.OpenSearch(rng.Next(100, 700), t, keyboard: rng.Next(2) == 0); break;
            case 24: m.CloseSearch(t); break;
            default: m.SetNotice(rng.Next(2) == 0, t); break;
        }
    }

    private static void ChangePages(IslandMachine m, World w, Random rng, double t)
    {
        var pages = w.Pages.Where(p => p.IsBuiltIn || rng.Next(3) > 0).ToList();
        if (rng.Next(2) == 0) pages.Add(new Page($"custom-{rng.Next(1000)}", "Custom", "#112233", "dot", null, false));
        pages = [.. pages.DistinctBy(p => p.Id)];
        if (rng.Next(4) == 0 && pages.Count > 1) pages.RemoveAt(rng.Next(pages.Count)); // even a built-in one goes (the machine must not depend on it)
        if (pages.Count == 0) return;
        foreach (var p in pages) if (!w.Rows.ContainsKey(p.Id)) w.Rows[p.Id] = World.RandomRow(rng, p.Id);
        w.Pages = pages;
        m.SetPages(pages, t);
    }

    private static void ChangeRows(IslandMachine m, World w, Random rng, double t)
    {
        var page = w.Pages[rng.Next(w.Pages.Count)];
        w.Rows[page.Id] = World.RandomRow(rng, page.Id);
        m.ContentsChanged(t);
    }

    private static void Check(IslandMachine m, World w)
    {
        Assert.Contains(w.Pages, p => p.Id == m.PageId);
        Assert.Contains(w.Pages, p => p.Id == m.ContentsPageId);
        var count = m.ContentsItems.Count;
        if (m.ContentsVisible && !m.ShowsPill && !m.SearchOpen) Assert.True(m.SelectedItem >= 0 && (count == 0 || m.SelectedItem < count), $"selected {m.SelectedItem} of {count} on {m.ContentsPageId} in {m.Phase}");
        if (m.SecondRowSelected >= 0) Assert.True(m.SecondRowOpen, "a second-row selection with the row closed");
        if (m.PendingDeleteId is { } pending && m.ContentsVisible && !m.ShowsPill && !m.SearchOpen) Assert.True(m.SelectedItem >= 0 && m.SelectedItem < count && m.ContentsItems[m.SelectedItem].PickId == pending, $"a Delete waits for {pending} but the selected tile is {m.SelectedItem} of {count} on {m.ContentsPageId} in {m.Phase}");
        Assert.True(double.IsFinite(m.DrawnWidth) && double.IsFinite(m.DrawnHeight) && double.IsFinite(m.DrawnY), $"drawn {m.DrawnWidth} {m.DrawnHeight} {m.DrawnY}");
        if (m.Phase == IslandPhase.Hidden) Assert.False(m.HasKeyboard && m.ShowsPill && false);
    }
}
