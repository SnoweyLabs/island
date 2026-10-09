namespace Island.Core.Agents.Sessions;

/// <summary>What a session is doing. Idle: known, nothing said yet (or after a tool finished with nothing running).</summary>
public enum SessionState
{
    Idle,
    Working,
    NeedsYou,
    Finished,
}

/// <summary>One row of the process list, as a plain fact of this piece's own: id, parent id, file name.</summary>
public sealed record ProcessFact(int Id, int ParentId, string ExeName)
{
    /// <summary>The same process: same id, same file name (case ignored), same parent. A reused id is another process.</summary>
    public bool Is(ProcessFact? other) =>
        other is not null && other.Id == Id && other.ParentId == ParentId && string.Equals(other.ExeName, ExeName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A session as the book holds it. <paramref name="Id"/> never changes for the life of a session (join on it);
/// <paramref name="Key"/> is the descriptive key of the work order and changes when the process is chosen.
/// <paramref name="Process"/> is the process the session hangs on, or null. <paramref name="IsShown"/>: it hangs on a
/// process, or its chain holds a process that owns a window of an AI program. Memory only; never logged.
/// </summary>
public sealed record SessionInfo(
    long Id,
    string Key,
    string Helper,
    string SessionId,
    string Project,
    IReadOnlyList<int> Chain,
    SessionState State,
    string ToolName,
    ProcessFact? Process,
    bool IsShown);

public enum ApplyOutcome
{
    /// <summary>The table maps this event to nothing.</summary>
    Ignored,

    /// <summary>Older than the last message applied to its session, or older than the session's end: no state changed.</summary>
    Dropped,

    Applied,
}

/// <summary><paramref name="SessionId"/> is the book's id of the session the message was for (0 when none). <paramref name="Changed"/>: something a tile shows may have changed.</summary>
public sealed record ApplyResult(ApplyOutcome Outcome, long SessionId, HelperSignal? Signal, bool Changed);

/// <summary>What a reading of the process list did: the sessions whose process went away (removed from the book).</summary>
public sealed record ReadingResult(bool Changed, IReadOnlyList<SessionInfo> Gone);
