namespace Island.Core.Agents.Sessions;

/// <summary>
/// The numbers of the version-2 message and of the session book. A number the work order gives is marked (WO11);
/// one chosen by Claude is marked (Claude). Every one is pinned by SessionLimitsTests.
/// </summary>
public static class SessionLimits
{
    /// <summary>The version-2 message's "v".</summary>
    public const int WireVersion = 2;

    /// <summary>The session id, cleaned and cut to this many characters (WO11).</summary>
    public const int MaxSessionIdChars = 64;

    /// <summary>The helper's name, e.g. "claude" (Claude).</summary>
    public const int MaxHelperChars = 32;

    /// <summary>The event's name; the longest of Claude Code's is 18 characters (Claude).</summary>
    public const int MaxEventChars = 48;

    /// <summary>The kind: a notification type or a tool's name (Claude).</summary>
    public const int MaxKindChars = 64;

    /// <summary>The tail of the folder; the project's name is its last part (WO11).</summary>
    public const int MaxFolderChars = 260;

    /// <summary>Process ids in the chain (WO11: AgentPipe.MaxChain).</summary>
    public const int MaxChain = AgentPipe.MaxChain;

    /// <summary>The whole message, newline included (WO11: AgentPipe.MaxMessageBytes).</summary>
    public const int MaxMessageBytes = AgentPipe.MaxMessageBytes;

    /// <summary>Sessions kept, remembered ends included; the one heard from least recently goes first (WO11, "Claude").</summary>
    public const int MaxSessions = 64;
}
