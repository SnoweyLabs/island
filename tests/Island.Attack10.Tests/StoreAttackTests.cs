using Island.Core;

namespace Island.Attack10.Tests;

/// <summary>The picks file attacked: a path in the wrong field, hostile sizes, old and newer versions, round trips, the cache of target answers.</summary>
public class StoreAttackTests
{
    private static string File2(string pick) => "{\"schema\":2,\"picks\":[" + pick + "]}";

    private static string Pick(string fields) => "{\"id\":\"program:a\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"apps\"," + fields + "}";

    // ---- A path in the wrong field ------------------------------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> WrongField =>
    [
        [Pick("\"exe\":\"Q:\\\\Invented\\\\a.exe\"")],
        [Pick("\"exe\":\"Q:/Invented/a.exe\"")],
        [Pick("\"exe\":\"a.exe\",\"package\":\"Q:\\\\Invented\"")],
        [Pick("\"exe\":\"a.exe\",\"package\":\"Q:/Invented\"")],
        [Pick("\"exe\":\"a.exe\",\"package\":\"Q:x\"")],
        [Pick("\"exe\":\"a.exe\",\"host\":\"Q:/Invented/x\"")],
        [Pick("\"exe\":\"a.exe\",\"folder\":\"Q:\\\\Invented\"")],
        [Pick("\"exe\":\"a.exe\",\"folder\":\"Downloads/..\"")],
        [Pick("\"exe\":\"a.exe\",\"name\":\"x\\\\y\"").Replace("\"name\":\"A\",", "")],
        ["{\"id\":\"program:a\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"Q:\\\\Invented\",\"exe\":\"a.exe\"}"],
        ["{\"id\":\"program:Q:/Invented\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"apps\",\"exe\":\"a.exe\"}"],
        ["{\"id\":\"program:a b\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"apps\",\"exe\":\"a.exe\"}"],
        ["{\"id\":\"program:a\\\\b\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"apps\",\"exe\":\"a.exe\"}"],
        ["{\"id\":\"site:a.org\",\"kind\":\"site\",\"name\":\"a.org\",\"page\":\"apps\",\"host\":\"a.org\",\"location\":\"Q:\\\\Invented\\\\x\"}"],
        ["{\"id\":\"site:a.org\",\"kind\":\"site\",\"name\":\"a.org\",\"page\":\"apps\",\"host\":\"https://a.org/x\"}"],
        ["{\"id\":\"file:1\",\"kind\":\"file\",\"name\":\"n\",\"page\":\"apps\"}"], // a file with no place
        ["{\"id\":\"file:1\",\"kind\":\"file\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\Invented\\\\x\",\"exe\":\"a.exe\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\"}"], // a folder with neither
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"folder\":\"Downloads\",\"location\":\"Q:\\\\Invented\\\\x\"}"], // both
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\Invented\\\\x\\\\..\\\\..\\\\..\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"\\\\\\\\server\\\\share\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"\\\\\\\\?\\\\Q:\\\\x\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"https://example.org/x\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"%APPDATA%\\\\x\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"%USERPROFILE%x\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"%USERPROFILE%\\\\..\\\\x\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\a:b\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\CON\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\a\\u0000b\"}"],
        ["{\"id\":\"banana:1\",\"kind\":\"banana\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\a\"}"], // an unknown kind
        ["{\"id\":\"folder:1\",\"kind\":7,\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\a\"}"],
        ["{\"id\":\"folder:1\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\a\"}"],
        ["{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\" + "\"}"], // fine as a drive root, but see the next theory
    ];

    [Theory]
    [MemberData(nameof(WrongField))]
    public void Holds_A_Pick_That_Is_Wrong_Anywhere_Makes_The_File_Unreadable_With_Words_That_Name_No_Path(string pick)
    {
        var load = PickStore.Parse(File2(pick));
        if (load.Status == PickStoreStatus.Loaded)
        {
            // only the drive-root folder at the end of the list is a good pick
            Assert.Equal(@"Q:\", load.Store.Picks[0].Location);
            return;
        }

        Assert.Equal(PickStoreStatus.Unreadable, load.Status);
        Assert.Empty(load.Store.Picks);
        Assert.NotNull(load.Detail);
        Assert.DoesNotContain("Invented", load.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Q:\", load.Detail!, StringComparison.Ordinal);
        Assert.DoesNotContain("server", load.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void Holds_A_Megabyte_Location_And_Other_Garbage_Never_Throw()
    {
        var megabyte = new string('a', 1 << 20);
        foreach (var text in new[]
                 {
                     File2("{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\" + megabyte + "\"}"),
                     File2("{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"" + megabyte + "\",\"page\":\"apps\",\"location\":\"Q:\\\\a\"}"),
                     File2("{\"id\":\"" + megabyte + "\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"Q:\\\\a\"}"),
                     "", " ", "null", "[]", "{", "}", "\0", "\uFEFF{}", "{\"picks\":null}", "{\"picks\":{}}", "{\"picks\":[1]}", "{\"picks\":[null]}", "{\"picks\":[[]]}", "{\"schema\":2}",
                     "{\"schema\":2,\"picks\":[],\"picks\":[]}", new string('[', 5000), "{\"a\":" + string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200)) + "}",
                     "{\"schema\":2,\"picks\":[{\"id\":\"a:b\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"p\",\"location\":\"Q:\\\\a\",\"location\":\"Q:\\\\b\"}]}",
                     "{\"schema\":2,\"picks\":[{\"id\":\"a:b\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"p\",\"location\":\"Q:\\\\a\\ud800\"}]}", // a lone surrogate written as an escape
                 })
        {
            var load = PickStore.Parse(text);
            Assert.True(load.Status is PickStoreStatus.Unreadable or PickStoreStatus.Loaded);
            if (load.Status == PickStoreStatus.Unreadable) Assert.NotNull(load.Detail);
        }
    }

    [Fact]
    public void Holds_A_Path_That_Is_An_Address_Is_Not_A_Location()
    {
        foreach (var address in new[] { "https://example.org/x", "file:///Q:/x", "ftp://Q:/x", "mailto:a@b.org", "javascript:alert(1)" })
            Assert.Equal(PickStoreStatus.Unreadable, PickStore.Parse(File2("{\"id\":\"file:1\",\"kind\":\"file\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"" + address + "\"}")).Status);
    }

    // ---- Versions ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Old_Files_Still_Load_Newer_And_Odd_Numbers_Do_Not()
    {
        var plain = "{\"id\":\"program:a\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"apps\",\"exe\":\"a.exe\"}";
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Parse("{\"picks\":[" + plain + "]}").Status); // version 1: no number at all
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Parse("{\"version\":1,\"picks\":[" + plain + "]}").Status);
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Parse("{\"schema\":1,\"picks\":[" + plain + "]}").Status);
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Parse("{\"schema\":2,\"picks\":[" + plain + "]}").Status);

        foreach (var number in new[] { "3", "4", "99", "2147483647" })
        {
            var load = PickStore.Parse("{\"schema\":" + number + ",\"picks\":[" + plain + "]}");
            Assert.Equal(PickStoreStatus.Unreadable, load.Status);
            Assert.True(FileSchema.IsNewer(load.Detail), number);
            Assert.Empty(load.Store.Picks);
        }

        foreach (var bad in new[] { "0", "-1", "\"2\"", "2.5", "99999999999", "null", "true", "[]" })
        {
            var load = PickStore.Parse("{\"schema\":" + bad + ",\"picks\":[" + plain + "]}");
            Assert.Equal(PickStoreStatus.Unreadable, load.Status);
            Assert.False(FileSchema.IsNewer(load.Detail), bad);
        }

        // the older key "version" is looked at as well: the higher one of the two decides
        Assert.True(FileSchema.IsNewer(PickStore.Parse("{\"schema\":1,\"version\":5,\"picks\":[" + plain + "]}").Detail));
    }

    [Fact]
    public void Holds_Schema_2_With_An_Unknown_Kind_Or_A_Newer_Field_Is_Handled_Without_Guessing()
    {
        // an unknown kind makes the file unreadable (left untouched); an unknown extra field is ignored and dropped on the next save
        var extra = "{\"id\":\"program:a\",\"kind\":\"program\",\"name\":\"A\",\"page\":\"apps\",\"exe\":\"a.exe\",\"future\":\"x\"}";
        var load = PickStore.Parse(File2(extra));
        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.DoesNotContain("future", load.Store.ToJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Holds_The_Repo_Fixtures_Of_Both_Shapes_Still_Load()
    {
        foreach (var version in new[] { "v1", "v2" })
        {
            var json = Repo.Text("tests", "Island.Tests", "Fixtures", version, "picks.json");
            var load = PickStore.Parse(json);
            Assert.Equal(PickStoreStatus.Loaded, load.Status);
            Assert.NotEmpty(load.Store.Picks);
        }
    }

    [Fact]
    public void Holds_A_Hand_Edited_Spelling_Is_Brought_To_The_One_Form_On_Load()
    {
        var json = File2("{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"%userprofile%/Docs/\"}");
        var load = PickStore.Parse(json);
        Assert.Equal(@"%USERPROFILE%\Docs", load.Store.Picks[0].Location);
        var plain = File2("{\"id\":\"folder:1\",\"kind\":\"folder\",\"name\":\"n\",\"page\":\"apps\",\"location\":\"q:/Invented//Alpha/./\"}");
        Assert.Equal(@"Q:\Invented\Alpha", PickStore.Parse(plain).Store.Picks[0].Location);
    }

    [Fact]
    public void Holds_The_Schema_Written_Follows_The_Shape()
    {
        var plain = new PickStore([Pick_("program:a", exe: "a.exe")]);
        Assert.Contains("\"schema\": 1", plain.ToJson(), StringComparison.Ordinal);
        var place = new PickStore([plain.Picks[0], new Pick("folder:1", PickKind.Folder, "F", "apps", Location: @"Q:\Invented\F")]);
        Assert.Contains("\"schema\": 2", place.ToJson(), StringComparison.Ordinal);
        var file = new PickStore([new Pick("file:1", PickKind.File, "F", "apps", Location: @"Q:\Invented\F.txt")]);
        Assert.Contains("\"schema\": 2", file.ToJson(), StringComparison.Ordinal);
    }

    private static Pick Pick_(string id, string? exe = null) => new(id, PickKind.Program, "A", "apps", ExeName: exe);

    // ---- Round trips and saving -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Unusual_But_Legal_Paths_Round_Trip_Through_The_File()
    {
        var leaves = new[] { "plain", "with space", "caf\u00E9", "\u4E2D\u6587", "emoji" + char.ConvertFromUtf32(0x1F600), "a&b", "a+b", "a'b", "100%", "[x]", "a;b,c", "%USERPROFILE%", "x" + new string('y', 150) };
        using var temp = new TempFolder();
        foreach (var leaf in leaves)
        {
            var pick = HandPicks.Folder(@"Q:\Invented\" + leaf, "apps", new HandContext(null, [])).Pick!;
            var store = new PickStore([pick]);
            Assert.True(store.Save(temp.File("p.json")), leaf);
            var back = PickStore.Load(temp.File("p.json"));
            Assert.Equal(PickStoreStatus.Loaded, back.Status);
            Assert.Equal(pick, back.Store.Picks[0]);
            Assert.Equal(@"Q:\Invented\" + leaf, back.Store.Picks[0].Location);
        }
    }

    [Fact]
    public void Defect_A_Chosen_Path_With_A_Lone_Surrogate_Is_Saved_As_A_Different_Path()
    {
        // NTFS lets a file name hold half of a surrogate pair; the choosing window can return it. TryNormalize accepts it (it is not a control character), the file writer
        // replaces the half pair with U+FFFD, and the pick that is read back points at a file that does not exist: it turns grey and says "not found" at the next start.
        var path = @"Q:\Invented\a" + (char)0xD800 + "b.txt";
        var made = HandPicks.File(path, "apps", new HandContext(null, []));
        using var temp = new TempFolder();
        if (!made.Ok) return; // refused: fine
        var store = new PickStore([made.Pick!]);
        var saved = store.Save(temp.File("p.json")); // must return, true or false
        if (saved) Assert.Equal(made.Pick!.Location, PickStore.Load(temp.File("p.json")).Store.Picks[0].Location); // what is read back is the very place that was saved
    }

    [Fact]
    public void Holds_Save_Refuses_A_Pick_That_Is_Not_Storable_And_Leaves_No_Temp_File()
    {
        using var temp = new TempFolder();
        var bad = new PickStore([new Pick("folder:1", PickKind.Folder, "F", "apps", Location: @"\\server\share")]);
        Assert.False(bad.Save(temp.File("p.json")));
        Assert.False(System.IO.File.Exists(temp.File("p.json")));
        Assert.False(System.IO.File.Exists(temp.File("p.json.tmp")));
        var spelled = new PickStore([new Pick("folder:1", PickKind.Folder, "F", "apps", Location: Make.Profile + @"\Docs")]);
        Assert.False(spelled.Save(temp.File("p.json"), Make.Profile)); // refused when it carries the profile folder
        Assert.True(spelled.Save(temp.File("p.json"))); // and accepted when nobody says what the profile folder is
    }

    [Fact]
    public void Holds_A_Thousand_Picks_Load_And_A_Thousand_And_One_Do_Not()
    {
        var thousand = Enumerable.Range(0, 1000).Select(i => new Pick($"folder:{i:x16}", PickKind.Folder, "F" + i, "apps", Location: @"Q:\Invented\F" + i)).ToList();
        var store = new PickStore(thousand);
        Assert.Equal(1000, store.Picks.Count);
        var load = PickStore.Parse(store.ToJson());
        Assert.Equal(PickStoreStatus.Loaded, load.Status);
        Assert.Equal(1000, load.Store.Picks.Count);
        var more = store.Add(new Pick("folder:ffff", PickKind.Folder, "G", "apps", Location: @"Q:\Invented\G"), out var added);
        Assert.False(added);
        Assert.Same(store, more);
        var tooMany = "{\"schema\":2,\"picks\":[" + string.Join(',', Enumerable.Range(0, 1001).Select(i => "{\"id\":\"folder:" + i + "\",\"kind\":\"folder\",\"name\":\"F\",\"page\":\"apps\",\"location\":\"Q:\\x" + i + "\"}")) + "]}";
        Assert.Equal(PickStoreStatus.Unreadable, PickStore.Parse(tooMany).Status);
    }

    // ---- The cache of target answers ----------------------------------------------------------------------------------------------------------------------

    private static Pick Folder(string leaf, string where = @"Q:\Invented\") => HandPicks.Folder(where + leaf, "apps", new HandContext(null, [])).Pick!;

    private sealed class Probe(Func<PickKind, string, bool> answer) : IPickTargetProbe
    {
        public List<(PickKind Kind, string Path)> Asked { get; } = [];

        public bool Exists(PickKind kind, string path)
        {
            lock (Asked) Asked.Add((kind, path));
            return answer(kind, path);
        }
    }

    [Fact]
    public void Holds_A_Probe_That_Throws_Leaves_The_Pick_Unanswered_And_Is_Asked_Again()
    {
        var cache = new PickTargetCache(() => 0);
        var pick = Folder("a");
        var calls = 0;
        var probe = new Probe((_, _) => ++calls == 1 ? throw new IOException("the disk is gone") : true);

        Assert.False(cache.Refresh(probe, [pick], null));
        Assert.Equal(TargetPresence.NotAsked, cache.Presence(pick, null));
        Assert.Single(cache.ToAsk([pick], null));
        Assert.True(cache.Refresh(probe, [pick], null));
        Assert.Equal(TargetPresence.Present, cache.Presence(pick, null));
    }

    [Fact]
    public void Holds_A_Probe_That_Throws_Anything_But_Out_Of_Memory_Is_Survived()
    {
        var cache = new PickTargetCache();
        foreach (var exception in new Exception[] { new IOException(), new UnauthorizedAccessException(), new InvalidOperationException(), new NotSupportedException(), new ArgumentException(), new System.Security.SecurityException(), new TimeoutException(), new NullReferenceException() })
            Assert.False(cache.Refresh(new Probe((_, _) => throw exception), [Folder("a")], null));
        Assert.Throws<OutOfMemoryException>(() => cache.Refresh(new Probe((_, _) => throw new OutOfMemoryException()), [Folder("a")], null));
    }

    [Fact]
    public void Holds_A_Probe_That_Blocks_Does_Not_Block_The_Drawing_Thread()
    {
        var cache = new PickTargetCache();
        var pick = Folder("a");
        using var release = new ManualResetEventSlim();
        using var inside = new ManualResetEventSlim();
        var probe = new Probe((_, _) =>
        {
            inside.Set();
            release.Wait();
            return false;
        });
        var worker = Task.Run(() => cache.Refresh(probe, [pick], null));
        Assert.True(inside.Wait(5000));
        var reads = Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                cache.Presence(pick, null);
                cache.ToAsk([pick], null);
                cache.Record(Folder("b" + i), null, true);
            }
        });
        Assert.True(reads.Wait(5000), "the cache was locked while the probe waited");
        release.Set();
        Assert.True(worker.Result);
        Assert.Equal(TargetPresence.Missing, cache.Presence(pick, null));
    }

    [Fact]
    public void Holds_Answers_That_Change_Every_Time_Are_Reported_As_Changes_And_Old_Ones_Are_Asked_Again()
    {
        var now = 0L;
        var cache = new PickTargetCache(() => now);
        var pick = Folder("a");
        var next = true;
        var probe = new Probe((_, _) => next = !next);

        Assert.True(cache.Refresh(probe, [pick], null)); // false: first answer
        Assert.False(cache.Refresh(probe, [pick], null)); // trusted: not asked again
        Assert.Single(probe.Asked);
        now += PickTargetCache.RecheckAfterMs;
        Assert.Single(cache.ToAsk([pick], null));
        Assert.Equal(TargetPresence.Missing, cache.Presence(pick, null)); // the old answer is used until the new one comes
        Assert.True(cache.Refresh(probe, [pick], null));
        Assert.Equal(TargetPresence.Present, cache.Presence(pick, null));
        now += PickTargetCache.RecheckAfterMs * 3;
        Assert.True(cache.Refresh(probe, [pick], null));
        Assert.Equal(TargetPresence.Missing, cache.Presence(pick, null));
    }

    [Fact]
    public void Defect_One_Hung_Place_Holds_Back_The_Answers_For_Every_Other_Place()
    {
        // Refresh asks the picks one after another on one thread. A place on a drive letter that hangs (a mapped network drive that went away) blocks that thread inside the probe,
        // so a place that is perfectly readable, later in the list, is never answered and its tile never turns grey or bright on the evidence. The timer calls Refresh again every 5 seconds.
        var cache = new PickTargetCache();
        var hung = Folder("hung");
        var fine = Folder("fine");
        using var release = new ManualResetEventSlim();
        using var inside = new ManualResetEventSlim();
        var probe = new Probe((_, path) =>
        {
            if (!path.Contains("HUNG", StringComparison.OrdinalIgnoreCase)) return false;
            inside.Set();
            release.Wait();
            return true;
        });

        var worker = Task.Run(() => cache.Refresh(probe, [hung, fine], null));
        try
        {
            Assert.True(inside.Wait(5000));
            SpinWait.SpinUntil(() => cache.Presence(fine, null) != TargetPresence.NotAsked, 1500);
            Assert.NotEqual(TargetPresence.NotAsked, cache.Presence(fine, null));
        }
        finally
        {
            release.Set();
            worker.Wait(5000);
        }
    }

    [Fact]
    public void Defect_A_Place_Being_Asked_About_Is_Asked_Again_By_Every_Overlapping_Refresh()
    {
        // The timer in PickPages fires every 5 seconds whether or not the last call has come back, and the cache keeps no note of a question in flight, so a probe that takes longer
        // than the period is entered again and again for the same place, one pool thread each.
        var cache = new PickTargetCache();
        var pick = Folder("slow");
        using var release = new ManualResetEventSlim();
        var entered = 0;
        var probe = new Probe((_, _) =>
        {
            Interlocked.Increment(ref entered);
            release.Wait();
            return true;
        });

        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() => cache.Refresh(probe, [pick], null))).ToArray();
        SpinWait.SpinUntil(() => Volatile.Read(ref entered) >= 4, 1500);
        var seen = Volatile.Read(ref entered);
        release.Set();
        Task.WaitAll(workers, 5000);
        Assert.Equal(1, seen);
    }

    [Fact]
    public void Holds_The_Cache_Is_Safe_From_Many_Threads_And_Never_Grows_Past_Its_Limit()
    {
        var cache = new PickTargetCache();
        var picks = Enumerable.Range(0, 3000).Select(i => Folder("f" + i)).ToArray();
        var probe = new Probe((_, path) => path.GetHashCode() % 2 == 0);
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, worker =>
        {
            var rng = new Random(worker);
            for (var i = 0; i < 4000; i++)
            {
                var pick = picks[rng.Next(picks.Length)];
                switch (rng.Next(4))
                {
                    case 0: cache.Record(pick, null, rng.Next(2) == 0); break;
                    case 1: cache.Presence(pick, null); break;
                    case 2: cache.ToAsk(picks.Take(50), null); break;
                    default: cache.Refresh(probe, [pick, picks[rng.Next(picks.Length)]], null); break;
                }
            }
        });
        Assert.InRange(cache.Count, 1, PickTargetCache.MaxEntries);
    }

    [Fact]
    public void Holds_Only_Things_With_A_Place_Are_Asked_About_And_The_Real_Path_Is_What_The_Probe_Sees()
    {
        var cache = new PickTargetCache();
        var withPlace = HandPicks.Folder(Make.Profile + @"\Docs", "apps", new HandContext(Make.Profile, [])).Pick!;
        var file = HandPicks.File(@"Q:\Invented\a.txt", "apps", new HandContext(Make.Profile, [])).Pick!;
        var program = Pick_("program:a", "a.exe");
        var site = HandPicks.Site("example.org", "apps").Pick!;
        var probe = new Probe((_, _) => true);

        cache.Refresh(probe, [withPlace, file, program, site], Make.Profile);
        Assert.Equal(2, probe.Asked.Count);
        Assert.Contains((PickKind.Folder, Make.Profile + @"\Docs"), probe.Asked);
        Assert.Contains((PickKind.File, @"Q:\Invented\a.txt"), probe.Asked);
        Assert.Equal(TargetPresence.Present, cache.Presence(program, null));
        Assert.Equal(TargetPresence.Present, cache.Presence(site, null));

        // a token with no profile folder to put in its place cannot be asked, and is never claimed missing
        var other = new PickTargetCache();
        var silent = new Probe((_, _) => false);
        Assert.False(other.Refresh(silent, [withPlace], null));
        Assert.Empty(silent.Asked);
        Assert.Equal(TargetPresence.Present, other.Presence(withPlace, null));
    }

    [Fact]
    public void Holds_A_File_And_A_Folder_At_One_Place_Are_Asked_Separately()
    {
        var cache = new PickTargetCache();
        var folder = new Pick("folder:1", PickKind.Folder, "x", "apps", Location: @"Q:\Invented\x");
        var file = new Pick("file:1", PickKind.File, "x", "apps", Location: @"Q:\Invented\x");
        cache.Record(folder, null, true);
        cache.Record(file, null, false);
        Assert.Equal(TargetPresence.Present, cache.Presence(folder, null));
        Assert.Equal(TargetPresence.Missing, cache.Presence(file, null));
    }
}
