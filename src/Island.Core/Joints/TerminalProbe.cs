using Island.Core.Terminals;

namespace Island.Core;

/// <summary>The console windows and the process list, as one reading of <see cref="ITerminalProbe"/> (WORK-ORDER-11 section 2). In memory only.</summary>
public sealed record TerminalProbeFacts(IReadOnlyList<ConsoleWindowFact> ConsoleWindows, IReadOnlyList<ProcessFact> Processes)
{
    public static TerminalProbeFacts Empty { get; } = new([], []);
}

/// <summary>
/// What only the machine can say about the Terminals page: the hidden console windows and classic console windows (class, owner, owning process)
/// and the process list (id, parent id, file name). It reads, it starts nothing and attaches to nothing. The page asks it off the drawing thread,
/// only while it is laid out; under the self-test it is never given a real one.
/// </summary>
public interface ITerminalProbe
{
    TerminalProbeFacts Read();
}
