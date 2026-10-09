using Island.Core;

namespace Island.Attack10.Tests;

/// <summary>Picks made by hand attacked: typed addresses, ids, names, browsed programs, folders and files. Invented names and invented paths on Q: only.</summary>
public class HandPickAttackTests
{
    private const string Profile = @"Q:\Invented\Profile";

    private static HandContext Context(Func<ulong>? random = null) => new(Profile, [("Downloads", Profile + @"\Downloads"), ("Music", Profile + @"\Music")], random);

    // ---- Typed addresses -------------------------------------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> NotSites =>
    [
        [""], [" "], ["\t"], ["\n"], ["example"], ["example."], [".org"], ["."], ["..."], ["http://"], ["https://"], ["//"], ["///"], ["@"], ["http://@"], ["http://user@"], ["user@"], ["#x.org"], ["?x=example.org"],
        ["ftp://example.org"], ["file:///Q:/x"], ["javascript:alert(1)"], ["ws://example.org"], ["http://http://example.org"], ["1.2.3.4"], ["127.0.0.1:8080"], ["http://10.0.0.1/"], ["1.2.3"], ["[::1]"], ["http://[::1]/"],
        ["exa mple.org"], ["example .org"], ["example. org"], ["example..org"], ["-a.org"], ["a-.org"], ["a_b.org"], ["exa$mple.org"], ["example.org:99999"], ["example.org:abc"], ["example.org:-1"],
        ["example.123"], ["example.org%2f"], ["exa\0mple.org"], ["exa\u0001mple.org"], ["\\\\example.org"], ["http:\\\\example.org"], ["localhost"], ["xn--.."], [new string('a', 70) + ".org"],
        ["http://" + new string('x', 5000) + ".org"], [new string(' ', 3000) + "example.org" + new string(' ', 3000)],
    ];

    [Theory]
    [MemberData(nameof(NotSites))]
    public void Holds_Nonsense_Is_Not_A_Site_And_Gets_The_Register_Words(string typed)
    {
        var understood = TypedSite.Understand(typed);
        Assert.False(understood.Ok);
        Assert.Equal("NOT_A_SITE", understood.Refusal?.Code);
        var added = HandPicks.Site(typed, "apps");
        Assert.False(added.Ok);
        Assert.Equal("NOT_A_SITE", added.Code);
        Assert.NotEmpty(added.Message!);
        Assert.Equal(HandPickRefusals.NotASite.Message, added.Message); // the register's own words, never the typed text
    }

    [Theory]
    [InlineData("example.org", "example.org")]
    [InlineData("EXAMPLE.ORG", "example.org")]
    [InlineData("https://www.Example.org/page?x=1#y", "example.org")]
    [InlineData("http://user:pw@example.org:8080/", "example.org")]
    [InlineData("example.org/path/to", "example.org")]
    [InlineData("  example.org.  ", "example.org")]
    [InlineData("//example.org", "example.org")]
    [InlineData("sub.example.co.uk:443", "sub.example.co.uk")]
    [InlineData("https://evil.test@example.org/", "example.org")]
    [InlineData("https://example.org\\@evil.test/", "example.org")]
    [InlineData("https://example.org#@evil.test", "example.org")]
    [InlineData("https://example.org?u=a@evil.test", "example.org")]
    [InlineData("xn--bcher-kva.de", "xn--bcher-kva.de")]
    [InlineData("b\u00FCcher.de", "xn--bcher-kva.de")]
    [InlineData("example.org:", "example.org")]
    [InlineData("HTTP://example.org", "example.org")]
    [InlineData("www.example.org", "example.org")]
    [InlineData("a.b", "a.b")]
    [InlineData("123-456.com", "123-456.com")]
    public void Holds_A_Typed_Address_Becomes_Its_Host(string typed, string host)
    {
        var understood = TypedSite.Understand(typed);
        Assert.Equal(host, understood.Host);
        var made = HandPicks.Site(typed, "browser");
        Assert.True(made.Ok, made.Message);
        Assert.Equal(host, made.Pick!.Host);
        Assert.Equal("site:" + host, made.Pick.Id);
        Assert.True(made.Pick.IsStorable(out var why), why);
        Assert.Null(made.Pick.Location);
    }

    [Fact]
    public void Holds_Control_Characters_And_Newlines_Never_Reach_A_Host()
    {
        for (var c = 0; c < 32; c++)
            Assert.False(TypedSite.Understand($"exa{(char)c}mple.org").Ok, $"U+{c:X4}");
        Assert.False(TypedSite.Understand("example.org\u007F").Ok);
        Assert.False(TypedSite.Understand("example.org\u0085").Ok);
    }

    [Fact]
    public void Holds_Odd_Unicode_Hosts_Never_Throw_And_Never_Give_A_Non_Ascii_Host()
    {
        var inputs = new[]
        {
            "\uD800.org", "a\uDC00b.org", "\uD83D\uDE00.org", "\u202Eexample.org", "exa\u200Bmple.org", "\u200Bexample.org\u200B", "\uFF45\uFF58\uFF41\uFF4D\uFF50\uFF4C\uFF45.org", "example\u3002org",
            "\u0130STANBUL.com", "\u00DF.de", "\u0645\u062B\u0627\u0644.\u0625\u062E\u062A\u0628\u0627\u0631", new string('\u00E9', 40) + ".org", "\u2160.com", "a\u0301.org", "\u0000.org", "xn--a.org", "xn--99999999.org",
        };
        foreach (var typed in inputs)
        {
            var understood = TypedSite.Understand(typed);
            if (understood.Ok) Assert.All(understood.Host!, c => Assert.True(c < 128, typed));
            var made = HandPicks.Site(typed, "apps");
            Assert.Equal(understood.Ok, made.Ok || made.Code != "NOT_A_SITE");
        }
    }

    [Fact]
    public void Holds_A_Site_Name_Is_Never_Made_From_A_Path_And_A_Site_Has_No_Place()
    {
        var site = HandPicks.Site("example.org/C:/Invented/Alpha", "browser");
        Assert.True(site.Ok);
        Assert.Null(site.Pick!.Location);
        Assert.DoesNotContain("Invented", new PickStore([site.Pick]).ToJson(), StringComparison.Ordinal);
    }

    // ---- Defects -----------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.0x1")]
    [InlineData("1.1.1.0x1")]
    [InlineData("0x7f.0.0.0x1")]
    public void Defect_A_Hex_Spelling_Of_An_Ip_Address_Is_Taken_As_A_Site(string typed)
    {
        // The rule refuses an IP address (a last label of only digits). A browser reads a last label such as 0x1 as a number too, so 127.0.0.0x1 is 127.0.0.1;
        // the island keeps it as a "website" and later hands https://127.0.0.0x1/ to the browser.
        Assert.False(TypedSite.Understand(typed).Ok, "taken as a site: " + TypedSite.Understand(typed).Host);
    }

    [Fact]
    public void Defect_A_Valid_Host_Of_196_To_253_Characters_Is_Understood_And_Then_Refused_With_Words_About_An_Id()
    {
        // The screen shows the host it understood (a valid DNS name may have 253 characters) and, on confirming, the pick is refused: its id "site:<host>" may hold only 200
        // characters (and its name and host fields 200). The reason shown talks about an id, a word the person never saw.
        var host = string.Join('.', Enumerable.Repeat(new string('a', 60), 4)) + ".org"; // 247 characters
        var understood = TypedSite.Understand(host);
        Assert.True(host.Length > 195);
        var made = HandPicks.Site(host, "browser");
        Assert.True(made.Ok == understood.Ok, $"understood but refused on confirming: {made.Message}"); // one answer on the screen and on confirming (the fix refuses it at the screen)
    }

    [Fact]
    public void Defect_What_The_Screen_Shows_Is_Not_Always_What_Is_Kept()
    {
        // UnderstandSite shows host "www.example.org" for "www.www.example.org" (Normalize cuts one leading www.), then Pick.ForSite cuts another: the pick keeps example.org.
        var typed = "www.www.example.org";
        var shown = TypedSite.Understand(typed).Host;
        var kept = HandPicks.Site(typed, "browser").Pick?.Host;
        Assert.Equal(shown, kept);
    }

    [Fact]
    public void Defect_A_Folder_Pick_Of_The_Profile_Folder_Itself_Is_Named_As_A_Drive()
    {
        // The profile folder is stored as the bare token, whose leaf is deliberately null (the leaf would be the account name), and the name then falls back to "Drive <letter>".
        var made = HandPicks.Folder(Profile, "folders", Context());
        Assert.True(made.Ok, made.Message);
        Assert.Equal("%USERPROFILE%", made.Pick!.Location);
        Assert.DoesNotContain("Drive", made.Pick.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Defect_A_Program_Browsed_To_Is_Added_Again_When_It_Is_Already_In_The_Island_From_The_List()
    {
        // EVALS C5: the same thing cannot be put on the island twice. The program from the list is program:alpha with exe alpha.exe and no place; browsing to alpha.exe makes
        // program:<random> with the same exe and a place. IsSameThing compares ids and places only, so both stay and both tiles show the same windows.
        var listed = HandPicks.FromInstalledProgram(new InstalledProgram("Alpha", "alpha.exe", null, @"Q:\Invented\Alpha\alpha.exe"), "apps").Pick!;
        var browsed = HandPicks.BrowsedProgram(@"Q:\Invented\Alpha\Alpha.exe", "apps", Context()).Pick!;
        var store = new PickStore([listed]);
        store.Add(browsed, out var added);
        Assert.False(added, "the same program is on the island twice");
    }

    [Fact]
    public void Defect_A_Spelled_Out_Profile_Path_Is_Not_The_Same_Thing_As_The_Token_Form_Of_It()
    {
        // A picks file written by hand (or by an older tool) can spell the profile folder out. The same folder chosen now is stored as the token, and IsSameThing compares the two
        // stored spellings without expanding the token, so the folder goes on the island twice.
        // A Pick alone cannot know the profile folder; the session that loaded the file does, and brings the spelled-out place to the token before it compares.
        var spelled = new Pick("folder:0000000000000001", PickKind.Folder, "Docs", "apps", Location: Profile + @"\Docs");
        using var temp = new TempFolder();
        var session = Make.Session(temp, [spelled], chooser: new Island.Core.SettingsEdit.PretendChooser(folder: Profile + @"\Docs"));
        var added = session.AddFolder("folders");
        Assert.False(added.Added, "the same folder under its spelled-out and its token form is two things");
        Assert.NotNull(added.AlreadyOn);
    }

    [Fact]
    public void Defect_The_Missing_Target_Message_Outgrows_The_Balloon_When_The_Name_Is_Long()
    {
        // PICK_TARGET_MISSING is shown from the tray icon (TrayIcon.Notify -> a balloon) and the repo's own limit for a balloon is SceneRefusals.MaxMessageLength (250: "Windows shows at most
        // 255 characters"). Scene refusals shorten the names to fit; this one puts the pick's name in whole, and a hand-made pick's name is up to 100 characters (the leaf of the chosen file).
        // The end of the message, "Remove it from the island and add it again from where it is now", is the part that is cut off.
        var name = new string('n', 100);
        var made = HandPicks.File(@"Q:\Invented\" + name + ".txt", "apps", Context());
        Assert.True(made.Ok, made.Message);
        var message = HandPickRefusals.ForMissing(made.Pick!).Message;
        Assert.True(message.Length <= SceneRefusals.MaxMessageLength, $"{message.Length} characters");
    }

    [Fact]
    public void Holds_The_Mark_And_The_Hue_Of_Any_Name_Never_Throw()
    {
        var rng = new Random(99);
        var alphabet = new[] { "a", "Z", "1", " ", "-", "_", ".", "\u00E9", "\u0301", "\u200B", "\u202E", "\uD83D\uDE00", ((char)0xD800).ToString(), ((char)0xDC00).ToString(), "\u4E2D", "\0", "\t", "\u3164" };
        for (var i = 0; i < 20_000; i++)
        {
            var name = string.Concat(Enumerable.Range(0, rng.Next(0, 8)).Select(_ => alphabet[rng.Next(alphabet.Length)]));
            Assert.NotNull(PickItems.Mark(name));
            Assert.InRange(PickItems.Hue(name), 0, 359);
        }
    }

    // ---- Holds: ids, names, kinds -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_An_Id_Is_Kind_Colon_And_Sixteen_Hex_Digits_And_Never_The_Path()
    {
        var path = @"Q:\Invented\Alpha";
        var random = Context(() => 0xABCDEFUL);
        foreach (var made in new[]
                 {
                     HandPicks.Folder(path, "apps", random), HandPicks.File(path + @"\a.txt", "apps", random), HandPicks.BrowsedProgram(path + @"\a.exe", "apps", random),
                 })
        {
            Assert.True(made.Ok, made.Message);
            var id = made.Pick!.Id;
            Assert.Matches("^(folder|file|program):[0-9a-f]{16}$", id);
            Assert.True(PickKeysJson.IsUsableId(id));
            Assert.DoesNotContain("alpha", id, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("folder:0000000000abcdef", HandPicks.NewId(PickKind.Folder, () => 0xABCDEFUL));
        Assert.Equal("file:ffffffffffffffff", HandPicks.NewId(PickKind.File, () => ulong.MaxValue));
        // two different folders get two different ids with the real random source, and one folder added twice does too (so the place decides "the same thing")
        Assert.NotEqual(HandPicks.NewId(PickKind.Folder), HandPicks.NewId(PickKind.Folder));
    }

    [Fact]
    public void Holds_Ids_Are_Unique_Over_A_Hundred_Thousand_Draws_And_Always_Usable()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 100_000; i++)
        {
            var id = HandPicks.NewId(PickKind.Folder);
            Assert.True(seen.Add(id));
            Assert.True(PickKeysJson.IsUsableId(id));
        }
    }

    [Fact]
    public void Holds_Names_Are_Cut_Whole_And_Fit_The_Pick()
    {
        var longLeaf = new string('x', 200);
        var folder = HandPicks.Folder(@"Q:\Invented\" + longLeaf, "apps", Context());
        Assert.True(folder.Ok, folder.Message);
        Assert.True(folder.Pick!.Name.Length <= 100);

        // a leaf of pairs outside the basic plane, cut at a pair boundary
        var emoji = string.Concat(Enumerable.Repeat("\uD83D\uDE00", 60));
        var file = HandPicks.File(@"Q:\Invented\" + emoji + ".txt", "apps", Context());
        Assert.True(file.Ok, file.Message);
        Assert.False(char.IsHighSurrogate(file.Pick!.Name[^1]));

        // a file name of only spaces and dots is not a name
        Assert.False(HandPicks.File(@"Q:\Invented\ . .", "apps", Context()).Ok);
        Assert.False(HandPicks.File(@"Q:\", "apps", Context()).Ok); // a drive is not a file
        Assert.False(HandPicks.BrowsedProgram(@"Q:\", "apps", Context()).Ok);
        Assert.Equal("Drive Q", HandPicks.Folder(@"Q:\", "apps", Context()).Pick!.Name);
    }

    [Fact]
    public void Holds_A_Name_With_A_Percent_Or_A_Space_Or_A_Dot_Is_Fine_And_Stored()
    {
        foreach (var leaf in new[] { "100%", "my folder", "a.b.c", "caf\u00E9", "\u4E2D\u6587", "-x-", "x&y", "x+y", "x'y", "x;y", "[x]", "x,y" })
        {
            var made = HandPicks.Folder(@"Q:\Invented\" + leaf, "apps", Context());
            Assert.True(made.Ok, leaf + ": " + made.Message);
            var store = new PickStore([made.Pick!]);
            var back = PickStore.Parse(store.ToJson());
            Assert.Equal(PickStoreStatus.Loaded, back.Status);
            Assert.Equal(made.Pick, back.Store.Picks[0]);
        }
    }

    [Fact]
    public void Holds_A_Shortcut_And_A_Program_Keep_Their_Own_Place_And_A_Wrong_Target_Name_Is_Refused()
    {
        var shortcut = HandPicks.BrowsedProgram(@"Q:\Invented\Tool.lnk", "apps", Context(), @"Q:\Invented\evil.exe");
        Assert.False(shortcut.Ok); // a target given as a path would be a path in the exe field
        Assert.Contains("looks like a path", shortcut.Message, StringComparison.Ordinal);
        var plain = HandPicks.BrowsedProgram(@"Q:\Invented\Tool.lnk", "apps", Context(), "evil");
        Assert.False(plain.Ok); // not an exe name
        Assert.True(HandPicks.BrowsedProgram(@"Q:\Invented\Tool.lnk", "apps", Context(), null).Ok);
        Assert.True(HandPicks.BrowsedProgram(@"Q:\Invented\a b.EXE", "apps", Context()).Ok);
    }

    [Fact]
    public void Holds_A_Known_Folder_Under_Any_Spelling_Is_The_Plain_Pick_And_A_Subfolder_Is_Not()
    {
        foreach (var spelling in new[] { Profile + @"\Downloads", Profile.ToLowerInvariant() + @"\downloads\", Profile + @"/Downloads", Profile + @"\Other\..\Downloads", Profile + @"\Downloads\." })
        {
            var made = HandPicks.Folder(spelling, "folders", Context());
            Assert.True(made.Ok, made.Message);
            Assert.Equal("folder:downloads", made.Pick!.Id);
            Assert.Null(made.Pick.Location);
        }

        var sub = HandPicks.Folder(Profile + @"\Downloads\Sub", "folders", Context()).Pick!;
        Assert.Equal(@"%USERPROFILE%\Downloads\Sub", sub.Location);
        Assert.StartsWith("folder:", sub.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Holds_A_File_Pick_Is_Always_Bright_And_Missing_Is_Grey_And_Says_So()
    {
        var file = HandPicks.File(Profile + @"\Docs\notes.txt", "apps", Context()).Pick!;
        var cache = new PickTargetCache();
        var context = new PickContext(Profile, cache);
        var open = OpenSnapshot.Empty;

        var fresh = PickStates.For(file, open, context);
        Assert.False(PickItems.For(new PickRow(file, fresh), null).IsClosed);
        Assert.Equal("file", PickItems.Subtitle(file, fresh));

        cache.Record(file, Profile, exists: false);
        var missing = PickStates.For(file, open, context);
        Assert.True(missing.Missing);
        Assert.Equal("not found", PickItems.Subtitle(file, missing));
        Assert.True(PickItems.For(new PickRow(file, missing), null).IsClosed);
        Assert.Equal(ClickKind.TargetMissing, PickStates.Plan(file, missing, new ClickCycler()).Kind);

        cache.Record(file, Profile, exists: true);
        Assert.Equal(ClickKind.OpenFile, PickStates.Plan(file, PickStates.For(file, open, context), new ClickCycler()).Kind);
        Assert.Equal(ClickKind.OpenFile, PickStates.Plan(file, PickStatus.Unknown, new ClickCycler()).Kind);
    }

    [Fact]
    public void Holds_A_Folder_Added_By_Hand_Is_Open_When_An_Explorer_Window_Shows_Its_Place_Under_Any_Spelling()
    {
        var folder = HandPicks.Folder(Profile + @"\Docs", "folders", Context()).Pick!;
        foreach (var shown in new[] { Profile + @"\Docs", Profile.ToUpperInvariant() + @"\DOCS\", Profile + @"/Docs" })
        {
            var open = new OpenSnapshot([], [new FolderWindow(7, null, shown, 0)], [], false);
            var status = PickStates.For(folder, open, new PickContext(Profile));
            Assert.True(status.IsOpen, shown);
            Assert.Equal(ClickKind.BringForward, PickStates.Plan(folder, status, new ClickCycler()).Kind);
        }

        foreach (var notShown in new[] { Profile + @"\Docs\Sub", Profile + @"\Doc", @"Q:\Other\Docs", "::{INVENTED-GUID}", "", null })
        {
            var open = new OpenSnapshot([], [new FolderWindow(7, null, notShown, 0)], [], false);
            Assert.False(PickStates.For(folder, open, new PickContext(Profile)).IsOpen, notShown);
        }

        // without a profile folder known, the token cannot be put in memory: closed, never an exception
        Assert.False(PickStates.For(folder, new OpenSnapshot([], [new FolderWindow(7, null, Profile + @"\Docs", 0)], [], false), PickContext.None).IsOpen);
    }

    // ---- Scenes ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_A_Scene_Run_Does_Not_Know_The_Profile_Folder_So_A_Hand_Folder_Under_It_Is_Never_Brought_Forward()
    {
        // ScenePlans.For calls PickStates.For(thing, open) with no PickContext, so a stored %USERPROFILE%\Docs cannot be put back in memory: the folder window that IS open is not
        // found and a scene run opens the folder again (and counts it in "n opened") instead of bringing it forward.
        var folder = HandPicks.Folder(Profile + @"\Docs", Scenes.ThingPage, Context()).Pick!;
        var scene = new Scene("s1", "Scene", [folder]);
        var open = new OpenSnapshot([], [new FolderWindow(7, null, Profile + @"\Docs", 0)], [], false);

        var plan = ScenePlans.For(scene, open, _ => true, new PickContext(Profile)); // the scene run is given the profile folder, as the app gives it
        Assert.Equal(SceneAction.BringForward, plan.Steps[0].Action);
    }

    [Fact]
    public void Holds_Scenes_Round_Trip_Hand_Made_Things_With_The_Token_And_Without_The_Real_Profile()
    {
        var folder = HandPicks.Folder(Profile + @"\Docs", Scenes.ThingPage, Context()).Pick!;
        var file = HandPicks.File(@"Q:\Invented\Alpha\notes.txt", Scenes.ThingPage, Context()).Pick!;
        var program = HandPicks.BrowsedProgram(Profile + @"\Tools\alpha.exe", Scenes.ThingPage, Context()).Pick!;
        var store = new SceneStore([new Scene("s1", "Scene", [folder, file, program])]);
        var json = store.ToJson();
        Assert.DoesNotContain("Profile", json.Replace("%USERPROFILE%", string.Empty), StringComparison.Ordinal);
        var back = SceneStore.Parse(json);
        Assert.Equal(SceneStoreStatus.Loaded, back.Status);
        Assert.Equal([folder, file, program], back.Store.Items[0].Things);

        // and the same file written to disk
        using var temp = new TempFolder();
        Assert.True(store.Save(temp.File("scenes.json")));
        Assert.Equal([folder, file, program], SceneStore.Load(temp.File("scenes.json")).Store.Items[0].Things);
    }

    [Fact]
    public void Holds_A_Scene_Thing_With_A_Path_In_The_Wrong_Field_Makes_The_File_Unreadable_And_Nothing_Is_Half_Read()
    {
        var bad = """{"schema":1,"scenes":[{"id":"s1","name":"One","things":[{"id":"program:a","kind":"program","name":"A","page":"scene","exe":"Q:\\Invented\\a.exe"}]}]}""";
        Assert.Equal(SceneStoreStatus.Unreadable, SceneStore.Parse(bad).Status);
        var site = """{"schema":1,"scenes":[{"id":"s1","name":"One","things":[{"id":"site:a.org","kind":"site","name":"a.org","page":"scene","host":"a.org","location":"Q:\\Invented\\x"}]}]}""";
        Assert.Equal(SceneStoreStatus.Unreadable, SceneStore.Parse(site).Status);
    }

    // ---- Click plans ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Every_Kind_Has_A_Click_And_Missing_Wins_Over_Closed()
    {
        var picks = new[]
        {
            HandPicks.Folder(Profile + @"\Docs", "apps", Context()).Pick!,
            HandPicks.File(@"Q:\Invented\a.txt", "apps", Context()).Pick!,
            HandPicks.BrowsedProgram(@"Q:\Invented\a.exe", "apps", Context()).Pick!,
            HandPicks.BrowsedProgram(@"Q:\Invented\b.lnk", "apps", Context()).Pick!,
            HandPicks.Site("example.org", "apps").Pick!,
        };
        foreach (var pick in picks)
        foreach (var status in new[] { PickStatus.Closed, PickStatus.Unknown, new PickStatus(true, true, 1, [5]), new PickStatus(true, false, 0, [], Missing: true) })
        {
            var plan = PickStates.Plan(pick, status, new ClickCycler());
            Assert.NotEqual(ClickKind.None, plan.Kind);
            if (status.Missing) Assert.Equal(ClickKind.TargetMissing, plan.Kind);
        }
    }
}
