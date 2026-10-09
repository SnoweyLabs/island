namespace Island.Core.Agents.Sessions;

// The part of the book that follows processes: choosing the process a session hangs on, noticing it is gone, and
// the rule that one helper process holds one session.
public sealed partial class SessionTracker
{
    /// <summary>
    /// One reading of the process list. <paramref name="aiWindowOwners"/>: the ids of the processes that own a window of
    /// an AI program. Chooses the process of every session that has none yet (the first reading after its first message:
    /// the nearest process of its chain whose file name is in the helper table, else the nearest one that is in that
    /// reading and in the next), removes the sessions whose process is gone (missing, or the same id with another file
    /// name or parent), and settles two sessions on one process (a new id replaces the old; an id-less one takes the id).
    /// </summary>
    public ReadingResult ApplyReading(IReadOnlyList<ProcessFact> processes, IReadOnlyCollection<int> aiWindowOwners)
    {
        lock (_gate)
        {
            var before = Fingerprint();
            var byId = new Dictionary<int, ProcessFact>();
            foreach (var p in processes) byId.TryAdd(p.Id, p);
            _aiOwners = aiWindowOwners.ToHashSet();

            var gone = new List<SessionInfo>();
            foreach (var e in _entries.Where(e => !e.Ended).ToList())
            {
                if (e.Hang is { } hung)
                {
                    if (byId.TryGetValue(hung.Id, out var now) && hung.Is(now)) continue;
                    gone.Add(Info(e));
                    _entries.Remove(e);
                }
                else if (!e.Resolved)
                {
                    Choose(e, byId);
                }
            }

            Settle();
            return new ReadingResult(Fingerprint() != before || gone.Count > 0, gone);
        }
    }

    /// <summary>
    /// The caller (section 2) found a helper process by its name and decided it counts. It is a session, idle, hanging
    /// on that process, unless one already does, or an unplaced session's chain holds the process (then that session
    /// takes it: a message and the name make one session). <paramref name="chain"/>: the process, its parent, and so on.
    /// Call it again at every reading; it adds nothing twice.
    /// </summary>
    public bool FoundByName(string helper, ProcessFact process, IReadOnlyList<int>? chain = null)
    {
        lock (_gate)
        {
            var before = Fingerprint();
            var name = HelperSignalTable.Normalize(helper);
            if (name.Length == 0 || _entries.Any(e => !e.Ended && e.Helper == name && process.Is(e.Hang))) return false;

            var waiting = _entries
                .Where(e => !e.Ended && e.Helper == name && e.Hang is null && e.Chain.Contains(process.Id))
                .OrderByDescending(e => e.Heard)
                .FirstOrDefault();
            if (waiting is null)
            {
                waiting = Create(name, "", (chain ?? [process.Id]).Where(p => p > 0).Take(SessionLimits.MaxChain).ToArray(), heard: false);
            }

            waiting.Hang = process;
            waiting.Resolved = true;
            waiting.Candidates = null;
            Settle();
            return Fingerprint() != before;
        }
    }

    private void Choose(Entry e, Dictionary<int, ProcessFact> byId)
    {
        if (e.Candidates is null)
        {
            // The first reading after the session's first message: a helper by name wins at once.
            foreach (var pid in e.Chain)
                if (byId.TryGetValue(pid, out var fact) && _helperExes.Contains(fact.ExeName))
                {
                    Pick(e, fact);
                    return;
                }

            // Otherwise wait for the next reading: a hook's own shell lives a fraction of a second.
            e.Candidates = e.Chain.Where(byId.ContainsKey).Select(pid => byId[pid]).ToList();
            if (e.Candidates.Count == 0) e.Resolved = true;
            return;
        }

        var kept = e.Candidates.FirstOrDefault(c => byId.TryGetValue(c.Id, out var again) && c.Is(again));
        if (kept is not null) Pick(e, kept);
        e.Resolved = true;
        e.Candidates = null;
    }

    private static void Pick(Entry e, ProcessFact fact)
    {
        e.Hang = fact;
        e.Resolved = true;
        e.Candidates = null;
    }

    /// <summary>
    /// One helper process holds one session, except a process that owns a window of an AI program. Two sessions on
    /// one process become one: with two different ids the one heard from last stays (the new id replaces the old);
    /// with no id on one of them the older session stays, takes the id and the newer state.
    /// </summary>
    private void Settle()
    {
        var groups = _entries
            .Where(e => !e.Ended && e.Hang is not null && !_aiOwners.Contains(e.Hang.Id))
            .GroupBy(e => (e.Helper, e.Hang!.Id))
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var group in groups)
        {
            var survivor = group.OrderBy(e => e.Id).First();
            foreach (var other in group.OrderBy(e => e.Id).Skip(1))
            {
                if (survivor.Sid.Length > 0 && other.Sid.Length > 0 && survivor.Sid != other.Sid)
                {
                    var winner = Newer(other, survivor);
                    _entries.Remove(winner == other ? survivor : other);
                    survivor = winner;
                    continue;
                }

                var newer = Newer(other, survivor);
                survivor.State = newer.State;
                survivor.Tool = newer.Tool;
                survivor.LastTime = Math.Max(survivor.LastTime, other.LastTime);
                survivor.Heard = Math.Max(survivor.Heard, other.Heard);
                if (survivor.Sid.Length == 0) survivor.Sid = other.Sid;
                survivor.Project = new[] { newer.Project, survivor.Project, other.Project }.FirstOrDefault(p => p.Length > 0) ?? "";
                if (newer.Chain.Length > 0) survivor.Chain = newer.Chain;
                _entries.Remove(other);
            }
        }
    }

    // The one that was heard from later by the sender's time, then by arrival.
    private static Entry Newer(Entry a, Entry b) =>
        a.LastTime != b.LastTime ? (a.LastTime > b.LastTime ? a : b) : (a.Heard >= b.Heard ? a : b);
}
