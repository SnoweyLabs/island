namespace Island.Core.Terminals;

// WORK-ORDER-11 sections 1 and 2: the plain facts the Terminals page is worked out from. Every real reader
// (the window lister, the console probe, the process list, the hook sessions) is joined to these by the app.
// All of it lives in memory only: no title, program name or process name found running is ever written anywhere.

/// <summary>
/// A window the island already lists, with the class of the window and the program that owns it.
/// <paramref name="RecencyRank"/> is 0 for the most recently used window, 1 for the next, and so on.
/// </summary>
public sealed record TermWindowFact(long Handle, int OwnerProcessId, string? ExeName, string? PackageFamily, string ClassName, string Title, int RecencyRank);

/// <summary>
/// A console window as the probe reads it: class "PseudoConsoleWindow" (hidden, owned by the terminal's own window) or
/// "ConsoleWindowClass" (a classic console, which is itself the window). <paramref name="OwnerWindowHandle"/> is 0 when it has no owner.
/// </summary>
public sealed record ConsoleWindowFact(long Handle, string ClassName, int OwnerProcessId, long OwnerWindowHandle);

/// <summary>One row of the process list: id, parent id and file name, nothing more.</summary>
public sealed record ProcessFact(int Id, int ParentId, string ExeName);

public enum HelperState
{
    Idle,
    Working,
    NeedsYou,
    Finished,
}

/// <summary>
/// A helper conversation that a hook reported (WORK-ORDER-11 section 3), as a plain fact of this piece's own shape.
/// <paramref name="HangsOnProcessId"/> is the helper process it hangs on (0 when none); <paramref name="LastHeard"/> grows with
/// every message (the later the number, the more recently the session was heard from).
/// </summary>
public sealed record HelperSessionFact(
    string HelperName,
    string ProjectName,
    IReadOnlyList<int> Chain,
    int HangsOnProcessId,
    HelperState State,
    long LastHeard);

/// <summary>Everything one reading of the machine gives: the page's windows, the console windows, the process list and the sessions.</summary>
public sealed record TerminalReading(
    IReadOnlyList<TermWindowFact> Windows,
    IReadOnlyList<ConsoleWindowFact> ConsoleWindows,
    IReadOnlyList<ProcessFact> Processes,
    IReadOnlyList<HelperSessionFact> Sessions)
{
    public static TerminalReading Empty { get; } = new([], [], [], []);
}

/// <summary>A colour for a helper's disc. Three bytes; the drawing turns it into a brush.</summary>
public readonly record struct HelperColor(byte R, byte G, byte B);

public enum WindowRole
{
    /// <summary>Not on the Terminals page.</summary>
    None,
    Terminal,
    AiProgram,
}

/// <summary>What the face of a tile is.</summary>
public enum FaceKind
{
    /// <summary>The program's icon (from <c>ExeName</c> / <c>PackageFamily</c>); <c>Letters</c> is the fallback when no icon can be read.</summary>
    ProgramIcon,

    /// <summary>A helper's disc: <c>Disc</c> colour with <c>Letters</c> on it.</summary>
    HelperDisc,
}

public sealed record TileFace(FaceKind Kind, string? ExeName, string? PackageFamily, string Letters, HelperColor? Disc);

/// <summary>One round tile of the Terminals page. The identity of a tile is its window (<see cref="WindowHandle"/>).</summary>
/// <param name="Kind">Terminal or AI program: what the window is (a helper in a terminal does not change it).</param>
/// <param name="Ring">The state the ring shows; <see cref="HelperState.Idle"/> means no ring.</param>
/// <param name="Dots">How many dots show under the tile (0 for one helper or none).</param>
/// <param name="HelperName">The name of the helper the texts are made from; null on a tile with no helper in it or with a helper that only gains a ring.</param>
public sealed record TerminalTile(
    long WindowHandle,
    WindowRole Kind,
    string FirstLine,
    string SecondLine,
    TileFace Face,
    HelperState Ring,
    int Dots,
    string? HelperName);
