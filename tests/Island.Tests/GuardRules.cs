using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>
/// The rules behind GuardTests.A_Picks_Path_Is_Named_Only_Where_It_Must_Be and A_Choosing_Window_Always_Has_An_Owner (WORK-ORDER-10 section 3), in one file with no test framework in it,
/// so Island.Attack10.Tests runs the very same rules (it links this file) over invented source lines. They work on source with the comments already taken out.
/// </summary>
public static class GuardRules
{
    // Anything that writes, shows or throws text: a log, a refusal, a balloon, a debug or trace line, a console line, a logger, a file append, an exception, a list that is written out (a list named like a report or a log).
    private static readonly Regex Sink = new(@"\b(?:[Ll]og\w*|Refuse|Notify|WriteLine|Write|WriteAll\w*|Trace\w*|Debug|Warn\w*|Info|Error|Fatal|AppendAll\w*|throw|(?:report|log|lines?|messages?|notes?|output|entries|errors|warnings)\w*\.Add)\b");

    // The name a statement gives to a value it makes from a place: "var x =", "string x =", "is { } x", "out var x".
    private static readonly Regex Named = new(@"(?:\b(?:var|string)\s+(\w+)\s*=|\bis\s*\{\s*\}\s*(\w+)|\bout\s+(?:var|string)\s+(\w+))");

    /// <summary>True when a place (the field named Location, or a name made from it) reaches a statement that writes, shows or throws text.</summary>
    public static bool PathReachesASink(string code)
    {
        var statements = code.Split(';');
        var tainted = new HashSet<string>();
        foreach (var s in statements)
        {
            if (!Regex.IsMatch(s, @"\bLocation\b") && !Regex.IsMatch(s, @"\bPickPath\.Expand\b") && !tainted.Any(t => Regex.IsMatch(s, @"\b" + t + @"\b"))) continue;
            foreach (Match m in Named.Matches(s))
                foreach (var g in m.Groups.Cast<Group>().Skip(1).Where(g => g.Success)) tainted.Add(g.Value);
        }

        foreach (var s in statements)
        {
            if (!Sink.IsMatch(s)) continue;
            if (Regex.IsMatch(s, @"\bLocation\b")) return true;
            if (tainted.Any(t => Regex.IsMatch(s, @"\b" + t + @"\b"))) return true;
        }

        return false;
    }

    // Something a person never wants in a log: a window's title, a program's or process's name, a project, a session id, a folder, the text of a tile, a chain of processes.
    private static readonly Regex Sensitive = new(@"\b(?:Title|WindowTitle|ExeName|ProcessName|ProjectName|SessionId|Folder|FirstLine|SecondLine|Chain|Helper|ProcessId|OwnerProcessId|Pid|Handle)\b");

    // A call that writes, shows or sends text out: the sinks above without the throw and the report lists (WORK-ORDER-11: a title or a process name reaches no log call).
    private static readonly Regex LogCall = new(@"\b(?:[Ll]og\w*|Refuse|Notify|WriteLine|Write\w*|Append\w*|Trace\w*|Debug|Warn\w*|Fatal)\s*[(.]|\bthrow\s+new\b");

    /// <summary>True when a statement that calls a log (or a refusal, a balloon, a trace, a console line) also names a title, a process or program name, a project, a session id, a folder or a chain.</summary>
    public static bool SensitiveReachesALog(string code)
    {
        foreach (var statement in code.Split(';'))
            if (LogCall.IsMatch(statement) && Sensitive.IsMatch(statement)) return true;
        return false;
    }

    /// <summary>The names of the ways to open a window for choosing a file or a folder that a file must not use without an owner.</summary>
    public static readonly Regex DialogTypes = new(@"\b(OpenFileDialog|OpenFolderDialog|SaveFileDialog|FolderBrowserDialog|CommonOpenFileDialog|CommonFileDialog|CommonSaveFileDialog|FileOpenPicker|FileSavePicker|FolderPicker|FileOpenDialog|FileSaveDialog|IFileDialog|IFileOpenDialog|IFileSaveDialog|GetOpenFileName\w*|GetSaveFileName\w*|SHBrowseForFolder\w*)\b");

    private static readonly Regex ShowDialogCall = new(@"\.ShowDialog\(([^)]*)\)");

    /// <summary>
    /// True when the file makes a choosing window and EVERY ShowDialog call has an owner: a name that is not empty, not null, not default and not a variable the file sets to null. A file
    /// that makes one and shows none with an owner (a Win32 call, a picker) is not accepted either: the one way that is accepted is ShowDialog(owner).
    /// </summary>
    public static bool OwnerGuardAccepts(string code)
    {
        if (!DialogTypes.IsMatch(code)) return true;
        var calls = ShowDialogCall.Matches(code);
        if (calls.Count == 0) return false;
        foreach (Match call in calls)
        {
            var argument = call.Groups[1].Value.Trim();
            if (!Regex.IsMatch(argument, @"^\w+$") || argument is "null" or "default") return false;
            if (Regex.IsMatch(code, @"\b" + argument + @"\b\s*=\s*(?:null|default)\b")) return false;
        }

        return true;
    }
}
