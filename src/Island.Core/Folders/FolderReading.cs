namespace Island.Core;

/// <summary>
/// One entry as the shell reports it: File Explorer lists one entry per TAB, and all tabs of one window
/// share the window's handle. <see cref="TabHandle"/> is that tab's own ShellTabWindowClass window (0 when
/// unknown, as on Windows 10 where there are no tabs). <see cref="FrameZOrder"/> is the frame's rank among the
/// File Explorer frames, 0 = top-most; ranking among Explorer frames only keeps it steady while other programs
/// are moved about. It is only ever compared with other folder entries.
/// </summary>
public sealed record RawFolderEntry(long FrameHandle, long TabHandle, string? Path, int FrameZOrder);

/// <summary>Turns the raw reading of File Explorer into <see cref="FolderWindow"/> records.</summary>
public static class FolderReading
{
    /// <summary>
    /// One <see cref="FolderWindow"/> per tab. <see cref="FolderWindow.Handle"/> is the FRAME handle: clicking an
    /// open folder brings that frame forward, and tabs of one frame share it (selecting a tab is not possible
    /// without undocumented messages). The list is ordered top-most frame first; inside a frame the active tab
    /// comes first, then the other tabs in the order the shell gave them.
    /// </summary>
    /// <param name="tabsByFrame">Per frame, its tab windows in z-order, top first (the first one is the active tab).</param>
    public static IReadOnlyList<FolderWindow> Build(
        IReadOnlyList<RawFolderEntry> raw,
        IReadOnlyDictionary<long, IReadOnlyList<long>> tabsByFrame,
        FolderMatch match)
    {
        var seen = new HashSet<(long, long)>();
        return
        [
            .. raw
                .Select((entry, index) => (entry, index))
                .Where(x => x.entry.FrameHandle != 0 && !string.IsNullOrWhiteSpace(x.entry.Path))
                // The same tab listed twice is one tab; an unknown tab (0) is never merged with another.
                .Where(x => x.entry.TabHandle == 0 || seen.Add((x.entry.FrameHandle, x.entry.TabHandle)))
                .OrderBy(x => x.entry.FrameZOrder)
                .ThenBy(x => x.entry.FrameHandle)
                .ThenBy(x => TabRank(x.entry, tabsByFrame))
                .ThenBy(x => x.index)
                .Select(x => new FolderWindow(x.entry.FrameHandle, match.Resolve(x.entry.Path), x.entry.Path, x.entry.FrameZOrder)),
        ];
    }

    /// <summary>The active tab of a frame: the first ShellTabWindowClass child in z-order. 0 when the frame has none.</summary>
    public static long ActiveTab(IReadOnlyList<long>? tabsInZOrder) => tabsInZOrder is { Count: > 0 } ? tabsInZOrder[0] : 0;

    private static int TabRank(RawFolderEntry entry, IReadOnlyDictionary<long, IReadOnlyList<long>> tabsByFrame)
    {
        if (!tabsByFrame.TryGetValue(entry.FrameHandle, out var tabs)) return int.MaxValue;
        var rank = tabs.ToList().IndexOf(entry.TabHandle); // rank 0 is the active tab
        return rank < 0 ? int.MaxValue : rank;
    }
}
