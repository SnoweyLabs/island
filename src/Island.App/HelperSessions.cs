using Island.Core;
using Island.Core.Agents.Sessions;
using Island.Core.Terminals;
using TermProcess = Island.Core.Terminals.ProcessFact;
using SessionProcess = Island.Core.Agents.Sessions.ProcessFact;

namespace Island.App;

/// <summary>
/// The conversations that helpers inside terminals reported through Island.Notify (WORK-ORDER-11 section 3), in memory only: the messages that arrive on the pipe
/// are applied to one <see cref="SessionTracker"/>, each reading of the process list (made by the Terminals page, only while it is laid out) settles which process a
/// session hangs on and which have ended, and what the page needs is handed over as the plain facts of <see cref="HelperSessionFact"/>. Nothing here is written anywhere:
/// no session id, project name, folder or process name reaches a log or a file.
/// </summary>
internal sealed class HelperSessions
{
    private readonly SessionTracker _tracker;
    private readonly TerminalTables _tables;
    private readonly Dictionary<string, string> _display;
    private readonly object _lock = new();

    /// <param name="tables">The tables of the Terminals page: which file names are helpers, and what each is called.</param>
    /// <param name="table">The signal table (every connected helper's rows).</param>
    public HelperSessions(TerminalTables tables, HelperSignalTable? table = null, Func<long>? clockMs = null)
    {
        _tables = tables;
        _display = tables.HelperPrograms.Select(h => h.HelperName).Distinct().ToDictionary(KeyOf, n => n);
        _tracker = new SessionTracker(clockMs ?? SessionTracker.SystemClockMs, table ?? AgentSignalTables.All, tables.HelperPrograms.Select(h => h.FileName));
    }

    /// <summary>Raised (from any thread) when what a shown session shows may have changed: the page is worked out again.</summary>
    public event Action? Changed;

    /// <summary>A message arrived on the pipe (any thread).</summary>
    public void Apply(SessionMessage message)
    {
        ApplyResult result;
        lock (_lock) result = _tracker.Apply(message);
        if (result.Changed) Changed?.Invoke();
    }

    /// <summary>
    /// One reading of the process list, from the page: the helpers found by name are told to the tracker, then it settles the processes of its sessions and ends the ones
    /// whose process is gone. <paramref name="aiWindowOwners"/> are the processes that own a window of an AI program.
    /// </summary>
    public void ApplyReading(IReadOnlyList<TermProcess> processes, IReadOnlyList<FoundHelper> byName, IReadOnlyCollection<int> aiWindowOwners)
    {
        ReadingResult result;
        var byId = new Dictionary<int, TermProcess>();
        foreach (var p in processes) byId.TryAdd(p.Id, p); // an id named twice counts once (the first), as everywhere else that reads this list
        lock (_lock)
        {
            foreach (var found in byName)
            {
                if (!byId.TryGetValue(found.ProcessId, out var process)) continue;
                _tracker.FoundByName(KeyOf(found.HelperName), new SessionProcess(process.Id, process.ParentId, process.ExeName), found.Chain);
            }

            result = _tracker.ApplyReading([.. processes.Select(p => new SessionProcess(p.Id, p.ParentId, p.ExeName))], aiWindowOwners);
        }

        if (result.Changed) Changed?.Invoke();
    }

    /// <summary>The shown sessions as the plain facts of the Terminals page.</summary>
    public IReadOnlyList<HelperSessionFact> Facts()
    {
        lock (_lock)
        {
            return [.. _tracker.Sessions().Select(s => new HelperSessionFact(
                NameOf(s.Helper), s.Project, s.Chain, s.Process?.Id ?? 0, StateOf(s.State), s.Id))];
        }
    }

    /// <summary>How many sessions are kept (for the self-test).</summary>
    public int Count
    {
        get
        {
            lock (_lock) return _tracker.Count;
        }
    }

    private static HelperState StateOf(SessionState state) => state switch
    {
        SessionState.Working => HelperState.Working,
        SessionState.NeedsYou => HelperState.NeedsYou,
        SessionState.Finished => HelperState.Finished,
        _ => HelperState.Idle,
    };

    // The wire carries a helper's name in lower case ("claude"), the page its words ("Claude Code"): one small table between them.
    private static readonly (string Key, string Name)[] Names =
    [
        (HelperNames.ClaudeCode, TerminalConstants.ClaudeCode),
        ("codex", TerminalConstants.Codex),
        ("antigravity", TerminalConstants.Antigravity),
        ("gemini", TerminalConstants.Gemini),
    ];

    // The page's words for a helper: the table's own name when it has one (a helper found by name), else the wire's own, else the key.
    private string NameOf(string key) => _display.TryGetValue(key, out var shown) ? shown : Names.FirstOrDefault(n => n.Key == key).Name ?? key;

    private static string KeyOf(string name) => Names.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)).Key ?? name.ToLowerInvariant();
}
