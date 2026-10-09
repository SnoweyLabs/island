namespace Island.Core.Agents.Connect;

/// <summary>
/// One row of the island's signal table for one helper: an event the helper's own hooks page names, and the island signal it means. The signal is
/// text (started, working, needs you, tool done, finished, ended) so this file uses no type of any other piece; the main session joins the tables.
/// </summary>
/// <param name="Helper">The helper's name as the island writes it after --agent.</param>
/// <param name="Event">The event's name exactly as the hooks page spells it.</param>
/// <param name="Kind">What narrows the event (a notification's kind, a tool's name), or null when the signal does not depend on it.</param>
/// <param name="Signal">One of <see cref="Signals"/>.</param>
/// <param name="RaisesNotice">True for the helper's "finished" (turn ended) and "needs you" (asks permission): the two that raise the notice.</param>
/// <param name="Written">False when the island writes no entry for the event, and why is in <paramref name="Note"/>.</param>
public sealed record SignalRow(string Helper, string Event, string? Kind, string Signal, bool RaisesNotice, bool Written, string? Note);

/// <summary>The six signals the island knows, as the text a row carries.</summary>
public static class Signals
{
    public const string Started = "started";
    public const string Working = "working";
    public const string NeedsYou = "needs you";
    public const string ToolDone = "tool done";
    public const string Finished = "finished";
    public const string Ended = "ended";
}

/// <summary>Codex's rows for the signal table. Every name read on https://learn.chatgpt.com/docs/hooks on 2026-10-07 (see agent-signals-2.md).</summary>
public static class CodexSignals
{
    public const string Helper = "codex";

    /// <summary>Why no entry is written for SessionEnd: the page says it always runs in the foreground, so it would make Codex wait.</summary>
    public const string SessionEndNote = "SessionEnd hooks always run synchronously, even when async is true: an entry would make Codex wait, so none is written. The session ends when its process is gone.";

    /// <summary>The interruption by the person: the turn is over (finished), and it raises no notice.</summary>
    public const string InterruptNote = "The person interrupted the turn: the state is finished, and no notice is raised.";

    public static IReadOnlyList<SignalRow> Rows { get; } =
    [
        new(Helper, "SessionStart", null, Signals.Started, false, true, null),
        new(Helper, "UserPromptSubmit", null, Signals.Working, false, true, null),
        new(Helper, "PermissionRequest", null, Signals.NeedsYou, true, true, "The kind is the tool's name (tool_name)."),
        new(Helper, "PostToolUse", null, Signals.ToolDone, false, true, "The kind is the tool's name (tool_name)."),
        new(Helper, "Stop", null, Signals.Finished, true, true, null),
        new(Helper, "Interrupt", null, Signals.Finished, false, true, InterruptNote),
        new(Helper, "SessionEnd", null, Signals.Ended, false, false, SessionEndNote),
    ];

    /// <summary>The row for an event name (exact spelling), or null for an event the island does not map.</summary>
    public static SignalRow? Find(string? eventName) => Rows.FirstOrDefault(r => r.Event == eventName);
}
