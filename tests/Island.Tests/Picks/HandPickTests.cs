using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-10 section 3: adding by hand, the logic only. Every path here is invented, under the drive Q: (or the invented profile folder).</summary>
public class HandPickTests
{
    private const string Profile = @"Q:\Invented\Profile";

    private static HandContext Context(Func<ulong>? random = null) => new(
        Profile,
        [("Downloads", Profile + @"\Downloads"), ("Documents", Profile + @"\Documents"), ("Downloads", "::{INVENTED-GUID}")],
        random ?? Counter());

    private static long _next = 0x1000;

    // One counter for the whole run, as a real random source would never give the same id twice.
    private static Func<ulong> Counter() => () => (ulong)Interlocked.Increment(ref _next);

    private static string V1(string name) => RepoPaths.File("tests", "Island.Tests", "Fixtures", "v1", name);

    private static string V2(string name) => RepoPaths.File("tests", "Island.Tests", "Fixtures", "v2", name);

    private static string Lines(string text) => text.Replace("\r\n", "\n");

    private static Pick Made(HandPickResult result)
    {
        Assert.True(result.Ok, result.Message);
        Assert.Null(result.Message);
        return result.Pick!;
    }

    // ---- What a pick made by hand holds ------------------------------------

    [Fact]
    public void A_Program_From_The_List_Stores_No_Path()
    {
        var listed = new InstalledProgram("Alpha", "alpha.exe", null, @"Q:\Invented\Alpha\alpha.exe"); // the launch target is a path in memory, never stored

        var pick = Made(HandPicks.FromInstalledProgram(listed, PageIds.Apps));

        Assert.Null(pick.Location);
        Assert.Equal("program:alpha", pick.Id);
        var json = new PickStore([pick]).ToJson();
        Assert.DoesNotContain("location", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Invented", json, StringComparison.Ordinal);
        Assert.Equal(pick, Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)); // the very pick the + list would make
    }

    [Fact]
    public void A_Browsed_Program_Stores_Its_Path_In_The_One_Field()
    {
        var program = Made(HandPicks.BrowsedProgram(@"q:\invented\Alpha\alpha.exe", PageIds.Apps, Context()));

        Assert.Equal(@"Q:\invented\Alpha\alpha.exe", program.Location);
        Assert.Equal("alpha.exe", program.ExeName);       // a file name only: open windows are still found by it
        Assert.Equal("Alpha", program.Name);
        Assert.Null(program.PackageFamily);
        Assert.Null(program.KnownFolder);
        Assert.Null(program.Host);

        // A shortcut is stored as the shortcut's own place; nothing is read from it, so the program's file name is not known.
        var shortcut = Made(HandPicks.BrowsedProgram(@"Q:\Invented\Alpha\Alpha tool.lnk", PageIds.Apps, Context()));
        Assert.Equal(@"Q:\Invented\Alpha\Alpha tool.lnk", shortcut.Location);
        Assert.Null(shortcut.ExeName);
        Assert.Equal("Alpha tool", shortcut.Name);
        Assert.Equal("program", PickItems.Subtitle(shortcut, PickStates.For(shortcut, OpenSnapshot.Empty)));
        Assert.False(PickItems.For(new PickRow(shortcut, PickStates.For(shortcut, OpenSnapshot.Empty)), null).IsClosed);
        // When the app has read the target it passes its file name, and the pick is open or closed like any other.
        var read = Made(HandPicks.BrowsedProgram(@"Q:\Invented\Alpha\Alpha tool.lnk", PageIds.Apps, Context(), "alpha.exe"));
        Assert.Equal("alpha.exe", read.ExeName);

        // In the file the path is in "location" and nowhere else.
        var json = new PickStore([program]).ToJson();
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(json, "Invented", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
        Assert.Contains("\"location\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Known_Folder_Stores_No_Path_And_Any_Other_Does()
    {
        var known = Made(HandPicks.Folder(@"q:\INVENTED\profile\downloads\", PageIds.Folders, Context()));
        Assert.Equal(PickKind.Folder, known.Kind);
        Assert.Equal("Downloads", known.KnownFolder);
        Assert.Null(known.Location);
        Assert.Equal(Pick.ForFolder("Downloads", PageIds.Folders), known);

        var other = Made(HandPicks.Folder(@"Q:\Invented\Beta", PageIds.Folders, Context()));
        Assert.Null(other.KnownFolder);
        Assert.Equal(@"Q:\Invented\Beta", other.Location);
        Assert.Equal("Beta", other.Name);

        // A folder inside a known folder is not that known folder; under the profile it is stored with the folder unexpanded.
        var inside = Made(HandPicks.Folder(Profile + @"\Downloads\Sub", PageIds.Folders, Context()));
        Assert.Null(inside.KnownFolder);
        Assert.Equal(@"%USERPROFILE%\Downloads\Sub", inside.Location);

        var drive = Made(HandPicks.Folder(@"q:\", PageIds.Folders, Context()));
        Assert.Equal(@"Q:\", drive.Location);
        Assert.Equal("Drive Q", drive.Name);
    }

    [Theory]
    [InlineData("example.org", "example.org")]
    [InlineData("https://example.org", "example.org")]
    [InlineData("HTTPS://WWW.Example.ORG/Some/Page?x=1#top", "example.org")]
    [InlineData("  example.org  ", "example.org")]
    [InlineData("example.org.", "example.org")]
    [InlineData("example.org...", "example.org")]
    [InlineData("example.org:8080/page", "example.org")]
    [InlineData("https://example.org:443", "example.org")]
    [InlineData("https://someone:secret@example.org/", "example.org")]
    [InlineData("someone@example.org", "example.org")]
    [InlineData("//example.org/page", "example.org")]
    [InlineData("example.org\\page", "example.org")]
    [InlineData("example.org/a page with spaces", "example.org")]
    [InlineData("music.example.org/x", "music.example.org")]
    [InlineData("www.example.org", "example.org")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void A_Typed_Address_Becomes_Its_Host(string typed, string host)
    {
        var understood = TypedSite.Understand(typed);
        Assert.True(understood.Ok);
        Assert.Equal(host, understood.Host);
        Assert.Null(understood.Refusal);

        var pick = Made(HandPicks.Site(typed, PageIds.Browser));
        Assert.Equal(PickKind.Site, pick.Kind);
        Assert.Equal(host, pick.Host);
        Assert.Equal("site:" + host, pick.Id);
        Assert.Null(pick.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("localhost")]
    [InlineData("example")]
    [InlineData("example.org some words")]
    [InlineData("exam ple.org")]
    [InlineData("192.168.0.1")]
    [InlineData("https://10.0.0.1:8080/x")]
    [InlineData("[::1]")]
    [InlineData("http://[2001:db8::1]/")]
    [InlineData("ftp://example.org")]
    [InlineData("javascript://example.org")]
    [InlineData("https://")]
    [InlineData("/only/a/page")]
    [InlineData("user@")]
    [InlineData(".example.org")]
    [InlineData("a..b")]
    [InlineData("-bad.example")]
    [InlineData("bad-.example")]
    [InlineData("under_score.example")]
    [InlineData("example.org:notaport")]
    [InlineData("example.org:99999")]
    [InlineData("a:b:c.example")]
    [InlineData("exam\tple.org")]
    [InlineData("%%%.%%%")]
    public void Something_That_Is_Not_A_Site_Is_Refused_With_A_Reason(string? typed)
    {
        var understood = TypedSite.Understand(typed);
        Assert.False(understood.Ok);
        Assert.Null(understood.Host);
        Assert.NotNull(understood.Refusal);
        Assert.Equal("NOT_A_SITE", understood.Refusal.Code);

        var result = HandPicks.Site(typed, PageIds.Browser);
        Assert.False(result.Ok);
        Assert.Equal("NOT_A_SITE", result.Code);
        Assert.Equal(
            "That does not look like the name of a website, so nothing was added. Island keeps only a site's name, such as example.org. Type the site's name and try again.",
            result.Message);
    }

    [Fact]
    public void A_Text_A_Megabyte_Long_Is_Refused_Not_Crashed_On()
    {
        var huge = "a." + new string('b', 1_000_000) + ".org";
        Assert.False(TypedSite.Understand(huge).Ok);
        Assert.False(HandPicks.File(@"Q:\" + new string('a', 1_000_000), PageIds.Folders, Context()).Ok);
        Assert.False(HandPicks.Folder(new string('\\', 1_000_000), PageIds.Folders, Context()).Ok);
        Assert.False(PickPath.IsAcceptable(new string('a', 1_000_000), out _));
        Assert.False(PickPath.IsAcceptable(@"Q:\" + string.Join('\\', Enumerable.Repeat("a", 200_000)), out _));
    }

    // ---- A file, and what a missing target does ----------------------------

    [Fact]
    public void A_File_Pick_Is_Always_Bright_And_Opens_On_Click()
    {
        var file = Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, Context()));
        Assert.Equal(PickKind.File, file.Kind);
        Assert.Equal("Delta.txt", file.Name);

        var status = PickStates.For(file, Fixtures.Open(windows: [Fixtures.Window(1, "delta.exe")], folders: [new FolderWindow(2, null, @"Q:\Invented\Delta", 0)]));
        var item = PickItems.For(new PickRow(file, status), null);

        Assert.False(item.IsClosed);                   // never grey
        Assert.Equal("file", item.Subtitle);           // the second text line
        Assert.Equal(0, item.Count);
        Assert.False(status.IsOpen);
        var plan = PickStates.Plan(file, status, new ClickCycler());
        Assert.Equal(ClickKind.OpenFile, plan.Kind);   // a click asks Windows to open it
        Assert.Equal(ClickKind.OpenFile, PickStates.Plan(file, status, new ClickCycler()).Kind); // every click, not only the first
    }

    [Fact]
    public void A_Missing_Target_Is_Grey_And_Says_So()
    {
        var file = Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, Context()));
        var folder = Made(HandPicks.Folder(@"Q:\Invented\Beta", PageIds.Folders, Context()));
        var store = new PickStore([file, folder]);
        var cache = new PickTargetCache();
        var context = new PickContext(Profile, cache);

        // Not asked yet: it draws as present, never grey on a guess.
        var before = PickList.RowsFor(PageIds.Folders, store, OpenSnapshot.Empty, context);
        Assert.False(PickItems.For(before[0], null).IsClosed);
        Assert.Equal("closed", PickItems.For(before[1], null).Subtitle);
        Assert.Equal("file", PickItems.For(before[0], null).Subtitle);

        // The app's worker asks; the file is gone, the folder is there.
        var probe = new FakeProbe(kind => kind == PickKind.File ? false : true);
        Assert.True(cache.Refresh(probe, store.Picks, Profile));
        var rows = PickList.RowsFor(PageIds.Folders, store, OpenSnapshot.Empty, context);

        var missing = PickItems.For(rows[0], null);
        Assert.True(missing.IsClosed);                                   // grey: the island greys what is marked closed
        Assert.Equal("not found", missing.Subtitle);
        Assert.Equal("closed", PickItems.For(rows[1], null).Subtitle);   // the folder is there but not open
        var plan = PickStates.Plan(file, rows[0].Status, new ClickCycler());
        Assert.Equal(ClickKind.TargetMissing, plan.Kind);
        var refusal = HandPickRefusals.ForMissing(file);
        Assert.Equal("PICK_TARGET_MISSING", refusal.Code);
        Assert.Equal(
            "Delta.txt is not where it was, so Island could not open it. It may have been moved, renamed or deleted, and guessing another place could open the wrong thing. Remove it from the island and add it again from where it is now.",
            refusal.Message);
        Assert.Equal(2, store.Picks.Count);                              // nothing is removed by itself

        // The probe was handed real paths, in memory, and only for things that have a place.
        Assert.Equal([@"Q:\Invented\Delta\Delta.txt", @"Q:\Invented\Beta"], probe.Asked.Select(a => a.Path));
        Assert.Equal(2, probe.Asked.Count);

        // A missing program that is running is not missing: it is plainly there.
        var program = Made(HandPicks.BrowsedProgram(@"Q:\Invented\Alpha\alpha.exe", PageIds.Apps, Context()));
        cache.Record(program, Profile, exists: false);
        var running = PickStates.For(program, Fixtures.Open(windows: [Fixtures.Window(7, "alpha.exe")]), context);
        Assert.False(running.Missing);
        Assert.True(running.IsOpen);
        var stopped = PickStates.For(program, OpenSnapshot.Empty, context);
        Assert.True(stopped.Missing);
        Assert.Equal(ClickKind.TargetMissing, PickStates.Plan(program, stopped, new ClickCycler()).Kind);
    }

    [Fact]
    public void A_Hand_Added_Folder_Is_Open_When_An_Explorer_Window_Shows_Its_Place()
    {
        var folder = Made(HandPicks.Folder(Profile + @"\Invented\Gamma", PageIds.Folders, Context()));
        Assert.Equal(@"%USERPROFILE%\Invented\Gamma", folder.Location);
        var context = new PickContext(Profile);

        var windows = Fixtures.Open(folders:
        [
            new FolderWindow(10, null, @"q:\invented\PROFILE\invented\gamma\", 0),   // the same place, spelled differently
            new FolderWindow(11, null, Profile + @"\Invented\Gamma\Inner", 1),      // inside it: not it
            new FolderWindow(12, "Downloads", Profile + @"\Downloads", 2),
        ]);

        var open = PickStates.For(folder, windows, context);
        Assert.True(open.IsOpen);
        Assert.Equal([10L], open.Targets);
        Assert.Equal(ClickKind.BringForward, PickStates.Plan(folder, open, new ClickCycler()).Kind);
        Assert.Equal("open", PickItems.Subtitle(folder, open));

        var closed = PickStates.For(folder, Fixtures.Open(folders: [new FolderWindow(11, null, Profile + @"\Invented\Gamma\Inner", 0)]), context);
        Assert.False(closed.IsOpen);
        Assert.Equal(ClickKind.OpenFolderAt, PickStates.Plan(folder, closed, new ClickCycler()).Kind);
        Assert.Equal(ClickKind.OpenFolder, PickStates.Plan(Pick.ForFolder("Downloads", PageIds.Folders), PickStatus.Closed, new ClickCycler()).Kind);

        // Without the profile folder the stored place cannot be put back, so it is simply closed (never a crash).
        Assert.False(PickStates.For(folder, windows).IsOpen);
    }

    // ---- The same thing twice ----------------------------------------------

    [Fact]
    public void The_Same_Thing_Cannot_Be_Added_Twice()
    {
        var store = PickStore.Empty;
        store = store.Add(Made(HandPicks.Folder(@"Q:\Invented\Beta", PageIds.Folders, Context())), out var folder);
        store = store.Add(Made(HandPicks.Folder(@"q:/invented/beta/", PageIds.Apps, Context())), out var folderAgain);
        store = store.Add(Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, Context())), out var file);
        store = store.Add(Made(HandPicks.File(@"Q:\INVENTED\DELTA\delta.TXT", PageIds.Apps, Context())), out var fileAgain);
        store = store.Add(Made(HandPicks.Site("example.org", PageIds.Browser)), out var site);
        store = store.Add(Made(HandPicks.Site("https://WWW.example.org/page", PageIds.Media)), out var siteAgain);
        store = store.Add(Made(HandPicks.FromInstalledProgram(new InstalledProgram("Alpha", "alpha.exe", null, "x"), PageIds.Apps)), out var program);
        store = store.Add(Made(HandPicks.FromInstalledProgram(new InstalledProgram("Alpha", "ALPHA.EXE", null, "x"), PageIds.Vibe)), out var programAgain);
        store = store.Add(Made(HandPicks.Folder(Profile + @"\Downloads", PageIds.Folders, Context())), out var known);
        store = store.Add(Pick.ForFolder("Downloads", PageIds.Apps), out var knownAgain);

        Assert.Equal([true, false, true, false, true, false, true, false, true, false], [folder, folderAgain, file, fileAgain, site, siteAgain, program, programAgain, known, knownAgain]);
        Assert.Equal(5, store.Picks.Count);

        // "It is already on page X": the store names the pick that is there, and its page.
        var wanted = Made(HandPicks.Folder(@"Q:\Invented\BETA\", PageIds.Vibe, Context()));
        Assert.Equal(PageIds.Folders, store.Find(wanted)!.PageId);
        Assert.Null(store.Find(Made(HandPicks.Folder(@"Q:\Invented\Other", PageIds.Vibe, Context()))));
    }

    [Fact]
    public void The_Same_Folder_Under_Two_Spellings_Is_One_Thing()
    {
        var spellings = new[]
        {
            @"Q:\Invented\Beta", @"Q:\Invented\Beta\", @"q:\invented\beta", @"Q:/Invented/Beta", @"Q:\Invented\\Beta", @"Q:\Invented\.\Beta",
            @"Q:\Invented\Other\..\Beta", @"Q:\Invented\Beta.", @"Q:\Invented\Beta ", @"  Q:\Invented\Beta  ", @"Q:\INVENTED\BETA\",
        };

        var picks = spellings.Select(s => Made(HandPicks.Folder(s, PageIds.Folders, Context()))).ToList();

        Assert.All(picks, p => Assert.True(picks[0].IsSameThing(p)));
        Assert.Single(picks.Select(p => PickPath.Key(p.Location)).Distinct());
        Assert.Single(picks.Aggregate(PickStore.Empty, (store, p) => store.Add(p, out _)).Picks);
        // ...while a neighbour is not it.
        Assert.False(picks[0].IsSameThing(Made(HandPicks.Folder(@"Q:\Invented\Beta2", PageIds.Folders, Context()))));
        Assert.False(picks[0].IsSameThing(Made(HandPicks.Folder(@"Q:\Invented\Beta\Inner", PageIds.Folders, Context()))));
        // The profile folder in its two spellings (stored with the token, or spelled out and compressed) is one place too.
        var viaToken = Pick.ForFolder("Downloads", PageIds.Folders) with { KnownFolder = null, Id = "folder:1", Location = @"%USERPROFILE%\Invented\Gamma" };
        var viaReal = Made(HandPicks.Folder(Profile.ToUpperInvariant() + @"\invented\GAMMA", PageIds.Folders, Context()));
        Assert.True(viaToken.IsSameThing(viaReal));
    }

    // ---- Storing and loading -----------------------------------------------

    [Fact]
    public void Old_Picks_Files_Still_Load()
    {
        var before = File.ReadAllBytes(V1("picks.json"));
        var load = PickStore.Load(V1("picks.json"));

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(["program:alpha", "folder:downloads", "site:alpha.example"], load.Store.Picks.Select(p => p.Id));
        Assert.All(load.Store.Picks, p => Assert.Null(p.Location));

        // Reading changes nothing; the old fixtures are exactly as they were (no new field, still the old "version").
        Assert.Equal(before, File.ReadAllBytes(V1("picks.json")));
        var text = File.ReadAllText(V1("picks.json"));
        Assert.Contains("\"version\": 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("location", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"file\"", text, StringComparison.Ordinal);

        // A copy round-trips: nothing is lost, the picks of the old shape are written with the old number, and the same text comes back every time.
        using var dir = new TempDir();
        File.Copy(V1("picks.json"), dir.File("picks.json"));
        var again = PickStore.Load(dir.File("picks.json")).Store;
        Assert.Equal(load.Store.Picks, again.Picks);
        Assert.True(again.Save(dir.File("saved.json")));
        Assert.Equal(load.Store.Picks, PickStore.Load(dir.File("saved.json")).Store.Picks);
        using var written = System.Text.Json.JsonDocument.Parse(again.ToJson());
        Assert.Equal(FileSchema.Current, written.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(again.ToJson(), PickStore.Parse(again.ToJson()).Store.ToJson());
        Assert.Equal(again.ToJson(), load.Store.ToJson());
    }

    [Fact]
    public void New_Shape_Round_Trips()
    {
        var load = PickStore.Load(V2("picks.json"));
        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(
            ["program:alpha", "program:00000000000000a1", "folder:downloads", "folder:00000000000000b2", "site:alpha.example", "file:00000000000000c3"],
            load.Store.Picks.Select(p => p.Id));
        Assert.Equal(
            [PickKind.Program, PickKind.Program, PickKind.Folder, PickKind.Folder, PickKind.Site, PickKind.File],
            load.Store.Picks.Select(p => p.Kind));
        Assert.Equal([null, @"Q:\Invented\Beta\beta.exe", null, @"%USERPROFILE%\Invented\Gamma", null, @"Q:\Invented\Delta\Delta.txt"], load.Store.Picks.Select(p => p.Location));

        // Byte for byte (but for the line ending the platform writes), through memory and through a file.
        var fixture = File.ReadAllText(V2("picks.json"));
        Assert.Equal(Lines(fixture), Lines(load.Store.ToJson()));
        using var dir = new TempDir();
        Assert.True(load.Store.Save(dir.File("picks.json")));
        Assert.Equal(Lines(fixture), Lines(File.ReadAllText(dir.File("picks.json"))));
        Assert.Equal(load.Store.Picks, PickStore.Load(dir.File("picks.json")).Store.Picks);

        // The number went up by one, because the shape changed.
        using var doc = System.Text.Json.JsonDocument.Parse(load.Store.ToJson());
        Assert.Equal(PickStore.NewShapeSchema, doc.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(2, PickStore.NewShapeSchema); // one more than the number every file carried before this shape

        // The old folder is untouched: its picks file still has the old number and none of the new fields.
        Assert.DoesNotContain("location", File.ReadAllText(V1("picks.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("schema", File.ReadAllText(V1("picks.json")), StringComparison.Ordinal);
        Assert.Equal(new[] { "picks.json", "pages.json", "scenes.json", "settings.json" }.Order(), Directory.GetFiles(RepoPaths.File("tests", "Island.Tests", "Fixtures", "v1")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void A_Newer_Picks_File_Is_Still_Left_Alone()
    {
        using var dir = new TempDir();
        var text = $"{{\"schema\":{PickStore.NewShapeSchema + 1},\"picks\":[],\"future\":1}}";
        File.WriteAllText(dir.File("picks.json"), text);

        var load = PickStore.Load(dir.File("picks.json"));

        Assert.Equal(PickStoreStatus.Unreadable, load.Status);
        Assert.True(FileSchema.IsNewer(load.Detail));
        Assert.Equal(text, File.ReadAllText(dir.File("picks.json")));
    }

    [Theory]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "https://example.org/x" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "\\\\server\\share\\x" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "\\\\?\\UNC\\server\\share\\x" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "relative\\x.txt" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "Q:x.txt" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "Q:\\x.txt", "exe": "x.exe" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "site:a.example", "kind": "site", "name": "X", "page": "browser", "host": "a.example", "location": "Q:\\x" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "folder:1", "kind": "folder", "name": "X", "page": "folders", "folder": "Downloads", "location": "Q:\\x" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "folder:1", "kind": "folder", "name": "X", "page": "folders" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "program:1", "kind": "program", "name": "X", "page": "apps", "exe": "Q:\\x\\x.exe" } ] }""")]
    [InlineData("""{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "Q:\\a\\..\\..\\x.txt" } ] }""")]
    public void A_Place_In_The_Wrong_Shape_Makes_The_File_Unreadable_And_It_Is_Left_Untouched(string content)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("picks.json"), content);

        var load = PickStore.Load(dir.File("picks.json"));

        Assert.Equal(PickStoreStatus.Unreadable, load.Status);
        Assert.Empty(load.Store.Picks);
        Assert.Equal(content, File.ReadAllText(dir.File("picks.json")));
        Assert.DoesNotContain("Q:", load.Detail ?? string.Empty, StringComparison.Ordinal); // the reason never names the path
    }

    [Fact]
    public void A_Hand_Edited_Spelling_Of_A_Place_Is_Brought_To_The_One_Form()
    {
        var json = """{ "picks": [ { "id": "file:1", "kind": "file", "name": "X", "page": "folders", "location": "q:/Invented//Delta/x.txt" },""" +
                   """ { "id": "file:2", "kind": "file", "name": "Y", "page": "folders", "location": "%userprofile%/Invented/y.txt" } ] }""";

        var load = PickStore.Parse(json);

        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal([@"Q:\Invented\Delta\x.txt", @"%USERPROFILE%\Invented\y.txt"], load.Store.Picks.Select(p => p.Location));
    }

    [Fact]
    public void Saving_Refuses_A_Place_That_Still_Spells_Out_The_Profile_Folder()
    {
        using var dir = new TempDir();
        var spelled = new PickStore([new Pick("file:1", PickKind.File, "Notes.txt", PageIds.Folders, Location: Profile + @"\Invented\Notes.txt")]);
        var compressed = new PickStore([Made(HandPicks.File(Profile + @"\Invented\Notes.txt", PageIds.Folders, Context()))]);

        Assert.False(spelled.Save(dir.File("a.json"), Profile));
        Assert.False(File.Exists(dir.File("a.json")));
        Assert.True(compressed.Save(dir.File("b.json"), Profile));
        var text = File.ReadAllText(dir.File("b.json"));
        Assert.Contains(@"%USERPROFILE%", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Profile", text.Replace("%USERPROFILE%", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        // A place that is not under the profile folder is fine.
        Assert.True(new PickStore([Made(HandPicks.File(@"Q:\Invented\Notes.txt", PageIds.Folders, Context()))]).Save(dir.File("c.json"), Profile));
    }

    [Fact]
    public void A_Scene_Keeps_A_Hand_Made_Thing_Through_The_Same_Reader()
    {
        // The scenes file refers to picks as copies written and read by PickStore itself, so the new field and kind need no code of their own there.
        var things = new[]
        {
            Scenes.ThingFrom(Made(HandPicks.File(Profile + @"\Invented\Delta.txt", PageIds.Folders, Context())))!,
            Scenes.ThingFrom(Made(HandPicks.Folder(@"Q:\Invented\Beta", PageIds.Folders, Context())))!,
        };
        var store = new SceneStore([new Scene("scene-1", "Evening", things)]);
        using var dir = new TempDir();

        Assert.True(store.Save(dir.File("scenes.json")));
        var text = File.ReadAllText(dir.File("scenes.json"));
        Assert.Contains(@"%USERPROFILE%", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Invented\\\\Profile", text, StringComparison.Ordinal);
        var load = SceneStore.Load(dir.File("scenes.json"));

        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Equal(things, load.Store.Items.Single().Things);
    }

    // ---- The rule about paths ----------------------------------------------

    [Fact]
    public void A_Profile_Path_Is_Stored_With_The_Folder_Unexpanded()
    {
        var file = Made(HandPicks.File(Profile + @"\Invented\Delta\Delta.txt", PageIds.Folders, Context()));
        Assert.Equal(@"%USERPROFILE%\Invented\Delta\Delta.txt", file.Location);

        var json = new PickStore([file]).ToJson();
        Assert.Contains("%USERPROFILE%", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Profile", json.Replace("%USERPROFILE%", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Q:", json, StringComparison.Ordinal);

        // Expanded in memory only, and only with a profile folder to put in its place.
        Assert.Equal(Profile + @"\Invented\Delta\Delta.txt", PickPath.Expand(file.Location, Profile));
        Assert.Equal(Profile + @"\Invented\Delta\Delta.txt", PickPath.Expand(file.Location, Profile.ToLowerInvariant() + @"\"), ignoreCase: true);
        Assert.Null(PickPath.Expand(file.Location, null));
        Assert.Null(PickPath.Expand(file.Location, "not a folder"));

        // The profile folder itself, in any capitals; and the token alone.
        Assert.Equal("%USERPROFILE%", PickPath.Compress(Profile, Profile));
        Assert.Equal("%USERPROFILE%", PickPath.Compress(Profile.ToUpperInvariant() + @"\", Profile));
        Assert.Equal(@"%USERPROFILE%\A", PickPath.Compress(Profile.ToLowerInvariant() + @"/A", Profile));
        Assert.Equal(Profile, PickPath.Expand("%USERPROFILE%", Profile));
        Assert.Equal(13, PickPath.ProfileToken.Length);
        Assert.Equal(PickPath.MaxStoredChars, PickPath.MaxPathChars + PickPath.ProfileToken.Length);

        // A path outside the profile that merely contains its name, or begins with the same letters, is not under it.
        foreach (var outside in new[] { @"Q:\Invented\Profile2\a.txt", @"Q:\Other\Invented\Profile\a.txt", @"Q:\Invented\ProfileX", @"Q:\Invented", @"R:\Invented\Profile\a.txt" })
            Assert.Equal(outside, PickPath.Compress(outside, Profile));
        // A drive's root is never taken for the profile folder.
        Assert.Equal(@"Q:\a", PickPath.Compress(@"Q:\a", @"Q:\"));
        Assert.False(PickPath.SpellsOutProfile(@"Q:\Invented\Profile2\a.txt", Profile));
        Assert.True(PickPath.SpellsOutProfile(Profile + @"\a.txt", Profile));
    }

    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share/file.txt")]
    [InlineData(@"\\?\UNC\server\share\file.txt")]
    [InlineData(@"\\?\Q:\Invented\file.txt")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\pipe\invented")]
    [InlineData(@"\??\Q:\x")]
    [InlineData(@"\rooted\no\drive")]
    [InlineData("https://example.org/file.txt")]
    [InlineData("file:///Q:/Invented/file.txt")]
    [InlineData("ftp://server/file.txt")]
    [InlineData("example.org")]
    [InlineData(@"Q:")]
    [InlineData(@"Q:file.txt")]
    [InlineData(@"relative\file.txt")]
    [InlineData(@"..\file.txt")]
    [InlineData(@"%USERPROFILE%\file.txt")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_Network_Place_Is_Refused(string? path)
    {
        Assert.False(PickPath.IsAcceptable(path, out var reason));
        Assert.NotEmpty(reason);
        Assert.DoesNotContain("server", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Invented", reason, StringComparison.Ordinal);

        foreach (var result in new[] { HandPicks.File(path!, PageIds.Folders, Context()), HandPicks.Folder(path!, PageIds.Folders, Context()), HandPicks.BrowsedProgram(path!, PageIds.Apps, Context()) })
        {
            Assert.False(result.Ok);
            Assert.Null(result.Pick);
            Assert.Equal("PATH_REFUSED", result.Code);
            Assert.NotEmpty(result.Message!);
            Assert.DoesNotContain("server", result.Message!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_Id_Is_Never_Made_From_The_Path()
    {
        var one = Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, Context(() => 0xABCDEF)));
        var two = Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, Context(() => 0x123456)));
        var folder = Made(HandPicks.Folder(@"Q:\Invented\Beta", PageIds.Folders, Context(() => 0xABCDEF)));
        var program = Made(HandPicks.BrowsedProgram(@"Q:\Invented\Alpha\alpha.exe", PageIds.Apps, Context(() => 0xABCDEF)));

        Assert.Equal("file:0000000000abcdef", one.Id);   // its kind, a colon, 16 hex digits
        Assert.Equal("folder:0000000000abcdef", folder.Id);
        Assert.Equal("program:0000000000abcdef", program.Id);
        Assert.NotEqual(one.Id, two.Id);                 // the same path, another id: only the random source decides
        Assert.True(one.IsSameThing(two));               // ...yet they are the same thing, by their paths

        var random = Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, new HandContext(Profile, [])));
        Assert.Matches("^file:[0-9a-f]{16}$", random.Id);
        Assert.NotEqual(random.Id, Made(HandPicks.File(@"Q:\Invented\Delta\Delta.txt", PageIds.Folders, new HandContext(Profile, []))).Id);
        foreach (var kind in Enum.GetValues<PickKind>())
        {
            var id = HandPicks.NewId(kind);
            Assert.Matches("^[a-z]+:[0-9a-f]{16}$", id);
            Assert.True(PickKeysJson.IsUsableId(id)); // what the settings file and the scenes file can hold as a name
        }

        foreach (var pick in new[] { one, folder, program })
        {
            Assert.DoesNotContain("invented", pick.Id, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("delta", pick.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Odd input ---------------------------------------------------------

    [Fact]
    public void A_Path_With_A_Nul_Or_Control_Character_Is_Refused()
    {
        foreach (var path in new[] { "Q:\\Invented\\a\0b.txt", "Q:\\Invented\\a\tb.txt", "Q:\\Invented\\a\nb.txt", "\0", "Q:\\\u0007" })
            Assert.False(PickPath.IsAcceptable(path, out _), path);
        Assert.False(new Pick("file:1", PickKind.File, "X", PageIds.Folders, Location: "Q:\\a\0b").IsStorable(out _));
    }

    [Fact]
    public void The_Length_Limit_Is_Windows_Own_259_And_The_End_Mark()
    {
        var atLimit = @"Q:\" + new string('a', PickPath.MaxPathChars - 3);
        var over = atLimit + "a";

        Assert.Equal(259, atLimit.Length);
        Assert.True(PickPath.IsAcceptable(atLimit, out _));
        Assert.False(PickPath.IsAcceptable(over, out var reason));
        Assert.Contains("260", reason, StringComparison.Ordinal);
        Assert.True(HandPicks.File(atLimit, PageIds.Folders, Context()).Ok);
        Assert.False(HandPicks.File(over, PageIds.Folders, Context()).Ok);
        // The limit is on the real path: one that fits as stored but not once the profile folder is put back in is not given back.
        var stored = Made(HandPicks.File(Profile + @"\" + new string('b', 200), PageIds.Folders, Context()));
        Assert.Null(PickPath.Expand(stored.Location, @"Q:\" + new string('p', 100)));
        // A hand-written file with a longer place than that is refused whole.
        Assert.False(PickStore.Parse("{\"picks\":[{\"id\":\"file:1\",\"kind\":\"file\",\"name\":\"X\",\"page\":\"folders\",\"location\":\"" + over.Replace("\\", "\\\\") + "\"}]}").Status == PickStoreStatus.Loaded);
    }

    [Theory]
    [InlineData(@"Q:\a\..\b.txt", @"Q:\b.txt")]
    [InlineData(@"Q:\a\.\b\..\..\c", @"Q:\c")]
    [InlineData(@"q:\a", @"Q:\a")]
    [InlineData(@"Q:/a/b/c.txt", @"Q:\a\b\c.txt")]
    [InlineData(@"Q:\\a\\\b", @"Q:\a\b")]
    [InlineData(@"Q:\a\b\", @"Q:\a\b")]
    [InlineData(@"Q:\a\b\\\", @"Q:\a\b")]
    [InlineData(@"Q:\", @"Q:\")]
    [InlineData(@"Q:\a\..", @"Q:\")]
    [InlineData(@"Q:\a.", @"Q:\a")]
    [InlineData(@"Q:\a \b", @"Q:\a\b")]
    [InlineData(@"  Q:\a  ", @"Q:\a")]
    [InlineData(@"Q:\Invented\a.b.txt", @"Q:\Invented\a.b.txt")]
    public void A_Place_Is_Written_In_One_Plain_Form(string typed, string plain)
    {
        Assert.True(PickPath.TryNormalize(typed, out var normalized, out _));
        Assert.Equal(plain, normalized);
        Assert.True(PickPath.TryNormalize(normalized, out var again, out _)); // and writing it again changes nothing
        Assert.Equal(normalized, again);
    }

    [Theory]
    [InlineData(@"Q:\..")]
    [InlineData(@"Q:\a\..\..\b")]
    [InlineData(@"Q:\...")]
    [InlineData(@"Q:\a\ . \b")]
    [InlineData(@"Q:\a<b")]
    [InlineData(@"Q:\a>b")]
    [InlineData("Q:\\a\"b")]
    [InlineData(@"Q:\a|b")]
    [InlineData(@"Q:\a?b")]
    [InlineData(@"Q:\a*b")]
    [InlineData(@"Q:\a:stream")]
    [InlineData(@"Q:\CON")]
    [InlineData(@"Q:\a\nul.txt")]
    [InlineData(@"Q:\a\com1")]
    [InlineData(@"Q:\a\LPT9.log")]
    public void A_Place_Windows_Cannot_Make_Is_Refused(string path)
    {
        Assert.False(PickPath.IsAcceptable(path, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void The_Stored_Form_Is_The_Only_Form_A_Pick_May_Hold()
    {
        foreach (var odd in new[] { "Q:/a", @"q:\a", @"Q:\a\", @"Q:\a\.\b", @"%userprofile%\a", @"%USERPROFILE%/a", @"%USERPROFILE%a", @"%USERPROFILE%\a\" })
            Assert.False(new Pick("file:1", PickKind.File, "X", PageIds.Folders, Location: odd).IsStorable(out _), odd);
        foreach (var plain in new[] { @"Q:\a", @"Q:\", "%USERPROFILE%", @"%USERPROFILE%\a b\c.txt" })
            Assert.True(new Pick("file:1", PickKind.File, "X", PageIds.Folders, Location: plain).IsStorable(out var why), plain + why);
        // The existing fields still never hold a path (their own tests are untouched); the new field does not open that door.
        Assert.False(new Pick("file:1", PickKind.File, "X", PageIds.Folders, ExeName: @"Q:\a\x.exe", Location: @"Q:\a\x.exe").IsStorable(out _));
        Assert.False(new Pick("program:1", PickKind.Program, "X", PageIds.Apps, ExeName: "x.exe", Location: "https://example.org").IsStorable(out _));
        Assert.False(new Pick("program:1", PickKind.Program, @"Q:\a", PageIds.Apps, ExeName: "x.exe").IsStorable(out _));
        // A reason never carries the path.
        new Pick("file:1", PickKind.File, "X", PageIds.Folders, Location: @"\\server\invented").IsStorable(out var reason);
        Assert.DoesNotContain("invented", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Long_Or_Odd_Name_Still_Makes_A_Storable_Pick()
    {
        var long255 = new string('n', 100);
        var file = Made(HandPicks.File(@"Q:\Invented\" + long255 + ".txt", PageIds.Folders, Context()));
        Assert.True(file.Name.Length <= 100);
        var astral = Made(HandPicks.File(@"Q:\Invented\" + new string('a', 99) + "\U0001F600.txt", PageIds.Folders, Context()));
        Assert.False(char.IsHighSurrogate(astral.Name[^1])); // never half a character
        Assert.False(HandPicks.File(@"Q:\", PageIds.Folders, Context()).Ok);   // a drive is not a file
        Assert.False(HandPicks.BrowsedProgram(@"Q:\", PageIds.Apps, Context()).Ok);
    }

    // ---- The cache of "is it there" ----------------------------------------

    [Fact]
    public void The_Cache_Never_Blocks_Is_Bounded_And_Asks_Again_After_A_While()
    {
        var now = 0L;
        var cache = new PickTargetCache(() => now);
        var file = Made(HandPicks.File(@"Q:\Invented\Delta.txt", PageIds.Folders, Context()));
        var known = Pick.ForFolder("Downloads", PageIds.Folders);

        Assert.Equal(TargetPresence.NotAsked, cache.Presence(file, Profile));
        Assert.Equal(TargetPresence.Present, cache.Presence(known, Profile)); // nothing to ask about a known folder
        Assert.Empty(cache.ToAsk([known], Profile));
        Assert.Equal([file], cache.ToAsk([known, file], Profile));

        Assert.True(cache.Record(file, Profile, false));
        Assert.False(cache.Record(file, Profile, false));  // nothing changed: the island need not be drawn again
        Assert.Equal(TargetPresence.Missing, cache.Presence(file, Profile));
        Assert.Empty(cache.ToAsk([file], Profile));
        now += PickTargetCache.RecheckAfterMs;
        Assert.Equal([file], cache.ToAsk([file], Profile));
        Assert.Equal(TargetPresence.Missing, cache.Presence(file, Profile)); // the old answer is used until the new one comes
        Assert.True(cache.Refresh(new FakeProbe(_ => true), [file], Profile));
        Assert.Equal(TargetPresence.Present, cache.Presence(file, Profile));

        // A probe that throws claims nothing.
        now += PickTargetCache.RecheckAfterMs;
        Assert.False(cache.Refresh(new FakeProbe(_ => throw new IOException("invented")), [file], Profile));
        Assert.Equal(TargetPresence.Present, cache.Presence(file, Profile));

        // Bounded, and safe from several threads at once.
        var big = new PickTargetCache(() => now);
        Parallel.For(0, PickTargetCache.MaxEntries * 3, i =>
        {
            big.Record(new Pick("file:" + i.ToString("x16"), PickKind.File, "F", PageIds.Folders, Location: @"Q:\Invented\f" + i), Profile, i % 2 == 0);
            _ = big.Presence(file, Profile);
        });
        Assert.InRange(big.Count, 1, PickTargetCache.MaxEntries);
    }

    private sealed class FakeProbe(Func<PickKind, bool> answer) : IPickTargetProbe
    {
        public List<(PickKind Kind, string Path)> Asked { get; } = [];

        public bool Exists(PickKind kind, string path)
        {
            Asked.Add((kind, path));
            return answer(kind);
        }
    }
}
