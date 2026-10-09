namespace Island.Core;

/// <summary>
/// WORK-ORDER-5 §5: what the second row (the + opens it) holds and says. It lists what is open right now and is not a pick —
/// programs, folders, and browser tabs when the add-on is connected. Pointing at a tile names it; a click jumps to it once
/// (nothing is added); its small + adds it to the page being shown. Lives in memory only: nothing of it is written.
/// </summary>
public static class PlusRow
{
    /// <summary>The most tiles the second row holds; more than fit slide.</summary>
    public const int MaxEntries = 40;

    public const string Label = "Open now";
    public const string NothingElse = "Nothing else is open";
    public const string AddTitle = "Add something";
    public const string HoverSubtitle = "open · not on the island";

    /// <summary>The second line of the + tile's text block: how many things are open and not on the island.</summary>
    public static string AddSubtitle(int entries) => $"{entries} open";

    public static IReadOnlyList<PlusEntry> Entries(OpenSnapshot open, PickStore store, IReadOnlyList<InstalledProgram> installed, string? ownExe = null) =>
        [.. PlusList.Candidates(open, store, installed, ownExe).Take(MaxEntries)];

    /// <summary>The store after the small + of an entry was pressed: the thing is on the page being shown. The same store when it cannot be stored or is already there.</summary>
    public static PickStore AddedTo(PickStore store, PlusEntry entry, string pageId)
    {
        var pick = entry.ToPick(pageId);
        return pick is null ? store : store.Add(pick, out _);
    }

    /// <summary>Where a click on a tile goes, once: the first window or tab; null when it has none. Nothing is stored.</summary>
    public static long? JumpTarget(PlusEntry entry) => entry.Targets.Count > 0 ? entry.Targets[0] : null;
}
