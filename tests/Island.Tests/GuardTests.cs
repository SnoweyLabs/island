using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Island.Tests;

public class GuardTests
{
    private static readonly string[] Forbidden =
        ["SetWindowsHookEx", "SendInput", "SendKeys", "keybd_event"];

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => p is "bin" or "obj");

    [Fact]
    public void No_Keyboard_Hook_Or_Fake_Input_In_Source()
    {
        var files = new[] { "src", "tools" }
            .Select(d => Path.Combine(RepoPaths.Root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(f))
            .ToList();

        Assert.NotEmpty(files);
        var hits = from f in files
                   let text = File.ReadAllText(f)
                   from word in Forbidden
                   where text.Contains(word, StringComparison.OrdinalIgnoreCase)
                   select $"{Path.GetRelativePath(RepoPaths.Root, f)}: {word}";
        Assert.Empty(hits);
    }

    [Fact]
    public void No_Account_Name_In_Source()
    {
        var name = Environment.GetEnvironmentVariable("USERNAME");
        if (string.IsNullOrWhiteSpace(name)) return; // nothing to compare against

        var hits = TrackedTextFiles()
            .Where(f => File.ReadAllText(f).Contains(name, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(RepoPaths.Root, f))
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Guard_Scans_Find_Files()
    {
        Assert.NotEmpty(TrackedTextFiles());
    }

    /// <summary>Files git tracks (or, with no git, the tracked kinds), minus build output; images skipped.</summary>
    private static List<string> TrackedTextFiles()
    {
        var listed = GitLsFiles() ?? WalkTrackedKinds();
        return listed
            .Where(f => !IsBuildOutput(f))
            .Where(f => !f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .ToList();
    }

    private static List<string>? GitLsFiles()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "ls-files -z")
            {
                WorkingDirectory = RepoPaths.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return null;
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(rel => Path.Combine(RepoPaths.Root, rel))
                .ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static List<string> WalkTrackedKinds() =>
        new[] { "src", "tests", "tools", "reference", "review" }
            .Select(d => Path.Combine(RepoPaths.Root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .ToList();

    // The members of the registry classes that create, set or delete a key or a value (Microsoft Learn, RegistryKey), and the native calls.
    private static readonly string[] RegistryWriters =
        ["CreateSubKey", "SetValue", "DeleteValue", "DeleteSubKey", "DeleteSubKeyTree", "RegSetValueEx", "RegDeleteValue", "RegCreateKeyEx"];

    [Fact]
    public void Registry_Is_Written_In_One_Outside_File()
    {
        // A file is about the registry when it names the registry classes or the native registry calls; framework calls with the same
        // names on other things (a dependency property's SetValue) are not the registry and are not looked at.
        static bool AboutRegistry(string code) =>
            code.Contains("RegistryKey", StringComparison.Ordinal) || code.Contains("Registry.", StringComparison.Ordinal)
            || code.Contains("Microsoft.Win32", StringComparison.Ordinal) || code.Contains("RegSetValueEx", StringComparison.Ordinal)
            || code.Contains("advapi32", StringComparison.OrdinalIgnoreCase);

        var writers = new List<string>();
        foreach (var f in OutsideGuardTests.SourceFiles())
        {
            var code = OutsideGuardTests.StripComments(File.ReadAllText(f));
            if (!AboutRegistry(code)) continue;
            if (RegistryWriters.Any(w => System.Text.RegularExpressions.Regex.IsMatch(code, @"(?<![A-Za-z0-9_])" + w + @"(?![A-Za-z0-9_])")))
                writers.Add(f);
        }

        var names = writers.Select(f => Path.GetRelativePath(RepoPaths.Root, f)).ToList();
        Assert.Single(names); // exactly one file writes the registry...
        Assert.StartsWith("Outside", Path.GetFileName(writers[0]), StringComparison.Ordinal); // ...its name begins Outside...
        Assert.Contains("OutsideGate", OutsideGuardTests.StripComments(File.ReadAllText(writers[0])), StringComparison.Ordinal); // ...and it asks the gate first
    }

    [Fact]
    public void Typed_Text_Is_Never_Logged()
    {
        // WORK-ORDER-7 section 3. What is typed into the search field is what the person is looking for: it goes to no logging call and no file call,
        // and no exception's message text is logged anywhere in the app (a message can carry a title, a name or a path). The words this guard looks
        // for are built from two halves, so that it does not find itself.
        var logCall = new Regex(@"(?<![A-Za-z0-9_])(?:_?files\.Log|Log\?*\.Invoke|log|Log|WriteLine|Debug\.Write|Trace\.Write)\s*\(", RegexOptions.Compiled);
        var typedWords = new[] { "Search" + "State", "search" + "Text", "Field" + "Text", ".Text" + ",", "tile." + "Name", "tile." + "Address", "Typed" };
        var exceptionMessage = new Regex(@"\b(?:e|ex|exception|error|problem)\.Message\b", RegexOptions.Compiled);
        var problems = new List<string>();
        foreach (var f in OutsideGuardTests.SourceFiles())
        {
            var lines = OutsideGuardTests.StripComments(File.ReadAllText(f)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!logCall.IsMatch(lines[i])) continue;
                var call = lines[i];
                if (typedWords.Any(w => call.Contains(w, StringComparison.Ordinal))) problems.Add($"{Path.GetRelativePath(RepoPaths.Root, f)}:{i + 1} logs typed text");
                if (exceptionMessage.IsMatch(call)) problems.Add($"{Path.GetRelativePath(RepoPaths.Root, f)}:{i + 1} logs an exception's message");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Claude_Settings_Location_Is_In_One_Outside_File()
    {
        // WORK-ORDER-7 section 4. Where Claude Code keeps its settings is known to one file, whose name begins "Outside" and which asks the gate first.
        // The text this guard looks for is built from two halves, so that it does not find itself.
        var folder = "." + "claude";
        var holders = OutsideGuardTests.SourceFiles()
            .Where(f => OutsideGuardTests.StripComments(File.ReadAllText(f)).Contains(folder, StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();

        var only = Assert.Single(holders);
        Assert.StartsWith("Outside", only, StringComparison.Ordinal);
        var code = OutsideGuardTests.StripComments(File.ReadAllText(OutsideGuardTests.SourceFiles().Single(f => Path.GetFileName(f) == only)));
        Assert.Contains("EditAgentSettings", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Addon_Log_Never_Names_A_Browser_Or_A_Tab()
    {
        // WORK-ORDER-9 section 2. The bridge writes nothing: it raises events (a kind and a number) and the app turns each into a log line. Every event the bridge
        // raises is given a kind of AddonEventKind and a plain number, and nothing else; the one place that raises is the method Raise; and what the app hands to
        // the log for the add-on is AddonLog.Line of such an event.
        var bridgeFolder = RepoPaths.File("src", "Island.Bridge");
        var bridgeFiles = Directory.EnumerateFiles(bridgeFolder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(Path.GetFileName, f => OutsideGuardTests.StripComments(File.ReadAllText(f)));
        var bridge = bridgeFiles["TabBridge.cs"];

        var raised = Regex.Matches(bridge, @"\bRaise\(([^;]*)\)\s*;").Select(m => m.Groups[1].Value.Trim()).Where(a => !a.StartsWith("AddonEventKind kind", StringComparison.Ordinal)).ToList();
        Assert.True(raised.Count >= 5, $"the guard must see the events (saw {raised.Count})");
        Assert.All(raised, a => Assert.Matches(@"^AddonEventKind\.\w+(\s*,\s*(\d+|refused))?$", a));

        // The events are made in one place and handed on in one place; no file of the bridge but TabBridge.cs raises or hands on anything.
        Assert.Single(Regex.Matches(bridge, @"new\s+AddonEvent\s*\("));
        Assert.Single(Regex.Matches(bridge, @"\(\(Action<AddonEvent>\)handler\)\("));
        Assert.Equal(2, Regex.Matches(bridge, @"\bAddonHappened\b").Count); // the declaration and the one place that walks its handlers: no alias, no other way to hand an event on
        foreach (var (name, code) in bridgeFiles.Where(f => f.Key != "TabBridge.cs"))
            Assert.False(Regex.IsMatch(code, @"\bAddonHappened\b|\bAddonEvent\b|\bAddonLog\b|\bRaise\s*\("), $"{name} touches the add-on's events");

        // The words of a line are made from the kind and the number alone.
        var line = OutsideGuardTests.StripComments(File.ReadAllText(RepoPaths.File("src", "Island.Core", "Tabs", "AddonStatus.cs")));
        var body = line[line.IndexOf("public static string Line", StringComparison.Ordinal)..];
        Assert.DoesNotMatch(@"\b(Profile|Title|Host|Tab|Browser|Origin)\b", body);

        // In the app: what is written for the add-on is AddonLog.Line of an event, and nothing else.
        var real = OutsideGuardTests.StripComments(File.ReadAllText(RepoPaths.File("src", "Island.App", "RealWorld.cs")));
        var written = Regex.Matches(real, @"\blog\??(?:\.Invoke)?\(([^;]*)\)\s*;").Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.True(written.Count >= 2, $"the guard must see what the app writes (saw {written.Count})");
        Assert.All(written, a => Assert.StartsWith("AddonLog.Line(", a, StringComparison.Ordinal));
        Assert.DoesNotContain("files.Log(", real, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(RepoPaths.File("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    [Fact]
    public void A_Picks_Path_Is_Named_Only_Where_It_Must_Be()
    {
        // WORK-ORDER-10 "THE RULE ABOUT PATHS": a pick the person added by hand stores its place in ONE field, `Location`. The word names that field only in the files that must
        // handle it: the pick, its store and its states, the cache of answers, the code that makes a pick by hand, the scenes' plan, the page logic that opens it and the one
        // outside file that starts it. In no other file, and in no logging call anywhere.
        var mustHandleIt = new[]
        {
            "Island.Core/Picks/Pick.cs", "Island.Core/Picks/PickStore.cs", "Island.Core/Picks/PickState.cs", "Island.Core/Picks/PickTargetCache.cs", "Island.Core/Picks/HandPicks.cs",
            "Island.Core/Scenes/ScenePlan.cs", "Island.App/PickPages.cs", "Island.Sources.Programs/OutsideActions.cs", "Island.App/HandStage.cs", // the self-test stage that adds a place by hand
        };
        // The same word for something that is not a pick's place: where Claude Code keeps its settings file (written %USERPROFILE%, never expanded).
        var unrelated = new[] { "Island.Core/Agents/AgentConnector.cs", "Island.App/OutsideClaudeSettings.cs", "Island.App/OutsideCodexSettings.cs", "Island.Core/SettingsEdit/SettingsSession.cs" };

        var root = RepoPaths.File("src");
        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            var name = Path.GetRelativePath(root, file).Replace('\\', '/');
            var code = OutsideGuardTests.StripComments(File.ReadAllText(file));
            if (!Regex.IsMatch(code, @"\bLocation\b")) continue;
            if (!mustHandleIt.Contains(name) && !unrelated.Contains(name)) offenders.Add(name);
            Assert.False(GuardRules.PathReachesASink(code), name + ": the place reaches a statement that writes, shows or throws text"); // never in a log, a refusal, a throw, a trace, a console line
        }

        Assert.Empty(offenders);
        Assert.All(mustHandleIt, name => Assert.True(File.Exists(Path.Combine(root, name)), name));
    }

    [Fact]
    public void A_Title_Or_A_Process_Name_Never_Reaches_A_Log_Call()
    {
        // WORK-ORDER-11: window titles, the names and ids of processes, project names, session ids and folders live in memory only. Every file the work order adds or
        // changes for the Terminals page and the helpers is read: no statement that calls a log, a refusal, a balloon or a console line also names one of them.
        var root = RepoPaths.File("src");
        var named = new[]
        {
            "Island.App/TerminalsPage.cs", "Island.Sources.Programs/TerminalProbe.cs", "Island.Core/Joints/TerminalProbe.cs", "Island.Core/Terminals/TerminalRing.cs",
            "Island.App/PickPages.cs", "Island.App/IslandRuntime.cs", "Island.App/IslandController.cs", "Island.App/Visuals/ContentsLayer.cs", "Island.App/Visuals/TileView.cs",
            "Island.App/CloseHandler.cs", "Island.Notify/Program.cs", "Island.Agents/AgentPipeServer.cs", "Island.App/AgentNoticeHost.cs", "Island.App/OutsideClaudeSettings.cs",
            "Island.App/HelperSessions.cs", "Island.App/OutsideCodexSettings.cs", "Island.Notify/ProcessSnapshot.cs", "Island.Agents/AgentPipeSecurity.cs", "Island.Core/TerminalsPageRefusals.cs",
            "Island.Core/Agents/AgentConnector.cs", "Island.Core/Agents/AgentNotice.cs", "Island.Core/Agents/AgentPipe.cs", "Island.Core/Agents/AgentSignals.cs", "Island.Core/Agents/AgentSignalTables.cs",
            "Island.Core/Agents/AgentText.cs", "Island.Core/Agents/AgentWire.cs", "Island.Core/Agents/HookCommand.cs", "Island.Core/Agents/HookInput.cs", "Island.Core/Agents/HookInstaller.cs",
            "Island.Core/Agents/NoticeQueue.cs", "Island.Core/Agents/NotifyArguments.cs", "Island.Core/Agents/ProcessChain.cs", "Island.Core/Agents/ProjectName.cs", "Island.Core/Agents/TerminalChoice.cs",
        };
        var folders = new[] { "Island.Core/Terminals", "Island.Core/Agents/Sessions", "Island.Core/Agents/Connect" };
        var files = named.Select(n => Path.Combine(root, n.Replace('/', Path.DirectorySeparatorChar))).Where(File.Exists)
            .Concat(folders.SelectMany(f => Directory.EnumerateFiles(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar)), "*.cs", SearchOption.AllDirectories)))
            .ToList();
        Assert.True(files.Count > 15, $"the guard must see the files ({files.Count})");

        var offenders = new List<string>();
        foreach (var file in files)
            if (GuardRules.SensitiveReachesALog(OutsideGuardTests.StripComments(File.ReadAllText(file)))) offenders.Add(Path.GetRelativePath(root, file));

        Assert.Empty(offenders);
        Assert.True(GuardRules.SensitiveReachesALog("_files.Log($\"opened {window.Title}\");"));
        Assert.True(GuardRules.SensitiveReachesALog("Debug.WriteLine(process.ExeName);"));
        Assert.False(GuardRules.SensitiveReachesALog("var title = window.Title; Render(title);"));
    }

    [Fact]
    public void No_Console_Is_Ever_Attached_To()
    {
        // WORK-ORDER-11 §2: attaching to another program's console makes the island receive that terminal's Ctrl+C and close events; reading another program's memory or
        // process information is never done either. The four names appear nowhere under src/.
        var root = RepoPaths.File("src");
        foreach (var file in SourceFiles())
        {
            var code = OutsideGuardTests.StripComments(File.ReadAllText(file));
            foreach (var word in new[] { "AttachConsole", "FreeConsole", "ReadProcessMemory", "NtQueryInformationProcess" })
                Assert.False(code.Contains(word, StringComparison.Ordinal), $"{word} in {Path.GetRelativePath(root, file)}");
        }
    }

    [Fact]
    public void The_App_Reads_The_Process_List_In_One_Place()
    {
        // WORK-ORDER-11 §2: the app's one reader of the process list is called from one place, the Terminals page. (Island.Notify reads it for its own chain, as it always did.)
        var root = RepoPaths.File("src");
        var readers = new List<string>();
        var callers = new List<string>();
        foreach (var file in SourceFiles())
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("Island.Notify/", StringComparison.Ordinal)) continue;
            var code = OutsideGuardTests.StripComments(File.ReadAllText(file));
            if (code.Contains("CreateToolhelp32Snapshot", StringComparison.Ordinal) || code.Contains("Process.GetProcesses", StringComparison.Ordinal) || code.Contains("EnumProcesses", StringComparison.Ordinal))
                readers.Add(relative);
            if (code.Contains("ProcessList.Read", StringComparison.Ordinal) || code.Contains(".Probe.Read(", StringComparison.Ordinal) || code.Contains("_probe.Read(", StringComparison.Ordinal))
                callers.Add(relative);
        }

        Assert.Equal(["Island.Sources.Programs/TerminalProbe.cs"], readers);
        Assert.Equal(["Island.App/TerminalsPage.cs", "Island.Sources.Programs/TerminalProbe.cs"], callers.Order().ToList()); // the page calls the probe; the probe calls its own reader
    }

    [Fact]
    public void A_Choosing_Window_Always_Has_An_Owner()
    {
        // Windows' own windows for choosing a file or a folder are made in files whose name begins "Outside" and nowhere else, and every one is shown with the window that owns it
        // (ShowDialog(owner)), so it opens in front of the settings screen and never behind it. A call with empty parentheses fails.
        var made = new List<string>();
        foreach (var file in SourceFiles())
        {
            var code = OutsideGuardTests.StripComments(File.ReadAllText(file));
            if (!GuardRules.DialogTypes.IsMatch(code)) continue;
            made.Add(Path.GetFileName(file));
            Assert.StartsWith("Outside", Path.GetFileName(file), StringComparison.Ordinal);
            Assert.True(GuardRules.OwnerGuardAccepts(code), Path.GetFileName(file) + ": a choosing window is shown without an owner (every call must be ShowDialog(owner))");
        }

        Assert.NotEmpty(made);
    }

    [Fact]
    public void Real_Pipe_Name_Is_Not_In_Tests()
    {
        // The real pipe name is used by the app started for real and by Island.Notify, and by nothing else: no test, and no helper, ever uses it.
        var usedIn = OutsideGuardTests.SourceFiles()
            .Where(f => OutsideGuardTests.StripComments(File.ReadAllText(f)).Contains("Default" + "Name", StringComparison.Ordinal)
                        && OutsideGuardTests.StripComments(File.ReadAllText(f)).Contains("AgentPipe", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .Order()
            .ToList();
        Assert.Equal(["AgentPipe.cs"], usedIn); // where it is defined; the app started for real and Island.Notify ask for the pipe of the user (ForThisUser, WORK-ORDER-13), which is built from it
        var askedFor = OutsideGuardTests.SourceFiles().Where(f => OutsideGuardTests.StripComments(File.ReadAllText(f)).Contains("AgentPipe.ForThisUser()", StringComparison.Ordinal)).Select(f => Path.GetFileName(f)).Order().ToList();
        Assert.Equal(["AppHost.cs", "Program.cs"], askedFor);

        var literal = "island.agents." + "notice";
        var tests = Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Where(f => File.ReadAllText(f).Contains(literal, StringComparison.Ordinal) || File.ReadAllText(f).Contains("AgentPipe." + "Default" + "Name", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoPaths.Root, f))
            .ToList();
        Assert.Empty(tests);
    }

    [Fact]
    public void Notify_Always_Exits_Zero()
    {
        // A hook that fails can hold up or disturb the coding agent, so Island.Notify ends with 0 on every path and never throws or exits otherwise.
        var code = OutsideGuardTests.StripComments(File.ReadAllText(RepoPaths.File("src", "Island.Notify", "Program.cs")));
        var returns = Regex.Matches(code, @"\breturn\s+([^;]+);").Where(m => !m.Value.Contains("ToArray", StringComparison.Ordinal) && !m.Value.Contains("buffer", StringComparison.Ordinal)).Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.NotEmpty(returns);
        Assert.All(returns, r => Assert.Equal("0", r));
        Assert.DoesNotContain("Environment.Exit", code, StringComparison.Ordinal);
        Assert.DoesNotContain("throw ", code, StringComparison.Ordinal);
    }
}
