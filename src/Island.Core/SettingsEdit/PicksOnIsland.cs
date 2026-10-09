namespace Island.Core.SettingsEdit;

/// <summary>One line of "On the island": a pick and whether it is on the island now.</summary>
public sealed record OnIslandRow(Pick Pick, bool On);

/// <summary>The yes/no question before a page's starter list comes back (it throws away what Dan picked on that page).</summary>
public sealed record RestoreQuestion(Page Page, int YourPicks, int StarterPicks, string Text);

/// <summary>
/// The logic of the "On the island" section. A row is a pick of the page, on; or a starter pick of that page
/// that Dan has switched off, off. Nothing else is remembered: a pick that is not on the starter list and was
/// switched off is gone (add it again from the + list). Every change goes through <see cref="PickStore"/>.
/// </summary>
public static class PicksOnIsland
{
    /// <summary>The page's picks (on) followed by its missing starter picks (off), for the programs installed on this computer.</summary>
    public static IReadOnlyList<OnIslandRow> RowsFor(string pageId, PickStore picks, IReadOnlyList<InstalledProgram> installed)
    {
        var rows = picks.ForPage(pageId).Select(p => new OnIslandRow(p, true)).ToList();
        var missing = StarterPicks.Build(installed)
            .Where(s => s.PageId == pageId && picks.ById(s.Id) is null)
            .Select(s => new OnIslandRow(s, false));
        return [.. rows, .. missing];
    }

    /// <summary>Off removes the pick from the island (nothing that is open is closed); on puts a starter pick back.</summary>
    public static PickStore Switch(PickStore picks, OnIslandRow row, bool on) =>
        on ? picks.Add(row.Pick, out _) : picks.Remove(row.Pick.Id);

    /// <summary>Only the five built-in pages have a starter list.</summary>
    public static bool CanRestore(Page page) => page.IsBuiltIn && PageIds.CanHoldPicks(page.Id);

    public static RestoreQuestion AskRestore(Page page, PickStore picks, IReadOnlyList<InstalledProgram> installed)
    {
        var mine = picks.ForPage(page.Id).Count;
        var starter = Restore(picks, page.Id, installed).ForPage(page.Id).Count; // what would really be there afterwards
        return new RestoreQuestion(page, mine, starter, SettingsText.RestoreStarterQuestion(page.Name, mine, starter));
    }

    /// <summary>EVALS C9: the page's picks become its starter picks; every other page is left as it is.</summary>
    public static PickStore Restore(PickStore picks, string pageId, IReadOnlyList<InstalledProgram> installed) =>
        picks.ReplacePage(pageId, StarterFor(pageId, installed));

    private static IReadOnlyList<Pick> StarterFor(string pageId, IReadOnlyList<InstalledProgram> installed) =>
        [.. StarterPicks.Build(installed).Where(p => p.PageId == pageId)];
}
