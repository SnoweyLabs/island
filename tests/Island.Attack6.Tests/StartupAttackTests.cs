using System.Text;
using Island.Core;

namespace Island.Attack6.Tests;

/// <summary>WO6 section 3: StartupSwitch against an in-memory registry only. Nothing here touches the real registry.</summary>
public class StartupAttackTests
{
    private const string Good = @"C:\Island\dist\Island\Island.App.exe";

    private static string PathOfLength(int length)
    {
        var sb = new StringBuilder(@"C:\a\");
        while (sb.Length < length - 4) sb.Append('x');
        sb.Append(".exe");
        return sb.ToString();
    }

    /// <summary>The Microsoft C runtime's rules for splitting a command line into arguments (what a started program sees).</summary>
    private static List<string> SplitLikeWindows(string line)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var any = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                var n = 0;
                while (i < line.Length && line[i] == '\\') { n++; i++; }
                if (i < line.Length && line[i] == '"')
                {
                    current.Append('\\', n / 2);
                    if (n % 2 == 1) { current.Append('"'); any = true; continue; }
                    i--; // the quote is processed as a delimiter next round
                    continue;
                }

                current.Append('\\', n);
                i--;
                any = true;
                continue;
            }

            if (c == '"') { inQuotes = !inQuotes; any = true; continue; }
            if (!inQuotes && (c == ' ' || c == '\t'))
            {
                if (any) { args.Add(current.ToString()); current.Clear(); any = false; }
                continue;
            }

            current.Append(c);
            any = true;
        }

        if (any) args.Add(current.ToString());
        return args;
    }

    // ---- the text written ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Island\dist\Island\Island.App.exe")]
    [InlineData(@"C:\Program Files\My Island\Island.App.exe")]
    [InlineData(@"C:\Users\Alpha\AppData\Local\Island\Island.App.exe")]
    [InlineData(@"C:\Üñï çödé\日本語\Island.App.exe")]
    [InlineData(@"\\server\share\Island\Island.App.exe")]
    [InlineData(@"\\?\C:\Island\Island.App.exe")]
    [InlineData(@"C:/Island/Island.App.exe")]
    [InlineData(@"D:\a b\c  d\e.exe")]
    [InlineData(@"C:\a\b.exe")]
    [InlineData(@"C:\a'b\c.exe")]
    [InlineData(@"C:\a&b^c(d)\e.exe")]
    [InlineData(@"C:\100%\e.exe")]
    public void Holds_A_Command_Line_Splits_Into_Exactly_The_Path_And_The_Autostart_Argument(string path)
    {
        var line = new StartupSwitch(new MemoryStartupRegistry(), path).CommandLine;
        Assert.NotNull(line);
        Assert.Equal($"\"{path}\" --autostart", line);
        var args = SplitLikeWindows(line!);
        Assert.Equal([path, "--autostart"], args);
        Assert.True(StartupSwitch.IsAutostartLaunch(args.Skip(1).ToList()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("Island.App.exe")]
    [InlineData(@"dist\Island.App.exe")]
    [InlineData(@".\Island.App.exe")]
    [InlineData(@"..\Island.App.exe")]
    [InlineData(@"\Island.App.exe")]
    [InlineData(@"C:Island.App.exe")]
    [InlineData("C:\\a\"b\\c.exe")]
    [InlineData("\"C:\\a\\b.exe\"")]
    [InlineData("C:\\a\\b.exe\"")]
    [InlineData("C:\\a\\b.exe\n")]
    [InlineData("C:\\a\n\\b.exe")]
    [InlineData("C:\\a\r\\b.exe")]
    [InlineData("C:\\a\0\\b.exe")]
    [InlineData("C:\\a\t\\b.exe")]
    [InlineData("C:\\a\u007F\\b.exe")]
    [InlineData("C:\\a\u0085\\b.exe")]
    [InlineData(@"C:\a\b\")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\a\b/")]
    public void Holds_A_Path_That_Cannot_Be_Written_Safely_Is_Not_Written_And_Has_No_Command_Line(string path)
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, path);
        Assert.Null(sw.CommandLine);
        Assert.False(sw.IsOn());
        Assert.False(sw.TurnOn().Ok);
        Assert.Equal(0, registry.Writes);
        Assert.Null(registry.Value);
        Assert.True(sw.TurnOff().Ok);
        Assert.Equal(0, registry.Removes);
    }

    [Fact]
    public void Holds_A_Null_Path_Is_Handled_As_Not_Writable()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, null!);
        Assert.Null(sw.CommandLine);
        Assert.False(sw.TurnOn().Ok);
        Assert.Equal(0, registry.Writes);
    }

    // ---- the length -------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Whole_Command_Line_Of_260_Characters_Is_Written_And_261_Is_Refused_With_The_Reason_And_Nothing_Is_Written()
    {
        var suffix = "\"\"".Length + 1 + StartupSwitch.AutostartArgument.Length; // two quotes, a space, the argument
        var longest = PathOfLength(StartupKey.MaxCommandLineLength - suffix);
        var registry = new MemoryStartupRegistry();
        var ok = new StartupSwitch(registry, longest);
        Assert.Equal(StartupKey.MaxCommandLineLength, ok.CommandLine!.Length);
        Assert.True(ok.TurnOn().Ok);
        Assert.Equal(1, registry.Writes);
        Assert.True(ok.IsOn());

        var tooLong = new MemoryStartupRegistry();
        var over = new StartupSwitch(tooLong, PathOfLength(StartupKey.MaxCommandLineLength - suffix + 1));
        Assert.Equal(StartupKey.MaxCommandLineLength + 1, over.CommandLine!.Length);
        var result = over.TurnOn();
        Assert.False(result.Ok);
        Assert.Same(StartupRefusals.PathTooLong, result.Refusal);
        Assert.Equal("STARTUP_PATH_TOO_LONG", result.Refusal!.Code);
        Assert.Equal(0, tooLong.Writes);
        Assert.Null(tooLong.Value);
    }

    [Fact]
    public void Holds_A_Path_Of_Exactly_260_And_261_Characters_Alone_Is_Too_Long_Because_The_Line_Is_Longer()
    {
        foreach (var length in new[] { 260, 261 })
        {
            var registry = new MemoryStartupRegistry();
            var result = new StartupSwitch(registry, PathOfLength(length)).TurnOn();
            Assert.False(result.Ok);
            Assert.NotNull(result.Refusal);
            Assert.Equal(0, registry.Writes);
        }
    }

    [Fact]
    public void Holds_A_Huge_Path_Is_Refused_Not_Written_And_Does_Not_Throw()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, PathOfLength(1_000_000));
        Assert.Same(StartupRefusals.PathTooLong, sw.TurnOn().Refusal);
        Assert.False(sw.IsOn());
        Assert.True(sw.TurnOff().Ok);
        Assert.Equal(0, registry.Writes + registry.Removes);
    }

    [Fact]
    public void Holds_Unicode_Counts_In_Characters_As_The_Page_Says_Not_In_Bytes()
    {
        var suffix = 2 + 1 + StartupSwitch.AutostartArgument.Length;
        var sb = new StringBuilder(@"C:\");
        while (sb.Length < StartupKey.MaxCommandLineLength - suffix - 4) sb.Append('\u00E9');
        sb.Append(".exe");
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, sb.ToString());
        Assert.Equal(StartupKey.MaxCommandLineLength, sw.CommandLine!.Length);
        Assert.True(sw.TurnOn().Ok);
        Assert.True(Encoding.UTF8.GetByteCount(sw.CommandLine) > StartupKey.MaxCommandLineLength); // more bytes than characters, still accepted
    }

    // ---- every order of on, off, is-on ---------------------------------------------------------------------------------

    [Fact]
    public void Holds_Every_Order_Of_On_Off_And_IsOn_Matches_A_Simple_Model()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, Good);
        var rng = new Random(11);
        var on = false;
        for (var i = 0; i < 20_000; i++)
        {
            switch (rng.Next(3))
            {
                case 0: Assert.True(sw.TurnOn().Ok); on = true; break;
                case 1: Assert.True(sw.TurnOff().Ok); on = false; break;
                default: Assert.Equal(on, sw.IsOn()); break;
            }

            Assert.Equal(on, registry.Value is not null);
            if (on) Assert.Equal(sw.CommandLine, registry.Value);
        }

        Assert.True(registry.Writes < 20_000 && registry.Removes < 20_000);
    }

    [Fact]
    public void Holds_Turning_On_Twice_Writes_Once_And_Turning_Off_Twice_Removes_Once()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, Good);
        sw.TurnOn();
        sw.TurnOn();
        Assert.Equal(1, registry.Writes);
        sw.TurnOff();
        sw.TurnOff();
        Assert.Equal(1, registry.Removes);
    }

    [Fact]
    public void Holds_Nothing_Writes_At_Launch_Or_When_Asking()
    {
        var registry = new MemoryStartupRegistry(@"""C:\Elsewhere\Island.App.exe"" --autostart");
        var sw = new StartupSwitch(registry, Good);
        _ = sw.CommandLine;
        _ = sw.IsOn();
        _ = StartupSwitch.IsAutostartLaunch(["--autostart"]);
        _ = StartupSwitch.SummonsAtStart([]);
        Assert.Equal(0, registry.Writes);
        Assert.Equal(0, registry.Removes);
    }

    [Fact]
    public void Holds_A_Registry_That_Refuses_Changes_Fails_Without_Lying_And_Leaves_What_Was_There()
    {
        var registry = new MemoryStartupRegistry { RefuseChanges = true };
        var sw = new StartupSwitch(registry, Good);
        var on = sw.TurnOn();
        Assert.False(on.Ok);
        Assert.Null(on.Refusal);
        Assert.False(sw.IsOn());

        registry.RefuseChanges = false;
        sw.TurnOn();
        registry.RefuseChanges = true;
        var off = sw.TurnOff();
        Assert.False(off.Ok);
        Assert.True(sw.IsOn()); // it is still there, and the switch says so
    }

    // ---- a stored value that is not ours --------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData(@"""C:\Elsewhere\Island.App.exe"" --autostart")]
    [InlineData(@"C:\Island\dist\Island\Island.App.exe --autostart")]
    [InlineData(@"""C:\Island\dist\Island\Island.App.exe""")]
    [InlineData(@"""C:\Island\dist\Island\Island.App.exe"" --autostart ")]
    [InlineData(@"""C:\Island\dist\Island\Island.App.exe""  --autostart")]
    public void Holds_A_Value_That_Is_Not_Ours_Reads_As_Off_And_Turning_Off_Leaves_It(string stored)
    {
        var registry = new MemoryStartupRegistry(stored);
        var sw = new StartupSwitch(registry, Good);
        Assert.False(sw.IsOn());
        Assert.True(sw.TurnOff().Ok);
        Assert.Equal(0, registry.Removes);
        Assert.Equal(stored, registry.Value);
    }

    [Fact]
    public void Holds_Turning_On_Replaces_A_Value_That_Is_Not_Ours_With_Ours_And_Turning_Off_Then_Clears_It()
    {
        var registry = new MemoryStartupRegistry(@"""C:\Elsewhere\Island.App.exe"" --autostart");
        var sw = new StartupSwitch(registry, Good);
        Assert.True(sw.TurnOn().Ok);
        Assert.Equal(sw.CommandLine, registry.Value);
        Assert.True(sw.TurnOff().Ok);
        Assert.Null(registry.Value);
    }

    [Fact]
    public void Holds_A_Value_That_Differs_Only_In_Letter_Case_Is_Ours_As_Windows_Paths_Are_Not_Case_Sensitive()
    {
        var registry = new MemoryStartupRegistry(@"""c:\ISLAND\dist\island\ISLAND.APP.EXE"" --AUTOSTART");
        var sw = new StartupSwitch(registry, Good);
        Assert.True(sw.IsOn());
        Assert.True(sw.TurnOff().Ok);
        Assert.Null(registry.Value);
    }

    // ---- the launch argument ---------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Only_The_Exact_Argument_Makes_A_Silent_Start()
    {
        Assert.True(StartupSwitch.IsAutostartLaunch(["--autostart"]));
        Assert.True(StartupSwitch.IsAutostartLaunch(["--selftest", "--autostart"]));
        Assert.False(StartupSwitch.SummonsAtStart(["--autostart"]));
        foreach (var args in new[] { Array.Empty<string>(), ["autostart"], ["-autostart"], ["--autostart=1"], [" --autostart"], ["--autostart "], ["--AUTOSTART"], ["--auto-start"], [""], ["--autostart\0"] })
        {
            Assert.False(StartupSwitch.IsAutostartLaunch(args), string.Join("|", args));
            Assert.True(StartupSwitch.SummonsAtStart(args));
        }

        Assert.True(StartupSwitch.IsAutostartLaunch([null!, "--autostart"]));
    }

    // ---- the switch inside the settings session ------------------------------------------------------------------------------

    private static (Island.Core.SettingsEdit.SettingsSession Session, MemoryStartupRegistry Registry, TempFolder Temp) Session(string path = Good)
    {
        var temp = new TempFolder();
        var registry = new MemoryStartupRegistry();
        var files = temp.Files();
        Settings.Defaults.Save(files.SettingsPath);
        var session = new Island.Core.SettingsEdit.SettingsSession(
            files,
            new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
            new Island.Core.SettingsEdit.PageStoreLoad(Island.Core.SettingsEdit.PageStore.Default, Island.Core.SettingsEdit.PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null),
            new PretendRegistrar(),
            () => [],
            () => false,
            new StartupSwitch(registry, path));
        return (session, registry, temp);
    }

    [Fact]
    public void Holds_The_Session_Switch_Writes_Once_Shows_The_Registry_And_Keeps_Only_Yes_Or_No_In_The_File()
    {
        var (session, registry, temp) = Session();
        using var _ = temp;
        Assert.True(session.StartupAvailable);
        Assert.False(session.StartWithWindowsOn);
        Assert.True(session.SetStartWithWindows(true).Changed);
        Assert.True(session.StartWithWindowsOn);
        Assert.Equal(1, registry.Writes);
        var file = File.ReadAllText(temp.File("settings.json"));
        Assert.Contains("\"startWithWindows\": true", file);
        Assert.DoesNotContain("Island.App.exe", file);
        Assert.DoesNotContain("--autostart", file);

        registry.Remove(); // somebody removes it behind the app's back: the switch shows the registry, not the file
        Assert.False(session.StartWithWindowsOn);
        Assert.True(Settings.Load(temp.File("settings.json")).Settings.StartWithWindows);
        Assert.Equal(1, registry.Writes); // and nothing repaired it
    }

    [Fact]
    public void Holds_A_Too_Long_Path_Is_Refused_In_The_Session_With_Its_Words_And_The_File_Is_Untouched()
    {
        var (session, registry, temp) = Session(PathOfLength(300));
        using var _ = temp;
        var before = File.ReadAllText(temp.File("settings.json"));
        var result = session.SetStartWithWindows(true);
        Assert.False(result.Ok);
        Assert.Contains("too long", result.Refusal);
        Assert.Equal(0, registry.Writes);
        Assert.Equal(before, File.ReadAllText(temp.File("settings.json")));
        Assert.False(session.Settings.StartWithWindows);
    }

    [Fact]
    public void Holds_A_Registry_That_Fails_Leaves_The_Setting_Off_And_Says_So()
    {
        var (session, registry, temp) = Session();
        using var _ = temp;
        registry.RefuseChanges = true;
        var result = session.SetStartWithWindows(true);
        Assert.False(result.Ok);
        Assert.False(session.Settings.StartWithWindows);
        Assert.False(Settings.Load(temp.File("settings.json")).Settings.StartWithWindows);
    }

    [Fact]
    public void Defect_Start_With_Windows_Says_Nothing_Was_Changed_But_The_Registry_Was_Written_When_The_Settings_File_Cannot_Be_Saved()
    {
        // The registry is written first and the settings file after; when the file cannot be saved the answer is "so nothing was changed",
        // yet Windows now starts the island at every sign-in and the switch (which reads the registry) shows on.
        var (session, registry, temp) = Session();
        using var _ = temp;
        File.SetAttributes(temp.File("settings.json"), FileAttributes.ReadOnly);

        var result = session.SetStartWithWindows(true);

        Assert.False(result.Ok);
        Assert.Contains("nothing was changed", result.Refusal);
        Assert.Null(registry.Value); // so the registry must not hold the value either
        Assert.False(session.StartWithWindowsOn);
    }

    [Fact]
    public void Holds_Turning_It_Off_Removes_The_Value_And_Saves_No()
    {
        var (session, registry, temp) = Session();
        using var _ = temp;
        session.SetStartWithWindows(true);
        Assert.True(session.SetStartWithWindows(false).Changed);
        Assert.Null(registry.Value);
        Assert.False(Settings.Load(temp.File("settings.json")).Settings.StartWithWindows);
        Assert.False(session.SetStartWithWindows(false).Changed);
    }

    [Fact]
    public void Holds_A_Session_Without_A_Switch_And_One_On_A_Locked_File_Write_Nothing()
    {
        using var temp = new TempFolder();
        var files = temp.Files();
        var none = new Island.Core.SettingsEdit.SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
            new Island.Core.SettingsEdit.PageStoreLoad(Island.Core.SettingsEdit.PageStore.Default, Island.Core.SettingsEdit.PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null), new PretendRegistrar(), () => [], () => false);
        Assert.False(none.StartupAvailable);
        Assert.False(none.StartWithWindowsOn);
        Assert.False(none.SetStartWithWindows(true).Ok);

        var registry = new MemoryStartupRegistry();
        var locked = new Island.Core.SettingsEdit.SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Unreadable, "x"),
            new Island.Core.SettingsEdit.PageStoreLoad(Island.Core.SettingsEdit.PageStore.Default, Island.Core.SettingsEdit.PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null), new PretendRegistrar(), () => [], () => false, new StartupSwitch(registry, Good));
        Assert.False(locked.SetStartWithWindows(true).Ok);
        Assert.Equal(0, registry.Writes);
    }
}
