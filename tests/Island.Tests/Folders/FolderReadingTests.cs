using Island.Core;

namespace Island.Tests;

public class FolderReadingTests
{
    private static readonly FolderMatch Match = new([("Downloads", @"X:\Alpha\Downloads"), ("Desktop", @"X:\Alpha\Desktop")]);
    private static readonly IReadOnlyDictionary<long, IReadOnlyList<long>> NoTabInfo = new Dictionary<long, IReadOnlyList<long>>();

    private static IReadOnlyDictionary<long, IReadOnlyList<long>> Tabs(long frame, params long[] tabsTopFirst) =>
        new Dictionary<long, IReadOnlyList<long>> { [frame] = tabsTopFirst };

    [Fact]
    public void Each_Tab_Is_One_Entry_And_Shares_Its_Frame_Handle()
    {
        var raw = new[]
        {
            new RawFolderEntry(100, 11, @"X:\Alpha\Downloads", 3),
            new RawFolderEntry(100, 12, @"X:\Alpha\Desktop", 3),
        };

        var list = FolderReading.Build(raw, Tabs(100, 11, 12), Match);

        Assert.Equal(2, list.Count);
        Assert.All(list, w => Assert.Equal(100, w.Handle));
        Assert.Equal(["Downloads", "Desktop"], list.Select(w => w.KnownFolder));
    }

    [Fact]
    public void Active_Tab_Is_The_First_Tab_Window_In_Z_Order()
    {
        Assert.Equal(12, FolderReading.ActiveTab([12, 11]));
        Assert.Equal(0, FolderReading.ActiveTab([]));
        Assert.Equal(0, FolderReading.ActiveTab(null));
    }

    [Fact]
    public void Active_Tab_Comes_First_Inside_Its_Frame_Whatever_The_Shell_Order()
    {
        var raw = new[]
        {
            new RawFolderEntry(100, 11, @"X:\Alpha\Downloads", 3),
            new RawFolderEntry(100, 12, @"X:\Alpha\Desktop", 3),
        };

        var list = FolderReading.Build(raw, Tabs(100, 12, 11), Match);

        Assert.Equal(["Desktop", "Downloads"], list.Select(w => w.KnownFolder));
    }

    [Fact]
    public void Top_Most_Frame_Is_Listed_First()
    {
        var raw = new[]
        {
            new RawFolderEntry(200, 21, @"X:\Alpha\Desktop", 9),
            new RawFolderEntry(100, 11, @"X:\Alpha\Downloads", 2),
        };

        var list = FolderReading.Build(raw, NoTabInfo, Match);

        Assert.Equal([100L, 200L], list.Select(w => w.Handle));
        Assert.Equal([2, 9], list.Select(w => w.ZOrder));
    }

    [Fact]
    public void Entries_Without_A_Path_Or_Frame_Are_Dropped()
    {
        var raw = new[]
        {
            new RawFolderEntry(100, 11, null, 1),
            new RawFolderEntry(100, 12, "  ", 1),
            new RawFolderEntry(0, 13, @"X:\Alpha\Downloads", 1),
            new RawFolderEntry(100, 14, @"X:\Alpha\Downloads", 1),
        };

        var list = FolderReading.Build(raw, NoTabInfo, Match);

        Assert.Single(list);
    }

    [Fact]
    public void The_Same_Tab_Listed_Twice_Is_One_Entry_But_Unknown_Tabs_Are_Not_Merged()
    {
        var twice = new[]
        {
            new RawFolderEntry(100, 11, @"X:\Alpha\Downloads", 1),
            new RawFolderEntry(100, 11, @"X:\Alpha\Downloads", 1),
        };
        var unknownTabs = new[]
        {
            new RawFolderEntry(100, 0, @"X:\Alpha\Downloads", 1),
            new RawFolderEntry(100, 0, @"X:\Alpha\Desktop", 1),
        };

        Assert.Single(FolderReading.Build(twice, NoTabInfo, Match));
        Assert.Equal(2, FolderReading.Build(unknownTabs, NoTabInfo, Match).Count);
    }

    [Fact]
    public void A_Folder_That_Is_Not_Known_Is_Still_Listed_Without_A_Known_Folder()
    {
        var raw = new[] { new RawFolderEntry(100, 11, @"X:\Gamma\Beta", 1) };

        var list = FolderReading.Build(raw, NoTabInfo, Match);

        Assert.Null(Assert.Single(list).KnownFolder);
    }
}
