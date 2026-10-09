using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Island.Attack11.Tests;

/// <summary>
/// A search of the new source of WORK-ORDER-11 for any call that could write a title, a folder, a session id or a process name (or an id of a process or a window) to a file or a log. The
/// words of the search are invented patterns, broader than the ones of GuardTests: a hit is a statement that calls something that writes, shows, throws or sends text AND names something
/// sensitive. Every hit is listed in the report; the reviewed ones are named here, with the reason each is no leak.
/// </summary>
public class SourceScanAttackTests(ITestOutputHelper output)
{
    private static readonly string[] Folders =
    [
        @"src\Island.Core\Terminals", @"src\Island.Core\Agents", @"src\Island.Agents", @"src\Island.Notify",
    ];

    private static readonly string[] Files =
    [
        @"src\Island.App\TerminalsPage.cs", @"src\Island.App\HelperSessions.cs", @"src\Island.App\OutsideCodexSettings.cs", @"src\Island.Sources.Programs\TerminalProbe.cs",
        @"src\Island.Core\Joints\TerminalProbe.cs", @"src\Island.Core\TerminalsPageRefusals.cs",
    ];

    private static List<string> Scanned()
    {
        var all = new List<string>();
        foreach (var folder in Folders)
        {
            var dir = Path.Combine(Repo.Root, folder);
            Assert.True(Directory.Exists(dir), folder);
            all.AddRange(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")));
        }

        foreach (var file in Files)
        {
            var path = Path.Combine(Repo.Root, file);
            Assert.True(File.Exists(path), file);
            all.Add(path);
        }

        return all;
    }

    /// <summary>Source without its comments (string literals are respected, so a "//" inside one stays).</summary>
    internal static string StripComments(string code)
    {
        var sb = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
                sb.Append('\n');
            }
            else if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) { if (code[i] == '\n') sb.Append('\n'); i++; }
                i++;
            }
            else if (c == '"' && i + 2 < code.Length && code[i + 1] == '"' && code[i + 2] == '"')
            {
                var end = code.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                end = end < 0 ? code.Length - 3 : end;
                sb.Append(code, i, end + 3 - i);
                i = end + 2;
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && (code[i - 1] == '@' || (i > 1 && code[i - 1] == '$' && code[i - 2] == '@') || (i > 1 && code[i - 1] == '@' && code[i - 2] == '$'));
                sb.Append(c);
                i++;
                while (i < code.Length)
                {
                    sb.Append(code[i]);
                    if (code[i] == '"' && verbatim && i + 1 < code.Length && code[i + 1] == '"') { sb.Append(code[++i]); }
                    else if (code[i] == '\\' && !verbatim && i + 1 < code.Length) { sb.Append(code[++i]); }
                    else if (code[i] == '"') break;
                    i++;
                }
            }
            else sb.Append(c);
        }

        return sb.ToString();
    }

    // Anything that writes, shows, throws or sends text: logs, consoles, traces, file and stream writers, serializers, refusals, exceptions that carry a message, a pipe write.
    private static readonly Regex Sink = new(
        @"(?<![A-Za-z0-9_])(?:[Ll]og\w*|Logger\w*|Debug\.\w+|Trace\.\w+|Console\.\w+|WriteLine|Write\w*|Append\w*|Print\w*|Refuse\w*|Report\w*|Serialize\w*|ToJsonString|StreamWriter|FileStream|File\.\w+|Directory\.\w+|Registry\w*|SetValue|CreateSubKey|throw\s+new|Exception\s*\()",
        RegexOptions.Compiled);

    // What must never reach one of them: the words for a title, a folder, a session id, a process or program name, an id of a process or window, the project's name, the chain.
    private static readonly Regex Sensitive = new(
        @"\b(?:Title|WindowTitle|ExeName|ProcessName|ProjectName|Project|SessionId|Sid|Folder|Cwd|Chain|Helper|HelperName|FirstLine|SecondLine|ProcessId|OwnerProcessId|Pid|ParentId|Handle|WindowHandle|FileName|Key)\b|\bs\.|\bmessage\.|\bsession\.|\bprocess\.|\bwindow\.|\bfound\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record Hit(string File, int Line, string Statement, bool Sensitive);

    private static List<Hit> Hits()
    {
        var hits = new List<Hit>();
        foreach (var path in Scanned())
        {
            var code = StripComments(File.ReadAllText(path));
            var rel = Path.GetRelativePath(Repo.Root, path).Replace('\\', '/');
            // a statement ends at ; or at an opening or closing brace; a line number is where it starts
            var line = 1;
            var start = 0;
            for (var i = 0; i <= code.Length; i++)
            {
                var end = i == code.Length || code[i] is ';' or '{' or '}';
                if (code.Length > i && code[i] == '\n') line++;
                if (!end) continue;
                var statement = code[start..i].Trim();
                var startLine = line - statement.Count(ch => ch == '\n');
                start = i + 1;
                if (statement.Length == 0 || !Sink.IsMatch(statement)) continue;
                hits.Add(new Hit(rel, startLine, Regex.Replace(statement, @"\s+", " "), Sensitive.IsMatch(statement)));
            }
        }

        return hits;
    }

    [Fact]
    public void Holds_The_Scan_Sees_The_Files_Of_The_New_Code()
    {
        var files = Scanned();
        Assert.True(files.Count >= 35, $"only {files.Count} files");
        Assert.Contains(files, f => f.EndsWith("HelperSessions.cs"));
        Assert.Contains(files, f => f.EndsWith("OutsideCodexSettings.cs"));
        Assert.Contains(files, f => f.EndsWith("SessionTracker.cs"));
        Assert.Contains(files, f => f.EndsWith("CodexHooks.cs"));
        Assert.Contains(files, f => f.EndsWith("Program.cs"));
        Assert.Contains(files, f => f.EndsWith("AgentPipeServer.cs"));
        Assert.Contains(files, f => f.EndsWith("TerminalProbe.cs"));
    }

    [Fact]
    public void Holds_The_Scan_Itself_Finds_What_It_Is_Meant_To_Find()
    {
        // invented lines, one of each way out
        foreach (var line in new[]
        {
            "_files.Log($\"x {window.Title}\");", "Debug.WriteLine(process.ExeName);", "File.WriteAllText(path, session.SessionId);", "File.AppendAllText(p, project);",
            "Console.Error.WriteLine(folder);", "throw new InvalidOperationException(title + Title);", "writer.Write(Chain);", "Trace.TraceError(message.Folder);",
        })
            Assert.True(Sink.IsMatch(line) && Sensitive.IsMatch(line), line);
        Assert.False(Sink.IsMatch("var title = window.Title; Render(title);"));
        Assert.Contains("// not a comment", StripComments("var s = \"// not a comment\"; // a comment"));
        Assert.DoesNotContain("; //", StripComments("var s = \"// not a comment\"; // a comment"));
    }

    /// <summary>
    /// The statements that call something which writes, shows, throws or sends and name something sensitive, reviewed one by one. Each is no leak: the reason is beside it.
    /// A new hit that is not on this list fails the test, so a new call is looked at by a person.
    /// </summary>
    private static readonly string[] Reviewed =
    [
        // The two encoders write the message that Island.Notify sends through the named pipe (a Utf8JsonWriter over a MemoryStream, then the pipe): the island's memory, never a file or a log.
        "AgentWire.cs|w.WriteString(\"f\", ",
        "AgentWire.cs|foreach (var pid in chain",
        "SessionWire.cs|var bytes = Write(",
        "SessionWire.cs|return bytes.Length <= ",
        "SessionWire.cs|private static byte[] Write(",
        "SessionWire.cs|w.WriteString(\"a\", ",
        "SessionWire.cs|w.WriteString(\"s\", ",
        "SessionWire.cs|w.WriteString(\"f\", ",
        "SessionWire.cs|foreach (var pid in ",
        // The current Windows user's SID, for the pipe's access rule; the exception text is a fixed sentence.
        "AgentPipeSecurity.cs|var me = WindowsIdentity.GetCurrent().User",
        // Codex's own folder (the profile's .codex), made before the settings file is written: a place, not a name from outside.
        "OutsideCodexSettings.cs|Directory.CreateDirectory(folder)",
        "OutsideCodexSettings.cs|if (freshSecondCopy && File.Exists(file)) File.Copy(file, Path.Combine(folder, UpdateBackupName)",
    ];

    [Fact]
    public void Holds_No_Statement_Names_Something_Sensitive_And_Writes_It_Anywhere()
    {
        var hits = Hits();
        output.WriteLine($"{hits.Count} statements call a sink; {hits.Count(h => h.Sensitive)} of them name something sensitive:");
        foreach (var h in hits) output.WriteLine($"  {(h.Sensitive ? "S" : " ")} {h.File}:{h.Line}  {Short(h.Statement)}");

        var offenders = hits.Where(h => h.Sensitive && !IsReviewed(h)).Select(h => $"{h.File}:{h.Line}  {Short(h.Statement)}").ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Holds_Every_File_Write_Of_The_New_Code_Is_In_One_Outside_File_That_Asks_The_Gate()
    {
        var writers = new List<string>();
        foreach (var path in Scanned())
        {
            var code = StripComments(File.ReadAllText(path));
            if (Regex.IsMatch(code, @"File\.(?:Write|Append|Copy|Move|Create|Replace|Delete)\w*|StreamWriter|FileStream|Registry\.|SetValue\(|CreateSubKey"))
                writers.Add(Path.GetFileName(path));
        }

        // WORK-ORDER-12 section 5: the file part of the two connectors (the copy beside the file, the temporary file, the move, the copy of Island.Notify) moved out of the two Outside files into one
        // small class that is handed a path and knows no place, so that it can be tested on a temporary folder. It stays the one writer, and it is reached from the two gated Outside files only.
        Assert.Equal(["HelperFileEditor.cs"], writers);
        var users = Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .Where(f => StripComments(File.ReadAllText(f)).Contains("HelperFileEditor.", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["OutsideClaudeSettings.cs", "OutsideCodexSettings.cs"], users.Where(u => u != "HelperFileEditor.cs").ToArray());
        Assert.DoesNotContain(users, u => u is not ("OutsideClaudeSettings.cs" or "OutsideCodexSettings.cs"));
        foreach (var outside in new[] { "OutsideCodexSettings.cs", "OutsideClaudeSettings.cs" })
            Assert.Contains("OutsideGate", StripComments(File.ReadAllText(Path.Combine(Repo.Root, "src", "Island.App", outside))));
    }

    [Fact]
    public void Holds_Nothing_Of_The_New_Code_Reads_A_Session_Id_Or_A_Title_Into_A_Path_Call()
    {
        // the file calls that read or probe the disk, anywhere in the scanned source, never carry a name from outside
        var offenders = new List<string>();
        foreach (var h in Hits())
            if (Regex.IsMatch(h.Statement, @"(?:File|Directory|Path)\.\w+") && Regex.IsMatch(h.Statement, @"\b(?:Title|SessionId|Sid|ProjectName|Project|Folder|Cwd|ExeName|ProcessName|HelperName|Chain)\b"))
                offenders.Add($"{h.File}:{h.Line}  {Short(h.Statement)}");
        Assert.Empty(offenders);
    }

    [Fact]
    public void Holds_The_New_Code_Has_No_Attach_Read_Memory_Or_Command_Line_Call()
    {
        foreach (var path in Scanned())
        {
            var code = StripComments(File.ReadAllText(path));
            foreach (var word in new[] { "AttachConsole", "FreeConsole", "ReadProcessMemory", "NtQueryInformationProcess", "GetCommandLine", "CommandLine", "OpenProcess", "Environment.GetEnvironmentVariables", "MainModule", "GetProcessById", "GetProcessesByName", "Process.Start", "SendInput", "SetForegroundWindow", "PostMessage", "SendMessage", "WriteProcessMemory", "CreateRemoteThread" })
                Assert.False(code.Contains(word, StringComparison.Ordinal), $"{word} in {Path.GetFileName(path)}");
        }
    }

    [Fact]
    public void Holds_No_Real_Pipe_Name_Is_Spelled_Out_In_The_New_Tests_Of_This_Attack()
    {
        var real = typeof(Island.Core.AgentPipe).GetField("DefaultName")!.GetRawConstantValue() as string;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.Root, "tests", "Island.Attack11.Tests"), "*.cs"))
            Assert.False(File.ReadAllText(file).Contains(real!, StringComparison.Ordinal), Path.GetFileName(file));
    }

    // ---- The guards of GuardTests, read from outside ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_The_Guard_That_Reads_The_New_Files_Does_Not_Read_All_Of_Them()
    {
        // FINDING A11-08 (LOW). GuardTests.A_Title_Or_A_Process_Name_Never_Reaches_A_Log_Call reads a named list of files plus three folders. The work order's new code also holds
        // src/Island.App/HelperSessions.cs (the session ids and project names), src/Island.App/OutsideCodexSettings.cs, src/Island.Notify/ProcessSnapshot.cs and the files directly in
        // src/Island.Core/Agents (HookInput.cs, AgentNotice.cs, AgentSignalTables.cs, NotifyArguments.cs ...), none of which that guard reads. Expected: every file of the scanned set is read.
        var text = File.ReadAllText(Path.Combine(Repo.Root, "tests", "Island.Tests", "GuardTests.cs"));
        var start = text.IndexOf("A_Title_Or_A_Process_Name_Never_Reaches_A_Log_Call", StringComparison.Ordinal);
        var body = text[start..text.IndexOf("No_Console_Is_Ever_Attached_To", start, StringComparison.Ordinal)];
        var missing = new List<string>();
        foreach (var path in Scanned())
        {
            var rel = Path.GetRelativePath(Path.Combine(Repo.Root, "src"), path).Replace('\\', '/');
            var name = Path.GetFileName(path);
            var inFolder = new[] { "Island.Core/Terminals/", "Island.Core/Agents/Sessions/", "Island.Core/Agents/Connect/" }.Any(f => rel.StartsWith(f, StringComparison.Ordinal));
            if (!inFolder && !body.Contains(rel, StringComparison.Ordinal)) missing.Add(rel);
        }

        Assert.True(missing.Count == 0, "not read by the guard: " + string.Join(", ", missing));
        _ = StripComments(body);
    }

    [Fact]
    public void Defect_The_Guard_Rule_Does_Not_See_A_File_Write_Or_An_Id_Of_A_Process()
    {
        // FINDING A11-09 (LOW). GuardRules.SensitiveReachesALog looks for log-like calls (Log*, Write(, WriteLine, Trace*, Debug, Warn*, Fatal, Notify, Refuse) and for the words Title,
        // ExeName, ProjectName, SessionId, Folder, Chain, ... It does not see File.WriteAllText / WriteAllLines / AppendAllText (the name after "Write" runs on), a StreamWriter, an exception that
        // carries the text, nor the ids of a process or a window (ProcessId, OwnerProcessId, Handle), which the work order keeps out of every file as well. Expected: each line below is a hit.
        var lines = new[]
        {
            "File.WriteAllText(path, window.Title);",
            "File.AppendAllText(path, process.ExeName);",
            "File.WriteAllLines(path, session.SessionId);",
            "_files.Log($\"owner {window.OwnerProcessId}\");",
            "Debug.WriteLine(process.ProcessId);",
            "throw new InvalidOperationException(\"no window for \" + window.Title);",
        };
        var missed = lines.Where(l => !Island.Tests.GuardRules.SensitiveReachesALog(l)).ToList();
        Assert.True(missed.Count == 0, "the guard lets these through: " + string.Join(" | ", missed));
    }

    // ---- Reading a hit ---------------------------------------------------------------------------------------------------------------------------------------

    private static bool IsReviewed(Hit hit)
    {
        var name = Path.GetFileName(hit.File);
        return Reviewed.Any(r =>
        {
            var parts = r.Split('|');
            return parts[0] == name && hit.Statement.StartsWith(parts[1], StringComparison.Ordinal);
        });
    }

    private static string Short(string statement) => statement.Length <= 140 ? statement : statement[..140] + "...";
}
