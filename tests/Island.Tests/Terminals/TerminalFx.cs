using Island.Core.Terminals;

namespace Island.Tests.Terminals;

/// <summary>Invented tables and invented facts for the Terminals tests. No real window, program or folder appears here.</summary>
internal static class TerminalFx
{
    public const string Wt = "CASCADIA_HOSTING_WINDOW_CLASS";
    public const string ClassicConsole = "ConsoleWindowClass";
    public const string Pseudo = "PseudoConsoleWindow";
    public const string TermExe = "alpha-term.exe";
    public const string AiExe = "bravo.exe";
    public const string HelperAExe = "helper-a.exe"; // "Claude Code"
    public const string HelperBExe = "helper-b.exe"; // "Codex"

    public static TerminalTables Tables { get; } = new(
        [Wt, ClassicConsole],
        [new TerminalProgramRow(TermExe, "Alpha Term")],
        [new AiProgramRow("Bravo", [AiExe], ["Invented.Bravo"])],
        [new HelperProgramRow(HelperAExe, "Claude Code"), new HelperProgramRow(HelperBExe, "Codex")],
        new Dictionary<string, HelperColor>
        {
            ["Claude Code"] = new(1, 2, 3),
            ["Codex"] = new(4, 5, 6),
        });

    public static TermWindowFact TermWindow(long handle, int pid, string title = "Alpha", int rank = 0, string cls = Wt, string? exe = TermExe) =>
        new(handle, pid, exe, null, cls, title, rank);

    public static TermWindowFact AiWindow(long handle, int pid, string title = "Chat", int rank = 0) =>
        new(handle, pid, AiExe, null, "Chrome_WidgetWin_1", title, rank);

    public static TermWindowFact OtherWindow(long handle, int pid, string title = "Notes", int rank = 0) =>
        new(handle, pid, "notes.exe", null, "Notepad", title, rank);

    public static ConsoleWindowFact Hidden(long handle, int ownerPid, long ownerWindow) => new(handle, Pseudo, ownerPid, ownerWindow);

    public static ProcessFact Proc(int id, int parent, string exe) => new(id, parent, exe);

    public static HelperSessionFact Session(string helper, string project, int hangsOn, HelperState state, long heard = 1, params int[] chain) =>
        new(helper, project, chain, hangsOn, state, heard);

    public static TerminalReading Reading(
        IEnumerable<TermWindowFact>? windows = null,
        IEnumerable<ConsoleWindowFact>? consoles = null,
        IEnumerable<ProcessFact>? processes = null,
        IEnumerable<HelperSessionFact>? sessions = null) =>
        new([.. windows ?? []], [.. consoles ?? []], [.. processes ?? []], [.. sessions ?? []]);

    public static IReadOnlyList<TerminalTile> Tiles(TerminalReading reading) => TerminalTiles.Build(reading, Tables);

    /// <summary>One terminal window (handle 1, process 300) with a shell (200) and a helper (100) in it, joined by a hidden console window.</summary>
    public static TerminalReading HelperInWindowOne(string helperExe = HelperAExe, string title = "Alpha") =>
        Reading(
            [TermWindow(1, 300, title)],
            [Hidden(50, 200, 1)],
            [Proc(300, 1, "explorer.exe"), Proc(200, 300, "shell.exe"), Proc(100, 200, helperExe)]);
}
