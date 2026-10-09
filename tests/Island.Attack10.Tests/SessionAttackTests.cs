using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack10.Tests;

/// <summary>SettingsSession "Add..." attacked: a locked file, an unknown page, a pretend chooser that answers anything, a file that cannot be written, messages and files that must hold no path.</summary>
public class SessionAttackTests
{
    private const string Marker = "ZZmarkerZZ";

    private static string Real(string text) => System.IO.File.Exists(text) ? System.IO.File.ReadAllText(text) : string.Empty;

    // ---- Defects ----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_A_Choosing_Window_Is_Opened_Before_The_Session_Looks_At_Its_Own_Locks()
    {
        // The session asks the chooser first and checks the lock and the page afterwards. With a picks file that could not be read (every edit refused) or a page that is gone,
        // Windows' window opens, the person picks something, and only then hears "nothing was added".
        using var locked = new TempFolder();
        var chooser = new PretendChooser(file: @"Q:\Invented\a.txt", folder: @"Q:\Invented\Alpha");
        var session = Make.Session(locked, picksUnreadable: true, chooser: chooser);
        session.AddFolder("apps");
        session.AddFile("apps");
        session.AddProgramByBrowsing("apps");
        Assert.Equal(0, chooser.Asked);

        using var gone = new TempFolder();
        var chooser2 = new PretendChooser(file: @"Q:\Invented\a.txt", folder: @"Q:\Invented\Alpha");
        var session2 = Make.Session(gone, chooser: chooser2);
        session2.AddFolder("no-such-page");
        Assert.Equal(0, chooser2.Asked);
    }

    [Fact]
    public void Defect_A_Chooser_That_Throws_Escapes_The_Session()
    {
        // A shell window can fail (the owner window closed, the shell busy). Nothing in AddFolder, AddFile or AddProgramByBrowsing catches it, so it climbs into the screen's handler.
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: new ThrowingChooser());
        var result = Record.Exception(() => session.AddFolder("apps"));
        Assert.Null(result);
        Assert.Null(Record.Exception(() => session.AddFile("apps")));
        Assert.Null(Record.Exception(() => session.AddProgramByBrowsing("apps")));
    }

    [Fact]
    public void Defect_A_Spelled_Out_Profile_Path_Loaded_From_The_File_Is_Written_Back_As_It_Is()
    {
        // PickStore.Save has a profileFolder argument that refuses a place that "would carry the Windows account name into the file", but the session's one save call
        // (ApplyPicks) never passes it, and a load does not bring a spelled-out place to the token. So after any later edit the file still holds the profile folder spelled out.
        using var temp = new TempFolder();
        var spelled = new Pick("folder:0000000000000001", PickKind.Folder, "Docs", "apps", Location: Make.Profile + @"\Docs");
        var session = Make.Session(temp, [spelled]);
        var added = session.AddSite("apps", "example.org");
        Assert.True(added.Added);
        Assert.DoesNotContain("Invented", Real(temp.File("picks.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Defect_A_Session_Given_A_Chooser_But_No_Hand_Context_Writes_The_Profile_Path_Spelled_Out()
    {
        // LATENT: the app always sets HandContextSource (AppHost), so nothing reaches this today. The fallback is HandContext(null, []): no profile folder, so nothing is compressed
        // and the account name goes into the file. The rule about paths wants the opposite default: with no profile folder known, refuse to store a place under one.
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: new PretendChooser(folder: Make.Profile + @"\Docs"));
        session.HandContextSource = null;
        var result = session.AddFolder("apps");
        Assert.DoesNotContain("Invented", Real(temp.File("picks.json")), StringComparison.Ordinal);
        Assert.True(result.Added || result.Message is not null);
    }

    // ---- Holds ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Locked_Picks_File_Refuses_Every_Way_Of_Adding_And_Writes_Nothing()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp, picksUnreadable: true, chooser: new PretendChooser(file: @"Q:\Invented\a.txt", folder: @"Q:\Invented\Alpha"),
            installed: [new InstalledProgram("Alpha", "alpha.exe", null, "x")]);
        var results = new[]
        {
            session.AddFolder("apps"), session.AddFile("apps"), session.AddProgramByBrowsing("apps"), session.AddSite("apps", "example.org"),
            session.AddProgramFromList("apps", new InstalledProgram("Alpha", "alpha.exe", null, "x")),
        };
        Assert.All(results, r =>
        {
            Assert.False(r.Added);
            Assert.False(string.IsNullOrWhiteSpace(r.Message));
            Assert.False(r.Cancelled);
        });
        Assert.False(System.IO.File.Exists(temp.File("picks.json")));
        Assert.Empty(session.Picks.Picks);
    }

    [Fact]
    public void Holds_An_Unknown_Page_Is_Refused_And_Writes_Nothing()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: new PretendChooser(folder: @"Q:\Invented\Alpha"));
        var before = Real(temp.File("picks.json"));
        foreach (var page in new[] { "", " ", "nope", "APPS ", "apps\0", new string('p', 10_000), "../apps" })
        {
            var result = session.AddSite(page, "example.org");
            Assert.False(result.Added);
            Assert.Equal(SettingsText.NoSuchPage, result.Message);
        }

        Assert.Equal(before, Real(temp.File("picks.json")));
        Assert.Null(Record.Exception(() => session.AddProgramFromList(null!, new InstalledProgram("Alpha", "alpha.exe", null, "x")).Added));
    }

    public static IEnumerable<object[]> ChooserAnswers =>
    [
        [null], [""], [" "], ["\t"], ["hello"], ["Q:"], [@"Q:\" + Marker + @"\CON"], [@"\\" + Marker + @"\share\x"], [@"Q:\" + Marker + @"\a:b"], [@"Q:\" + Marker + @"\..\..\..\x"], ["http://" + Marker + ".org/x"],
        ["Q:" + Marker], [@"\\?\Q:\" + Marker], [@"Q:\" + Marker + "\n"], [@"Q:\" + Marker + "\0"], [@"Q:\" + new string('m', 3000) + Marker], ["Q:\\" + Marker + new string('x', 1 << 20)], [Marker], [@"%USERPROFILE%\" + Marker],
        ["\uFEFF" + Marker], [@"Q:\" + Marker + @"\<>"],
    ];

    [Theory]
    [MemberData(nameof(ChooserAnswers))]
    public void Holds_A_Chooser_That_Answers_Garbage_Adds_Nothing_And_No_Message_Holds_The_Path(string? answer)
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: new PretendChooser(file: answer, folder: answer));
        var before = Real(temp.File("picks.json"));
        var results = new[] { session.AddFolder("apps"), session.AddFile("apps"), session.AddProgramByBrowsing("apps") };
        for (var i = 0; i < results.Length; i++)
        {
            var r = results[i];
            if (answer is null)
            {
                Assert.True(r.Cancelled);
                Assert.False(r.Added);
                continue;
            }

            Assert.False(r.Added, $"{i}: {answer.Length} chars");
            Assert.False(r.Cancelled);
            Assert.False(string.IsNullOrWhiteSpace(r.Message));
            Assert.DoesNotContain(Marker, r.Message!, StringComparison.Ordinal);
            Assert.DoesNotContain(@"Q:\", r.Message!, StringComparison.Ordinal);
            Assert.DoesNotContain("://", r.Message!, StringComparison.Ordinal);
        }

        Assert.Equal(before, Real(temp.File("picks.json")));
        Assert.Empty(session.Picks.Picks);
    }

    [Fact]
    public void Holds_Typed_Garbage_For_A_Site_Gives_The_Registers_Words_And_No_Typed_Text()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp);
        foreach (var typed in new[] { Marker, "http://" + Marker, Marker + " " + Marker + ".", "exa mple" + Marker + ".org", Marker + "\0.org", new string('z', 1 << 20) + Marker })
        {
            var result = session.AddSite("browser", typed);
            Assert.False(result.Added);
            Assert.Equal(HandPickRefusals.NotASite.Message, result.Message);
            Assert.DoesNotContain(Marker, session.UnderstandSite(typed).Refusal!.Message, StringComparison.Ordinal);
        }

        Assert.Equal("example.org", session.UnderstandSite("https://www.Example.org/page").Host);
        Assert.Null(session.UnderstandSite(null).Host);
    }

    [Fact]
    public void Holds_A_File_That_Cannot_Be_Written_Refuses_Says_So_Without_A_Path_And_Changes_Nothing()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: new PretendChooser(folder: @"Q:\Invented\" + Marker));
        var raised = 0;
        session.Changed += _ => raised++;
        var before = Real(temp.File("picks.json"));
        new FileInfo(temp.File("picks.json")).Attributes |= FileAttributes.ReadOnly;

        var result = session.AddFolder("apps");
        Assert.False(result.Added);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.DoesNotContain(Marker, result.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Root, result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"Q:\", result.Message!, StringComparison.Ordinal);
        Assert.Empty(session.Picks.Picks);
        Assert.Equal(0, raised);
        Assert.Equal(before, Real(temp.File("picks.json")));
        Assert.False(System.IO.File.Exists(temp.File("picks.json.tmp")));

        // the same with a directory sitting where the temporary file goes
        new FileInfo(temp.File("picks.json")).Attributes = FileAttributes.Normal;
        Directory.CreateDirectory(temp.File("picks.json.tmp"));
        var second = session.AddSite("apps", "example.org");
        Assert.False(second.Added);
        Assert.Empty(session.Picks.Picks);
        Directory.Delete(temp.File("picks.json.tmp"));

        // and it works again once the file can be written
        Assert.True(session.AddSite("apps", "example.org").Added);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Holds_The_Same_Folder_Under_Two_Spellings_Says_Where_It_Already_Is()
    {
        using var temp = new TempFolder();
        var chooser = new PretendChooser(folder: @"Q:\Invented\Alpha");
        var session = Make.Session(temp, chooser: chooser);
        var raised = 0;
        session.Changed += _ => raised++;

        Assert.True(session.AddFolder("apps").Added);
        foreach (var spelling in new[] { @"q:\invented\alpha", @"Q:\Invented\Alpha\", @"Q:/Invented/Alpha", @"Q:\Invented\.\Alpha", @"Q:\Invented\Beta\..\Alpha", @"Q:\INVENTED\ALPHA." })
        {
            chooser.Folder = spelling;
            var again = session.AddFolder("folders");
            Assert.False(again.Added, spelling);
            Assert.Equal("Apps", again.AlreadyOn);
            Assert.Contains("already on the island", again.Message!, StringComparison.Ordinal);
        }

        Assert.Single(session.Picks.Picks);
        Assert.Equal(1, raised);

        // a file under two spellings, and a site under two
        chooser.File = @"Q:\Invented\Alpha\notes.txt";
        Assert.True(session.AddFile("apps").Added);
        chooser.File = @"q:/invented/alpha/NOTES.TXT";
        Assert.False(session.AddFile("apps").Added);
        Assert.True(session.AddSite("browser", "example.org").Added);
        Assert.False(session.AddSite("apps", "https://www.EXAMPLE.org:443/x").Added);
    }

    [Fact]
    public void Holds_Nothing_Written_Holds_The_Real_Profile_Folder_Or_Another_Path_Than_The_One_Field()
    {
        using var temp = new TempFolder();
        var chooser = new PretendChooser(folder: Make.Profile + @"\Docs\Sub", file: Make.Profile + @"\Docs\notes.txt");
        var session = Make.Session(temp, chooser: chooser);
        Assert.True(session.AddFolder("apps").Added);
        Assert.True(session.AddFile("apps").Added);
        chooser.File = Make.Profile + @"\Tools\Alpha.exe";
        Assert.True(session.AddProgramByBrowsing("apps").Added);
        chooser.Folder = Make.Profile;
        Assert.True(session.AddFolder("apps").Added);

        foreach (var name in new[] { "picks.json", "settings.json", "pages.json" })
        {
            var text = Real(temp.File(name));
            Assert.DoesNotContain("Invented", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Profile\\\\", text, StringComparison.Ordinal);
            Assert.DoesNotContain(temp.Root, text, StringComparison.OrdinalIgnoreCase);
        }

        var picks = Real(temp.File("picks.json"));
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(picks, "%USERPROFILE%").Count);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(picks, "\"location\"").Count);
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Load(temp.File("picks.json")).Status);
        Assert.Equal(session.Picks.Picks, PickStore.Load(temp.File("picks.json")).Store.Picks);
    }

    [Fact]
    public void Holds_A_Program_Browsed_To_Twice_Is_Refused_The_Second_Time_Under_Any_Spelling()
    {
        using var temp = new TempFolder();
        var chooser = new PretendChooser(file: @"Q:\Invented\Alpha\alpha.exe");
        var session = Make.Session(temp, chooser: chooser);
        Assert.True(session.AddProgramByBrowsing("apps").Added);
        chooser.File = @"Q:/INVENTED/alpha/ALPHA.EXE";
        Assert.False(session.AddProgramByBrowsing("vibe").Added);
        Assert.Single(session.Picks.Picks);
    }

    [Fact]
    public void Holds_The_List_Of_Programs_Survives_Odd_Filters()
    {
        using var temp = new TempFolder();
        var installed = new[] { new InstalledProgram("Alpha Edit", "alpha.exe", null, "x"), new InstalledProgram("beta (x64)", "beta.exe", null, "y"), new InstalledProgram("\u00C9cole", null, "Pkg_1", "z") };
        var session = Make.Session(temp, installed: installed);
        Assert.Equal(3, session.ProgramsToChoose(null).Count);
        Assert.Equal(3, session.ProgramsToChoose("").Count);
        Assert.Equal(3, session.ProgramsToChoose("   ").Count);
        Assert.Single(session.ProgramsToChoose("ALPHA"));
        Assert.Single(session.ProgramsToChoose("(x64)"));
        Assert.Single(session.ProgramsToChoose("\u00E9COLE")); // case is ignored for letters outside ASCII too
        foreach (var odd in new[] { "*", "?", "\\", "[", ".*", "%", "\0", new string('a', 1 << 20), ((char)0xD800).ToString(), "\u200B" })
            Assert.Null(Record.Exception(() => session.ProgramsToChoose(odd)));
        Assert.Equal(["Alpha Edit", "beta (x64)", "\u00C9cole"], session.ProgramsToChoose(null).Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Holds_Programs_From_The_List_That_Cannot_Be_A_Pick_Are_Refused_With_Words()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp);
        var odd = new[]
        {
            new InstalledProgram("No Names", null, null, "x"),
            new InstalledProgram(new string('n', 300), "long.exe", null, "x"),
            new InstalledProgram("Back\\slash", "bs.exe", null, "x"),
            new InstalledProgram("Not exe", "tool.bat", null, "x"),
            new InstalledProgram("Slash exe", "a/b.exe", null, "x"),
            new InstalledProgram("Spaces", "my tool.exe", null, "x"),
            new InstalledProgram(" ", "blank.exe", null, "x"),
            new InstalledProgram("Pkg", null, "Contoso.App_8wekyb3d8bbwe", "x"),
        };
        var results = odd.Select(p => session.AddProgramFromList("apps", p)).ToList();
        Assert.False(results[0].Added);
        Assert.False(results[1].Added);
        Assert.False(results[2].Added);
        Assert.False(results[3].Added);
        Assert.False(results[4].Added);
        Assert.False(results[6].Added);
        Assert.All(results.Where(r => !r.Added), r => Assert.False(string.IsNullOrWhiteSpace(r.Message)));
        Assert.DoesNotContain(results.Where(r => !r.Added), r => r.Message!.Contains("Q:", StringComparison.Ordinal));
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Load(temp.File("picks.json")).Status);
        Assert.True(results[5].Added || results[5].Message is not null);
        Assert.True(results[7].Added, results[7].Message);
    }

    [Fact]
    public void Holds_The_Same_Program_From_The_List_Twice_Says_Where_It_Is()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp);
        var program = new InstalledProgram("Alpha", "alpha.exe", null, "x");
        Assert.True(session.AddProgramFromList("apps", program).Added);
        var again = session.AddProgramFromList("vibe", new InstalledProgram("Alpha again", "ALPHA.EXE", null, "y"));
        Assert.False(again.Added);
        Assert.Equal("Apps", again.AlreadyOn);
    }

    [Fact]
    public void Holds_A_Full_Island_Refuses_The_Next_Add_And_The_File_Stays_Loadable()
    {
        using var temp = new TempFolder();
        var thousand = Enumerable.Range(0, PickStore.MaxPicks).Select(i => new Pick($"folder:{i:x16}", PickKind.Folder, "F" + i, "apps", Location: @"Q:\Invented\F" + i));
        var session = Make.Session(temp, thousand);
        var result = session.AddSite("apps", "example.org");
        Assert.False(result.Added);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(PickStoreStatus.Loaded, PickStore.Load(temp.File("picks.json")).Status);
        Assert.Equal(PickStore.MaxPicks, PickStore.Load(temp.File("picks.json")).Store.Picks.Count);
    }

    [Fact]
    public void Holds_A_Successful_Add_Raises_Changed_Once_And_Is_On_The_Disk()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: new PretendChooser(folder: @"Q:\Invented\Alpha", file: @"Q:\Invented\a.txt"));
        var areas = new List<SettingsArea>();
        session.Changed += areas.Add;
        var one = session.AddFolder("apps");
        var two = session.AddFile("folders");
        var three = session.AddSite("browser", "example.org");
        Assert.All(new[] { one, two, three }, r => Assert.True(r.Added, r.Message));
        Assert.Equal([SettingsArea.Picks, SettingsArea.Picks, SettingsArea.Picks], areas);
        var load = PickStore.Load(temp.File("picks.json"));
        Assert.Equal(session.Picks.Picks, load.Store.Picks);
        Assert.Equal(["apps", "folders", "browser"], load.Store.Picks.Select(p => p.PageId).ToArray());
    }

    [Fact]
    public void Holds_No_Chooser_Set_Answers_That_None_Is_Available()
    {
        using var temp = new TempFolder();
        var session = Make.Session(temp, chooser: null);
        foreach (var result in new[] { session.AddFolder("apps"), session.AddFile("apps"), session.AddProgramByBrowsing("apps") })
        {
            Assert.False(result.Added);
            Assert.Equal(SettingsText.NoChoosingWindow, result.Message);
        }
    }
}
