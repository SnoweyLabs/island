namespace Island.Core;

/// <summary>Where Claude Code stands as far as the island can tell.</summary>
public enum AgentConnection
{
    /// <summary>Not connected: the hooks are not there (or the island may not look, as under the self-test).</summary>
    NotConnected,

    Connected,

    /// <summary>
    /// Some entry that runs Island.Notify is there but not all of today's: the connection made before the Terminals page tells the island only when Claude Code has
    /// finished or needs an answer. The row then offers Update (WORK-ORDER-11 section 3).
    /// </summary>
    ConnectedOlder,

    /// <summary>The helper's own folder is not on this computer: the row says so and has no button (WORK-ORDER-11 section 5). Only the folder's being there is looked at.</summary>
    NotFound,

    /// <summary>The settings file is there and cannot be read as JSON: the island leaves it alone.</summary>
    Unreadable,
}

/// <summary>
/// The connection of Claude Code (WORK-ORDER-7 section 4). The settings screen only draws and asks; what is read and written lives behind this, in one
/// file whose name begins "Outside" and that asks <see cref="OutsideGate"/> first. Reading the state is for the person who opens the section, writing is
/// for the person who presses the confirming button: nothing else ever calls it.
/// </summary>
public interface IAgentConnector
{
    /// <summary>The helper's name as the island writes it after <c>--agent</c> ("claude", "codex").</summary>
    string Helper => Island.Core.Agents.Sessions.HelperNames.ClaudeCode;

    /// <summary>The helper's name as the screen says it ("Claude Code", "Codex").</summary>
    string DisplayName => "Claude Code";

    /// <summary>One more sentence for the question (Codex asks the person once to trust new hooks); null when there is none.</summary>
    string? Trust => null;

    /// <summary>
    /// Brings a connection that is only "connected, older" up to today's set: the same as <see cref="Connect"/> (nothing is added twice), but a fresh copy of the file as it
    /// is now is saved beside it under a second name first, whatever copy is already there.
    /// </summary>
    ConnectorResult Update() => Connect();

    /// <summary>The location of Claude Code's settings file as it is written for the person, never expanded: %USERPROFILE%, then the folder and the file.</summary>
    string Location { get; }

    /// <summary>Why "Connect" is not offered, in words; null when it is.</summary>
    string? NotOffered { get; }

    /// <summary>What connecting would add, exactly, for the question that is asked before anything is written.</summary>
    IReadOnlyList<string> LinesToAdd { get; }

    AgentConnection State();

    /// <summary>Copies Island.Notify where the hooks will name it, adds the two entries after saving a copy of the file, and says what happened.</summary>
    ConnectorResult Connect();

    /// <summary>Removes exactly the entries that run Island.Notify and nothing else.</summary>
    ConnectorResult Disconnect();
}

/// <param name="Done">The file now says what was asked.</param>
/// <param name="Refusal">Why not, in plain words, or null.</param>
public sealed record ConnectorResult(bool Done, Refusal? Refusal);

/// <summary>The refusals of connecting Claude Code.</summary>
public static class AgentRefusals
{
    public static Refusal HooksFileUnreadable { get; } = new(
        HookInstaller.FileUnreadable,
        "Claude Code's settings file could not be read, so Island did not connect.",
        "Island left the file exactly as it was, because writing into a file it cannot read could break Claude Code.",
        "Open the file, fix it or ask Claude Code to, then press Connect again.");

    public static Refusal NotifyMissing { get; } = new(
        "NOTIFY_MISSING",
        "Island.Notify, the small program the hook runs, is not next to Island, so Island did not connect.",
        "A hook that names a program that is not there would only fail every time Claude Code stops.",
        "Start Island from its own folder (run.cmd does), then press Connect again.");

    /// <summary>The place of Island.Notify cannot be written into a hook line: told as that, not as a settings file that could not be read (the file was never read).</summary>
    public static Refusal NotifyPathInvalid { get; } = new(
        HookInstaller.NotifyPathInvalid,
        "Island did not connect: the path of Island.Notify is not one a hook line can hold.",
        "A relative path, or one with a quote, %, $ or backtick, could run something else.",
        "Move Island to a plainly named folder, start it there, then press Connect again.");

    public static Refusal HooksFileNotWritten { get; } = new(
        "HOOKS_FILE_NOT_WRITTEN",
        "Claude Code's settings file could not be written, so Island did not connect.",
        "The file may be read-only or held open by another program, such as an editor. Island left it exactly as it was.",
        "Close the program that holds it or make the file writable, then press Connect again.");

    /// <summary>The same refusal for taking the lines out again: Island did not disconnect, and Disconnect is what to press.</summary>
    public static Refusal HooksFileNotWrittenForDisconnect { get; } = new(
        "HOOKS_FILE_NOT_WRITTEN",
        "Claude Code's settings file could not be written, so Island did not take its lines out.",
        "The file may be read-only or held open by another program, such as an editor. Island left it exactly as it was.",
        "Close the program that holds it or make the file writable, then press Disconnect again.");

    public static Refusal NotifyNotCopied { get; } = new(
        "NOTIFY_NOT_COPIED",
        "Island.Notify, the small program the hook runs, could not be copied into the folder of Island, so Island did not connect.",
        "A hook may hold the old copy for a moment (a hook lives only a fraction of a second), or the folder may not let Island write.",
        "Wait a moment and press Connect again; if it happens again, close the program that holds the folder.");

    /// <summary>The same two refusals for Disconnect: they name the button that was pressed.</summary>
    public static Refusal HooksFileUnreadableForDisconnect { get; } = new(
        HookInstaller.FileUnreadable,
        "Claude Code's settings file could not be read, so Island did not take its lines out.",
        "Island left the file exactly as it was, because writing into a file it cannot read could break Claude Code.",
        "Open the file, fix it or ask Claude Code to, then press Disconnect again.");

    public static Refusal NotAllowedForDisconnect { get; } = new(
        "AGENT_SETTINGS_NOT_ALLOWED",
        "Island may not touch Claude Code's settings file here.",
        "Only the app started for real, by a person's own press, may; a test or a helper never does.",
        "Start Island normally and press Disconnect again.");

    public static Refusal NotAllowed { get; } = new(
        "AGENT_SETTINGS_NOT_ALLOWED",
        "Island may not touch Claude Code's settings file here.",
        "Only the app started for real, by a person's own press, may; a test or a helper never does.",
        "Start Island normally and press Connect again.");
}
