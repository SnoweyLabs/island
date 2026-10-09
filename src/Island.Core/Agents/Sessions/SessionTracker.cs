namespace Island.Core.Agents.Sessions;

/// <summary>
/// The book of sessions (WORK-ORDER-11 section 3, "A session and its state"): pure logic over plain facts, all in
/// memory, nothing read from Windows. Every public method takes one lock, so the pipe's thread and the thread that
/// reads the process list may both call it. Sessions are kept in a plain list: there are at most
/// <see cref="SessionLimits.MaxSessions"/>, so a scan is the simplest correct lookup.
/// </summary>
public sealed partial class SessionTracker
{
    private readonly object _gate = new();
    private readonly Func<long> _clockMs;
    private readonly HelperSignalTable _table;
    private readonly HashSet<string> _helperExes;
    private readonly List<Entry> _entries = [];
    private HashSet<int> _aiOwners = [];
    private long _nextId;
    private long _heardCounter;

    /// <param name="steadyClockMs">The island's own reading of the steady counter, in milliseconds (see <see cref="SystemClockMs"/>).</param>
    /// <param name="table">Which events mean what (<see cref="HelperSignalTable.Default"/> is Claude Code's).</param>
    /// <param name="helperExeNames">The table of terminal helpers' file names (section 2), handed in as data.</param>
    public SessionTracker(Func<long> steadyClockMs, HelperSignalTable table, IEnumerable<string> helperExeNames)
    {
        _clockMs = steadyClockMs;
        _table = table;
        _helperExes = new HashSet<string>(helperExeNames.Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Milliseconds since Windows started (Environment.TickCount64). Island.Notify must take its own reading from the
    /// same counter, minus the age of its process, or the order of messages is meaningless.
    /// </summary>
    public static long SystemClockMs() => Environment.TickCount64;

    /// <summary>Applies one message. A version-1 message has a null time and takes the island's reading at arrival.</summary>
    public ApplyResult Apply(SessionMessage message)
    {
        lock (_gate)
        {
            var match = _table.Match(message.Helper, message.Event, message.Kind);
            if (match is null) return new ApplyResult(ApplyOutcome.Ignored, 0, null, false);

            var before = Fingerprint();
            var helper = HelperSignalTable.Normalize(message.Helper);
            var now = _clockMs();
            var time = message.Time is { } sent ? Math.Min(Math.Max(sent, 0), now) : now; // a time from the future counts as now
            var sessionId = AgentText.Head(AgentText.Clean(message.SessionId).Trim(), SessionLimits.MaxSessionIdChars);
            var chain = message.Chain.Where(p => p > 0).Take(SessionLimits.MaxChain).ToArray();

            var entry = Find(helper, sessionId, chain, out var inherited);
            if (entry is { Ended: true })
            {
                // An earlier message (or one at the very moment of the end) never brings it back; a later one starts it anew.
                if (time <= entry.EndTime) return new ApplyResult(ApplyOutcome.Dropped, 0, match.Signal, Fingerprint() != before);
                _entries.Remove(entry);
                entry = null;
            }

            var isNew = entry is null;
            entry ??= Create(helper, sessionId, chain);
            if (isNew && inherited is not null)
            {
                entry.Hang = inherited; // the new id is on the process the old one hung on
                entry.Resolved = true;
            }

            entry.Heard = ++_heardCounter;
            if (!isNew && time < entry.LastTime) return new ApplyResult(ApplyOutcome.Dropped, entry.Id, match.Signal, Fingerprint() != before);

            if (entry.Sid.Length == 0) entry.Sid = sessionId; // a session with no id takes the id of the first message that hangs on its process
            if (chain.Length > 0) entry.Chain = chain;
            if (ProjectName.From(message.Folder) is { Length: > 0 } project) entry.Project = project;
            ApplySignal(entry, match);
            entry.LastTime = time;
            if (match.Signal == HelperSignal.Ended)
            {
                entry.Ended = true;
                entry.EndTime = time;
            }

            return new ApplyResult(ApplyOutcome.Applied, entry.Id, match.Signal, Fingerprint() != before);
        }
    }

    /// <summary>
    /// The sessions that are shown, oldest first. A session is shown when it hangs on a process, or when its chain
    /// holds a process that owns a window of an AI program (the set of the last reading).
    /// </summary>
    public IReadOnlyList<SessionInfo> Sessions()
    {
        lock (_gate) return _entries.Where(IsShown).OrderBy(e => e.Id).Select(Info).ToList();
    }

    public SessionInfo? Get(long id)
    {
        lock (_gate) return _entries.Where(e => e.Id == id && !e.Ended).Select(Info).FirstOrDefault();
    }

    /// <summary>Sessions held, remembered ends included (what the limit counts).</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>The most urgent state of the sessions with these ids: needs you, then working, then finished, then idle. Null: none of them is known.</summary>
    public SessionState? MostUrgentOf(IEnumerable<long> sessionIds)
    {
        lock (_gate)
        {
            var ids = sessionIds.ToHashSet();
            return MostUrgent(_entries.Where(e => !e.Ended && ids.Contains(e.Id)).Select(e => e.State));
        }
    }

    public static SessionState? MostUrgent(IEnumerable<SessionState> states)
    {
        SessionState? best = null;
        foreach (var s in states)
            if (best is null || Rank(s) > Rank(best.Value)) best = s;
        return best;
    }

    private static int Rank(SessionState s) => s switch
    {
        SessionState.NeedsYou => 3,
        SessionState.Working => 2,
        SessionState.Finished => 1,
        _ => 0,
    };

    // ---- applying one signal ----

    private static void ApplySignal(Entry e, SignalMatch m)
    {
        switch (m.Signal)
        {
            case HelperSignal.Working:
                e.State = SessionState.Working;
                e.Tool = "";
                break;
            case HelperSignal.NeedsYou:
                e.State = SessionState.NeedsYou;
                if (m.ToolName.Length > 0) e.Tool = m.ToolName; // a nameless one keeps the name already held
                break;
            case HelperSignal.ToolDone:
                if (e.State == SessionState.Idle) e.State = SessionState.Working;
                else if (e.State == SessionState.NeedsYou && (m.ToolName.Length == 0 || e.Tool.Length == 0 || m.ToolName == e.Tool))
                {
                    e.State = SessionState.Working;
                    e.Tool = "";
                }

                break; // working and finished stay as they are: a late one never revives a finished session
            case HelperSignal.Finished:
                e.State = SessionState.Finished;
                e.Tool = "";
                break;
            // Started changes nothing more; Ended is handled by the caller.
        }
    }

    // ---- finding and making sessions ----

    private Entry? Find(string helper, string sessionId, int[] chain, out ProcessFact? inherited)
    {
        inherited = null;
        Entry? byId = sessionId.Length > 0 ? _entries.FirstOrDefault(e => e.Helper == helper && e.Sid == sessionId) : null;
        if (byId is not null) return byId;

        Entry? live = null, ended = null;
        var replaced = new List<Entry>();
        foreach (var e in _entries.Where(e => e.Helper == helper && SameProcess(e, sessionId, chain)))
        {
            if (e.Ended)
            {
                if ((e.Sid.Length == 0 || sessionId.Length == 0) && (ended is null || e.Heard > ended.Heard)) ended = e;
            }
            else if (e.Sid.Length > 0 && sessionId.Length > 0)
            {
                // A new id on the same process replaces the old session. Only a process that is chosen counts: before
                // that the first process of the chain proves nothing (an AI program's many conversations share it);
                // the reading settles two such sessions when it chooses.
                if (e.Hang is not null) replaced.Add(e);
            }
            else if (live is null || e.Heard > live.Heard) live = e;
        }

        foreach (var r in replaced) _entries.Remove(r);
        inherited = replaced.Select(r => r.Hang).FirstOrDefault();
        return live ?? ended;
    }

    /// <summary>Does the message come from the process this session hangs on (or, before it is chosen, from the first process of its chain)?</summary>
    private bool SameProcess(Entry e, string sessionId, int[] chain)
    {
        var first = e.Chain.Length > 0 ? e.Chain[0] : 0;
        var anchor = e.Hang?.Id ?? first;
        if (anchor == 0) return chain.Length == 0;
        if (_aiOwners.Contains(anchor))
            return e.Sid.Length == 0 && sessionId.Length == 0 && chain.Length > 0 && chain[0] == first; // an AI program holds many conversations: no sharing of one id
        return e.Hang is not null ? chain.Contains(anchor) : chain.Length > 0 && chain[0] == anchor;
    }

    /// <summary>
    /// A session made by a message counts as just heard. One made by a helper's name alone (<paramref name="heard"/> false) was never heard from: it ranks below every session that
    /// was, so a hundred helpers that have said nothing never push out the sessions that have a state (and may go first themselves; they are made again at the next reading).
    /// </summary>
    private Entry Create(string helper, string sessionId, int[] chain, bool heard = true)
    {
        var entry = new Entry { Id = ++_nextId, Helper = helper, Sid = sessionId, Chain = chain, Heard = heard ? ++_heardCounter : 0 };
        _entries.Add(entry);
        while (_entries.Count > SessionLimits.MaxSessions)
            _entries.Remove((heard ? _entries.Where(e => e != entry) : _entries).MinBy(e => e.Heard)!);
        return entry;
    }

    // ---- reading out ----

    private bool IsShown(Entry e) => !e.Ended && (e.Hang is not null || e.Chain.Any(_aiOwners.Contains));

    private SessionInfo Info(Entry e)
    {
        var shared = e.Hang is not null && _aiOwners.Contains(e.Hang.Id);
        var key = e.Hang is not null && !shared ? $"{e.Helper}|h{e.Hang.Id}"
            : e.Sid.Length > 0 ? $"{e.Helper}|s{e.Sid}"
            : $"{e.Helper}|p{(e.Chain.Length > 0 ? e.Chain[0] : 0)}";
        return new SessionInfo(e.Id, key, e.Helper, e.Sid, e.Project, e.Chain, e.State, e.Tool, e.Hang, IsShown(e));
    }

    // What a tile can show, for "did anything change": one tuple per shown session.
    private (long, SessionState, string, string)[] Fingerprint() =>
        _entries.Where(IsShown).OrderBy(e => e.Id).Select(e => (e.Id, e.State, e.Tool, e.Project)).ToArray();

    private sealed class Entry
    {
        public long Id;
        public string Helper = "";
        public string Sid = "";
        public string Project = "";
        public int[] Chain = [];
        public SessionState State = SessionState.Idle;
        public string Tool = "";
        public long LastTime = long.MinValue;
        public long Heard;
        public bool Ended;
        public long EndTime;
        public ProcessFact? Hang;
        public bool Resolved; // the process has been chosen, or there was none to choose
        public List<ProcessFact>? Candidates; // the chain's processes of the first reading, waiting for the next
    }
}
