using Island.Core;

namespace Island.Tests;

internal static class Fixtures
{
    public static InstalledProgram Program(string name, string? exe, string? package = null) =>
        new(name, exe, package, "launch-" + name); // the launch target is a made-up id, never a real path

    /// <summary>A laptop where every starter program is installed under its first candidate name.</summary>
    public static IReadOnlyList<InstalledProgram> EverythingInstalled { get; } =
    [
        .. StarterPicks.Programs.Select(s => Program(s.Name, s.ExeCandidates[0])),
    ];

    public static OpenWindow Window(long handle, string exe, int z = 0) => new(handle, exe, null, "title " + handle, z);

    public static TabInfo Tab(int tabId, string host, long order, bool audible = false) =>
        new($"c1:{tabId}", 1, tabId, "tab " + tabId, host, audible, false, order, null);

    public static OpenSnapshot Open(IEnumerable<OpenWindow>? windows = null, IEnumerable<FolderWindow>? folders = null, IEnumerable<TabInfo>? tabs = null, bool connected = false) =>
        new([.. windows ?? []], [.. folders ?? []], [.. tabs ?? []], connected);
}

public class PickListTests
{
    [Fact]
    public void Unpicked_Open_Things_Are_Not_Listed()
    {
        var store = new PickStore([Pick.ForProgram("Notepad", PageIds.Apps, "notepad.exe", null)]);
        var open = Fixtures.Open(windows:
        [
            Fixtures.Window(1, "notepad.exe"),
            Fixtures.Window(2, "chrome.exe"),   // open, not picked
            Fixtures.Window(3, "chrome.exe"),
            Fixtures.Window(4, "mspaint.exe"),
        ]);

        var rows = PickList.RowsFor(PageIds.Apps, store, open);

        var row = Assert.Single(rows);
        Assert.Equal("notepad.exe", row.Pick.ExeName);
        Assert.Equal(1, row.Status.Count);
        Assert.Empty(PickList.RowsFor(PageIds.Browser, store, open)); // chrome is open, but nobody picked it
    }

    [Fact]
    public void Picks_Round_Trip()
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");
        var picks = new PickStore(
        [
            Pick.ForProgram("Spotify", PageIds.Media, "Spotify.exe", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0"),
            Pick.ForSite("YouTube Music", "music.youtube.com", PageIds.Media),
            Pick.ForFolder("Downloads", PageIds.Folders),
        ]);

        Assert.True(picks.Save(path));
        var load = PickStore.Load(path);

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(picks.Picks, load.Store.Picks);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "picks": 5 }""")]
    [InlineData("""{ "picks": [ { "id": "x", "kind": "program", "name": "X", "page": "apps", "exe": "C:\\Tools\\x.exe" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "x", "kind": "banana", "name": "X", "page": "apps" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "x", "kind": "folder", "name": "X", "page": "folders", "folder": "Secrets" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "s", "kind": "site", "name": "S", "page": "media", "host": "https://youtube.com/x" } ] }""")]
    public void Broken_File_Is_Left_Untouched_And_Gives_An_Empty_List(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");
        File.WriteAllText(path, content);
        var before = File.GetLastWriteTimeUtc(path);

        var load = PickStore.OpenOrStart(path, Fixtures.EverythingInstalled, selfTest: false);

        Assert.Equal(PickStoreStatus.Unreadable, load.Status);
        Assert.Empty(load.Store.Picks);
        Assert.False(string.IsNullOrEmpty(load.Detail));
        Assert.Equal(content, File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }
}

public class PickStoreTests
{
    [Fact]
    public void Saved_File_Never_Contains_A_Path()
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");
        var picks = new PickStore(StarterPicks.Build(Fixtures.EverythingInstalled));
        Assert.True(picks.Save(path));

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("\\\\", text);                      // no backslash survives JSON escaping
        Assert.DoesNotContain(":/", text);
        Assert.DoesNotMatch(@"[A-Za-z]:[\\/]", text);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text, StringComparison.OrdinalIgnoreCase);

        // A pick that points at a path cannot be saved at all.
        var bad = new PickStore([new Pick("program:x", PickKind.Program, "X", PageIds.Apps, ExeName: "C:\\Tools\\x.exe")]);
        var badPath = dir.File("bad.json");
        Assert.False(bad.Save(badPath));
        Assert.False(File.Exists(badPath));
    }

    [Theory]
    [InlineData("x.exe", true)]
    [InlineData("C:\\x.exe", false)]
    [InlineData("tools/x.exe", false)]
    [InlineData("x", false)]
    public void A_Program_Pick_Holds_A_File_Name_Only(string exe, bool storable)
    {
        var pick = new Pick("program:x", PickKind.Program, "X", PageIds.Apps, ExeName: exe);
        Assert.Equal(storable, pick.IsStorable(out _));
    }
}

public class StarterPicksTests
{
    [Fact]
    public void First_Run_Has_The_Starter_List()
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");

        var load = PickStore.OpenOrStart(path, Fixtures.EverythingInstalled, selfTest: false);

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.True(File.Exists(path));
        string[] Names(string page) => [.. load.Store.ForPage(page).Select(p => p.Name)];
        Assert.Equal(["Spotify", "YouTube", "YouTube Music", "Twitch", "SoundCloud"], Names(PageIds.Media));
        Assert.Equal(["Claude", "Antigravity", "Windows Terminal", "Visual Studio Code", "Cursor"], Names(PageIds.Vibe));
        Assert.Equal(["Downloads", "Documents", "Desktop"], Names(PageIds.Folders));
        Assert.Equal(["Discord", "Telegram", "Premiere Pro", "After Effects", "OBS Studio", "Notepad"], Names(PageIds.Apps));
        Assert.Equal(["Chrome", "Edge", "Firefox", "Brave"], Names(PageIds.Browser));
        Assert.All(load.Store.Picks, p => Assert.True(p.IsStorable(out _)));
    }

    [Fact]
    public void Programs_Not_Installed_Are_Left_Out()
    {
        IReadOnlyList<InstalledProgram> installed = [Fixtures.Program("Notepad", "notepad.exe"), Fixtures.Program("Google Chrome", "chrome.exe")];

        var picks = StarterPicks.Build(installed);

        Assert.DoesNotContain(picks, p => p.Name == "Discord");
        Assert.Contains(picks, p => p.Name == "Notepad" && p.ExeName == "notepad.exe");
        Assert.Contains(picks, p => p.Name == "Chrome" && p.ExeName == "chrome.exe");
        Assert.Equal(2, StarterPicks.CountFound(installed));
        // Sites and the three folders do not depend on what is installed.
        Assert.Equal(4, picks.Count(p => p.Kind == PickKind.Site));
        Assert.Equal(3, picks.Count(p => p.Kind == PickKind.Folder));
    }

    [Fact]
    public void A_Program_Is_Found_Under_Whichever_Candidate_Name_Exists()
    {
        // The terminal's launcher shim is only a candidate; the package family and the display name are fallbacks.
        Assert.Equal("wt.exe", StarterPicks.Programs.First(s => s.Name == "Windows Terminal") is var t
            ? StarterPicks.Find(t, [Fixtures.Program("x", "wt.exe")])!.ExeName : null);
        var byPackage = StarterPicks.Find(StarterPicks.Programs.First(s => s.Name == "Notepad"),
            [new InstalledProgram("Anything", null, "Microsoft.WindowsNotepad_8wekyb3d8bbwe", "app-id")]);
        Assert.NotNull(byPackage);
        var pick = Pick.ForProgram("Notepad", PageIds.Apps, byPackage!.ExeName, byPackage.PackageFamily);
        Assert.True(pick.IsStorable(out var why), why);
    }

    [Fact]
    public void Not_Applied_Under_Selftest()
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");

        var load = PickStore.OpenOrStart(path, Fixtures.EverythingInstalled, selfTest: true);

        Assert.Equal(PickStoreStatus.Missing, load.Status);
        Assert.Empty(load.Store.Picks);
        Assert.False(File.Exists(path), "The self-test never writes Dan's picks file.");
    }

    [Fact]
    public void Starter_List_Is_Not_Reapplied_After_Dan_Changes_It()
    {
        using var dir = new TempDir();
        var path = dir.File("picks.json");
        var first = PickStore.OpenOrStart(path, Fixtures.EverythingInstalled, selfTest: false);

        // Dan removes everything but one pick; the file stays, so the starters do not come back.
        var mine = new PickStore([first.Store.Picks.First(p => p.Name == "Notepad")]);
        Assert.True(mine.Save(path));
        var again = PickStore.OpenOrStart(path, Fixtures.EverythingInstalled, selfTest: false);

        Assert.Single(again.Store.Picks);
        Assert.Equal("Notepad", again.Store.Picks[0].Name);

        // Even an empty list is Dan's choice and stays empty.
        Assert.True(PickStore.Empty.Save(path));
        Assert.Empty(PickStore.OpenOrStart(path, Fixtures.EverythingInstalled, selfTest: false).Store.Picks);
    }
}

public class PickStateTests
{
    [Fact]
    public void Closed_Pick_Is_Listed_As_Closed_And_Opens_On_Click()
    {
        var spotify = Pick.ForProgram("Spotify", PageIds.Media, "Spotify.exe", null);
        var downloads = Pick.ForFolder("Downloads", PageIds.Folders);
        var open = Fixtures.Open(windows: [Fixtures.Window(7, "chrome.exe")]);
        var cycler = new ClickCycler();

        var status = PickStates.For(spotify, open);

        Assert.True(status.Known);
        Assert.False(status.IsOpen);
        Assert.Equal(0, status.Count);
        Assert.Equal(new ClickPlan(ClickKind.Start), PickStates.Plan(spotify, status, cycler));
        Assert.Equal(new ClickPlan(ClickKind.OpenFolder), PickStates.Plan(downloads, PickStates.For(downloads, open), cycler));

        // Once it is open, the same click brings it forward instead.
        var nowOpen = Fixtures.Open(windows: [Fixtures.Window(9, "spotify.exe")]);
        var plan = PickStates.Plan(spotify, PickStates.For(spotify, nowOpen), cycler);
        Assert.Equal(new ClickPlan(ClickKind.BringForward, 9), plan);
    }

    [Fact]
    public void A_Program_Is_Matched_By_Package_Family_As_Well()
    {
        var pick = Pick.ForProgram("Claude", PageIds.Vibe, null, "Claude_pzs8sxrjxfjjc");
        var open = Fixtures.Open(windows: [new OpenWindow(5, "ApplicationFrameHost.exe", "Claude_pzs8sxrjxfjjc", "t", 0)]);
        Assert.True(PickStates.For(pick, open).IsOpen);
    }

    [Fact]
    public void A_Site_Pick_Without_The_Addon_Is_Unknown_And_Opens_The_Site()
    {
        var youtube = Pick.ForSite("YouTube", "youtube.com", PageIds.Media);
        var status = PickStates.For(youtube, Fixtures.Open(tabs: [Fixtures.Tab(1, "youtube.com", 1)], connected: false));
        Assert.False(status.Known);
        Assert.Equal(new ClickPlan(ClickKind.OpenSite), PickStates.Plan(youtube, status, new ClickCycler()));
    }
}

public class SiteMatchTests
{
    [Fact]
    public void Tabs_Of_One_Site_Share_One_Pick_With_A_Count()
    {
        var youtube = Pick.ForSite("YouTube", "youtube.com", PageIds.Media);
        var open = Fixtures.Open(connected: true, tabs:
        [
            Fixtures.Tab(1, "youtube.com", 10),
            Fixtures.Tab(2, "www.youtube.com", 12),
            Fixtures.Tab(3, "youtube.com", 11),
            Fixtures.Tab(4, "music.youtube.com", 13),
            Fixtures.Tab(5, "example.org", 14),
        ]);

        var status = PickStates.For(youtube, open);

        Assert.True(status.IsOpen);
        Assert.Equal(3, status.Count);
        Assert.Equal([12L, 11L, 10L], status.Targets); // newest first
    }

    [Fact]
    public void Several_Windows_Of_One_Program_Are_One_Pick_With_A_Count()
    {
        var pick = Pick.ForProgram("Discord", PageIds.Apps, "Discord.exe", null);
        var open = Fixtures.Open(windows: [Fixtures.Window(1, "discord.exe", 2), Fixtures.Window(2, "Discord.exe", 0), Fixtures.Window(3, "other.exe", 1)]);

        var status = PickStates.For(pick, open);

        Assert.Equal(2, status.Count);
        Assert.Equal([2L, 1L], status.Targets); // top-most first
    }

    [Fact]
    public void Repeated_Click_Cycles_Newest_First()
    {
        var cycler = new ClickCycler();
        long[] targets = [30, 20, 10];

        var clicks = Enumerable.Range(0, 5).Select(_ => cycler.Next("site:youtube.com", targets)).ToArray();

        Assert.Equal([30L, 20L, 10L, 30L, 20L], clicks);

        // A window that went away is skipped; with nothing left the cycle forgets itself.
        Assert.Equal(10L, cycler.Next("site:youtube.com", [10]));
        Assert.Null(cycler.Next("site:youtube.com", []));
        Assert.Equal(30L, cycler.Next("site:youtube.com", targets));
    }

    [Fact]
    public void Music_Youtube_Is_Not_Youtube()
    {
        Assert.False(SiteMatch.Matches("youtube.com", "music.youtube.com"));
        Assert.False(SiteMatch.Matches("music.youtube.com", "youtube.com"));
        Assert.True(SiteMatch.Matches("youtube.com", "www.YouTube.com"));
        Assert.Equal("music.youtube.com", SiteMatch.HostOf("https://music.youtube.com/watch?v=abc"));
        Assert.Equal("youtube.com", SiteMatch.HostOf("https://www.youtube.com/watch?v=abc"));
        Assert.Null(SiteMatch.HostOf(""));
    }
}

public class FolderStateTests
{
    [Fact]
    public void Tabs_Of_One_Explorer_Frame_Count_Separately_But_Are_One_Click_Target()
    {
        var downloads = Pick.ForFolder("Downloads", PageIds.Folders);
        var open = Fixtures.Open(folders: [new FolderWindow(10, "Downloads", "x", 0), new FolderWindow(10, "Downloads", "x", 0), new FolderWindow(11, "Downloads", "x", 1), new FolderWindow(12, "Documents", "x", 2)]);

        var status = PickStates.For(downloads, open);

        Assert.Equal(3, status.Count);
        Assert.Equal([10L, 11L], status.Targets);
        var cycler = new ClickCycler();
        Assert.Equal([10L, 11L, 10L], Enumerable.Range(0, 3).Select(_ => PickStates.Plan(downloads, status, cycler).Target).ToArray());
    }
}
