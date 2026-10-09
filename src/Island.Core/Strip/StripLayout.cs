namespace Island.Core;

/// <summary>
/// WORK-ORDER-5 §4: the row of tiles is a strip. At most <see cref="ChoiceConstants.MaxVisibleTiles"/> picks show at once;
/// the capsule's width follows the width rule for the picks shown plus the + tile and never grows beyond that. With more
/// picks than that they slide inside the capsule; the + tile stays at the right end of the picks.
/// </summary>
public static class StripLayout
{
    public static int PickCount(IReadOnlyList<Item> items) => items.Count(i => !i.IsPlus);

    /// <summary>A page has at most one + tile (a second one would share its slot): it counts once.</summary>
    public static int PlusCount(IReadOnlyList<Item> items) => items.Any(i => i.IsPlus) ? 1 : 0;

    private static int _limit = ChoiceConstants.MaxVisibleTiles;

    /// <summary>
    /// The most picks the capsule shows at once on the screen it is on: seven, or fewer on a screen whose work area is narrower than
    /// the island (WORK-ORDER-6 §1, <see cref="ScreenFit"/>). Set by the app at a summon, while nothing of the island is visible.
    /// </summary>
    public static int VisibleLimit
    {
        get => Volatile.Read(ref _limit);
        set => Volatile.Write(ref _limit, Math.Clamp(value, 1, ChoiceConstants.MaxVisibleTiles));
    }

    /// <summary>How many picks show at once for this many picks (the second row can show fewer: it is as wide as the capsule lets it be).</summary>
    public static int VisiblePicks(int picks, int? maxVisible = null) => Math.Clamp(picks, 0, Math.Max(0, maxVisible ?? VisibleLimit));

    /// <summary>How many tiles fit in a row of this width (a tile and its gap are one pitch; the last tile has no gap after it).</summary>
    public static int TilesThatFit(double width) =>
        width < LookConstants.ItemSize ? 0 : (int)Math.Floor((width + LookConstants.WidthItemTrailingGap) / LookConstants.WidthItemPitch);

    /// <summary>How many tiles the capsule is as wide as: the picks shown plus the + tile (the number for the width rule).</summary>
    public static int ShownTiles(IReadOnlyList<Item> items) => VisiblePicks(PickCount(items)) + PlusCount(items);

    /// <summary>Width of the sliding part, from the left of the first slot to the right of the last: the tiles and their gaps.</summary>
    public static double StripWidth(int visiblePicks) =>
        visiblePicks <= 0 ? 0 : visiblePicks * LookConstants.WidthItemPitch - LookConstants.WidthItemTrailingGap;

    /// <summary>
    /// Where the left edge of a tile is, from the left of the first slot, when the picks have slid by <paramref name="position"/> tiles.
    /// A pick moves with the strip; the + tile (<paramref name="isPlus"/>) never does: it sits in the slot after the visible picks.
    /// </summary>
    public static double TileLeft(int index, bool isPlus, double position, int visiblePicks) =>
        isPlus ? visiblePicks * LookConstants.WidthItemPitch : (index - position) * LookConstants.WidthItemPitch;
}
