using System.Globalization;

namespace Island.Core.Agents.Sessions;

/// <summary>The six things the island understands, whatever a helper calls its events.</summary>
public enum HelperSignal
{
    Started,
    Working,
    NeedsYou,
    ToolDone,
    Finished,
    Ended,
}

/// <summary>
/// One line of a helper's table. <paramref name="Kind"/> null means any kind (the table's "any" and "-").
/// <paramref name="KindIsToolName"/>: the kind carries the tool's name (the table's "the tool's name").
/// <paramref name="RaisesNotice"/>: this event is also one of the two that raise the notice of WORK-ORDER-7 section 4.
/// </summary>
public sealed record SignalRow(string Event, string? Kind, HelperSignal Signal, bool KindIsToolName = false, bool RaisesNotice = false);

/// <summary>What a row says about one message. <paramref name="ToolName"/> is "" when the message names no tool.</summary>
public sealed record SignalMatch(HelperSignal Signal, string ToolName, bool RaisesNotice);

/// <summary>Helper names as the wire carries them (lower case).</summary>
public static class HelperNames
{
    /// <summary>Island.Notify's <c>--agent claude</c>; also what a version-1 message means.</summary>
    public const string ClaudeCode = "claude";
}

/// <summary>
/// ONE table from a helper's name, an event's name and its kind to a signal, or to nothing. Immutable: the main
/// session joins the tables of the other helpers with <see cref="With"/> (helper name -> rows). Claude Code's rows are
/// those of WORK-ORDER-11 section 3, each event name confirmed on code.claude.com/docs/en/hooks on 2026-10-07.
/// </summary>
public sealed class HelperSignalTable
{
    /// <summary>The rows of Claude Code. Notice events: Stop and Notification/permission_prompt, as <see cref="AgentSignals"/>.</summary>
    public static IReadOnlyList<SignalRow> ClaudeCodeRows { get; } =
    [
        new("SessionStart", null, HelperSignal.Started),
        new("UserPromptSubmit", null, HelperSignal.Working),
        new("PermissionRequest", null, HelperSignal.NeedsYou, KindIsToolName: true),
        new("Notification", "permission_prompt", HelperSignal.NeedsYou, RaisesNotice: true),
        new("PostToolUse", null, HelperSignal.ToolDone, KindIsToolName: true),
        new("PostToolUseFailure", null, HelperSignal.ToolDone, KindIsToolName: true),
        new("Stop", null, HelperSignal.Finished, RaisesNotice: true),
        new("StopFailure", null, HelperSignal.Finished),
        new("Notification", "idle_prompt", HelperSignal.Finished),
        new("SessionEnd", null, HelperSignal.Ended),
    ];

    private readonly IReadOnlyDictionary<string, IReadOnlyList<SignalRow>> _rows;

    private HelperSignalTable(IReadOnlyDictionary<string, IReadOnlyList<SignalRow>> rows) => _rows = rows;

    public static HelperSignalTable Empty { get; } = new(new Dictionary<string, IReadOnlyList<SignalRow>>());

    /// <summary>The table with Claude Code's rows only.</summary>
    public static HelperSignalTable Default { get; } = Empty.With(HelperNames.ClaudeCode, ClaudeCodeRows);

    public IReadOnlyCollection<string> Helpers => _rows.Keys.ToList();

    public IReadOnlyList<SignalRow> RowsOf(string? helper) =>
        _rows.TryGetValue(Normalize(helper), out var rows) ? rows : [];

    /// <summary>A new table that also holds (or replaces) the rows of one helper. A row with no event name is left out.</summary>
    public HelperSignalTable With(string helper, IEnumerable<SignalRow> rows)
    {
        var key = Normalize(helper);
        var copy = new Dictionary<string, IReadOnlyList<SignalRow>>(_rows);
        if (key.Length == 0) return this;
        copy[key] = rows.Where(r => !string.IsNullOrEmpty(r.Event)).ToList();
        return new HelperSignalTable(copy);
    }

    /// <summary>The first row whose event matches and whose kind matches; an exact kind is tried before "any kind". Null: nothing.</summary>
    public SignalMatch? Match(string? helper, string? hookEvent, string? kind)
    {
        if (string.IsNullOrEmpty(hookEvent) || !_rows.TryGetValue(Normalize(helper), out var rows)) return null;
        var kindText = kind ?? "";
        SignalRow? any = null;
        foreach (var row in rows)
        {
            if (!string.Equals(row.Event, hookEvent, StringComparison.Ordinal)) continue;
            if (row.Kind is null) { any ??= row; continue; }
            if (string.Equals(row.Kind, kindText, StringComparison.Ordinal)) return Of(row, kindText);
        }

        return any is null ? null : Of(any, kindText);
    }

    /// <summary>True when the event is one that raises the notice (Claude Code: Stop, Notification/permission_prompt).</summary>
    public bool RaisesNotice(string? helper, string? hookEvent, string? kind) => Match(helper, hookEvent, kind)?.RaisesNotice == true;

    private static SignalMatch Of(SignalRow row, string kind) =>
        new(row.Signal, row.KindIsToolName ? AgentText.Clean(kind).Trim() : "", row.RaisesNotice);

    /// <summary>Lower case, trimmed: the form a helper's name has in the table and in keys.</summary>
    public static string Normalize(string? helper) => (helper ?? "").Trim().ToLower(CultureInfo.InvariantCulture);

    /// <summary>
    /// A signal named as text (the other helpers' tables name signals so): case, spaces, hyphens and underscores
    /// are ignored, so "needs you", "NeedsYou" and "needs-you" are one name.
    /// </summary>
    public static bool TryParseSignal(string? text, out HelperSignal signal)
    {
        signal = default;
        if (text is null) return false;
        var plain = new string(text.Where(char.IsLetter).ToArray());
        return Enum.TryParse(plain, ignoreCase: true, out signal) && Enum.IsDefined(signal);
    }
}
