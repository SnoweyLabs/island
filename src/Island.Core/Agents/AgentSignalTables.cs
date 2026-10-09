using Island.Core.Agents.Connect;
using Island.Core.Agents.Sessions;
using ConnectRow = Island.Core.Agents.Connect.SignalRow;
using SessionRow = Island.Core.Agents.Sessions.SignalRow;

namespace Island.Core;

/// <summary>
/// The one signal table the app and Island.Notify use (WORK-ORDER-11 section 3): Claude Code's rows, joined with the rows of every other helper that is
/// connected (Codex today; Gemini and Antigravity's terminal program are blocked, see STATE.md). Pure data.
/// </summary>
public static class AgentSignalTables
{
    /// <summary>The events whose kind is the tool's name (the tool that asked, or finished) rather than a notification's kind.</summary>
    private static readonly string[] ToolEvents = ["PermissionRequest", "PostToolUse"];

    /// <summary>Every helper's rows in one table.</summary>
    public static HelperSignalTable All { get; } = HelperSignalTable.Default.With(CodexSignals.Helper, CodexSignals.Rows.Select(Convert));

    private static SessionRow Convert(ConnectRow row) =>
        new(row.Event, row.Kind, HelperSignalTable.TryParseSignal(row.Signal, out var signal) ? signal : HelperSignal.Started,
            KindIsToolName: ToolEvents.Contains(row.Event), RaisesNotice: row.RaisesNotice);

    /// <summary>The kind Island.Notify sends for an input: a notification's kind when it has one, else the tool's name.</summary>
    public static string KindOf(HookInput input) => input.NotificationType.Length > 0 ? input.NotificationType : input.ToolName;
}
