using Island.Core.Terminals;
using TermProcess = Island.Core.Terminals.ProcessFact;

namespace Island.Attack11.Tests;

/// <summary>Finds the worktree root (the folder with Island.sln) so a test can read source text. Read only.</summary>
internal static class Repo
{
    public static string Root { get; } = Find();

    public static string Text(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Island.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Island.sln not found above the test folder");
    }
}

/// <summary>Invented facts: made-up windows, programs and processes, nothing read from Windows.</summary>
internal static class Fact
{
    public const string TermClass = "CASCADIA_HOSTING_WINDOW_CLASS";
    public const string ClassicClass = "ConsoleWindowClass";
    public const string Pseudo = "PseudoConsoleWindow";

    /// <summary>The real default tables (their program names are generic file names; every window, title and process below is invented).</summary>
    public static TerminalTables Tables { get; } = TerminalTables.Default;

    /// <summary>A terminal window of an invented terminal program (class is the terminal class).</summary>
    public static TermWindowFact Term(long handle, int owner, string title = "Alpha", string exe = "WindowsTerminal.exe", int rank = 0, string cls = TermClass) =>
        new(handle, owner, exe, null, cls, title, rank);

    /// <summary>A window of an AI program of the default table (Claude.exe is the table's own candidate).</summary>
    public static TermWindowFact Ai(long handle, int owner, string title = "Alpha chat", string exe = "Claude.exe", int rank = 0) =>
        new(handle, owner, exe, null, "Chrome_WidgetWin_1", title, rank);

    public static TermWindowFact Other(long handle, int owner, string title = "Notes", string exe = "alpha.exe", int rank = 0) =>
        new(handle, owner, exe, null, "Alpha_Class", title, rank);

    public static ConsoleWindowFact Console(long handle, int owner, long ownerWindow, string cls = Pseudo) => new(handle, cls, owner, ownerWindow);

    public static TermProcess Proc(int id, int parent, string exe) => new(id, parent, exe);

    public static TerminalReading Reading(IEnumerable<TermWindowFact>? windows = null, IEnumerable<ConsoleWindowFact>? consoles = null, IEnumerable<TermProcess>? processes = null,
        IEnumerable<HelperSessionFact>? sessions = null) =>
        new([.. windows ?? []], [.. consoles ?? []], [.. processes ?? []], [.. sessions ?? []]);

    public static HelperSessionFact Session(string helper, string project, int[] chain, int hangs, HelperState state, long heard) =>
        new(helper, project, chain, hangs, state, heard);
}
