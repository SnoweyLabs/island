namespace Island.Core.Terminals;

/// <summary>A helper process found by name: its id, the helper's name, and the chain (the process itself first, then its parent, and so on).</summary>
public sealed record FoundHelper(int ProcessId, string HelperName, IReadOnlyList<int> Chain);

/// <summary>
/// A helper as the page sees it: found by name, or reported by a hook, or both. <see cref="Key"/> is its identity in memory
/// (the process id; a negative number for a session with no process).
/// </summary>
public sealed record HelperInstance(long Key, string HelperName, int ProcessId, string ProjectName, HelperState State, bool FromHook, IReadOnlyList<int> Chain);

/// <summary>WORK-ORDER-11 section 2: helpers found by name, and which window a helper is in. Pure; never throws on odd input.</summary>
public static class HelperFinder
{
    /// <summary>
    /// The processes whose file name is in the table of helpers, minus two exceptions: going up the chain from the process itself, the
    /// nearest process that owns a window of the page decides, and if that is a window of an AI program the process is not a helper (Claude's
    /// own program is also called claude.exe and runs Claude Code inside itself); and a helper that has another helper among its ancestors
    /// is not counted (it belongs to that one). In the order of the process list; a process id that appears twice counts once (the first).
    /// </summary>
    public static IReadOnlyList<FoundHelper> FindByName(IReadOnlyList<ProcessFact>? processes, PageWindows page, TerminalTables tables)
    {
        var parentOf = new Dictionary<int, int>();
        var candidates = new List<(ProcessFact Process, string Name)>();
        foreach (var p in processes ?? [])
        {
            if (p is null || p.Id <= 0 || !parentOf.TryAdd(p.Id, p.ParentId)) continue;
            if (tables.HelperOf(p.ExeName) is { } row) candidates.Add((p, row.HelperName));
        }

        var counted = new List<FoundHelper>();
        foreach (var (process, name) in candidates)
        {
            var chain = ChainOf(process.Id, parentOf);
            if (!UnderAiProgramWindow(chain, page)) counted.Add(new FoundHelper(process.Id, name, chain));
        }

        var chainOf = counted.ToDictionary(h => h.ProcessId, h => h.Chain);
        return [.. counted.Where(h => !h.Chain.Skip(1).Any(a => IsOuter(a, h.ProcessId, chainOf)))];
    }

    // An ancestor that is a helper counted by name makes this one part of it, unless the two are each other's ancestors (a parent id that Windows gave to a newer process makes a loop):
    // then the one with the lower id is the outer one, so a loop never takes both away.
    private static bool IsOuter(int ancestor, int self, Dictionary<int, IReadOnlyList<int>> chainOf) =>
        chainOf.TryGetValue(ancestor, out var theirs) && (!theirs.Skip(1).Contains(self) || ancestor < self);

    /// <summary>The process itself, then its parent, and so on: stops at a loop, at the top, and after <see cref="AgentPipe.MaxChain"/> parents.</summary>
    public static IReadOnlyList<int> ChainOf(int processId, IReadOnlyDictionary<int, int> parentOf) =>
        [processId, .. ProcessChain.Build(processId, parentOf)];

    private static bool UnderAiProgramWindow(IReadOnlyList<int> chain, PageWindows page)
    {
        foreach (var pid in chain)
        {
            var own = page.OwnedBy(pid);
            if (own.Count > 0) return own.Any(w => page.Roles[w.Handle] == WindowRole.AiProgram);
        }

        return false;
    }

    /// <summary>
    /// Which window of the page a helper is in, from its chain (the process itself first). The four rules, in order:
    /// 1. the nearest process of the chain that owns a console window: for a hidden PseudoConsoleWindow the window that owns it, for a
    ///    ConsoleWindowClass window that window itself, if that window is a terminal window of the page;
    /// 2. else TerminalChoice.Choose, limited to the page's terminal windows;
    /// 3. else, only for a session a hook reported: the nearest process of the chain that owns a window of an AI program, that window;
    /// 4. else none (the handle is null).
    /// </summary>
    public static long? WindowOf(IReadOnlyList<int>? chain, string? projectName, bool reportedByHook, IReadOnlyList<ConsoleWindowFact>? consoleWindows, PageWindows page)
    {
        var ids = Normalize(chain);
        if (ids.Count == 0) return null;

        if (ConsoleWindowOf(ids, consoleWindows, page) is { } console) return console;

        var project = TerminalClassify.CleanName(projectName);
        if (TerminalChoice.Choose(ids, [.. page.Terminals.Select(AsChoiceFact)], project) is { } chosen) return chosen.Handle;

        if (reportedByHook && TerminalChoice.Choose(ids, [.. page.AiWindows.Select(AsChoiceFact)], project) is { } ai) return ai.Handle;

        return null;
    }

    /// <summary>The chain without ids that are not processes (0, negative) and without repeats, cut to <see cref="TerminalConstants.MaxChainUsed"/>.</summary>
    public static List<int> Normalize(IReadOnlyList<int>? chain)
    {
        var ids = new List<int>();
        var seen = new HashSet<int>();
        foreach (var id in chain ?? [])
        {
            if (ids.Count >= TerminalConstants.MaxChainUsed) break;
            if (id > 0 && seen.Add(id)) ids.Add(id);
        }

        return ids;
    }

    // Titles are cut before they are compared with a project's name: a title can be a megabyte long.
    private static WindowFact AsChoiceFact(TermWindowFact w) =>
        new(w.Handle, w.OwnerProcessId, AgentText.Head(w.Title ?? "", TerminalConstants.TitleReadLimit), w.RecencyRank);

    // The nearest process that owns a console window decides: if none of its console windows leads to a terminal window of the page, rule 1 gives nothing.
    private static long? ConsoleWindowOf(IReadOnlyList<int> chain, IReadOnlyList<ConsoleWindowFact>? consoleWindows, PageWindows page)
    {
        foreach (var pid in chain)
        {
            var own = (consoleWindows ?? []).Where(c => c is not null && c.OwnerProcessId == pid).ToList();
            if (own.Count == 0) continue;

            foreach (var c in own)
            {
                var target = TargetOf(c);
                if (target is { } handle && page.TerminalByHandle(handle) is not null) return handle;
            }

            return null;
        }

        return null;
    }

    private static long? TargetOf(ConsoleWindowFact c)
    {
        if (string.Equals(c.ClassName, TerminalConstants.PseudoConsoleClass, StringComparison.OrdinalIgnoreCase)) return c.OwnerWindowHandle != 0 ? c.OwnerWindowHandle : null;
        if (string.Equals(c.ClassName, TerminalConstants.ClassicConsoleClass, StringComparison.OrdinalIgnoreCase)) return c.Handle;
        return null;
    }

    // Below every negated process id (those are above -2^31).
    private const long EmptyChainKeyBase = -(1L << 40);

    // Above every process id.
    private const long SharedProcessKeyBase = 1L << 40;

    /// <summary>
    /// The helpers of one reading: those found by name and those a hook reported, a session that hangs on a helper found by name being that
    /// helper (its project and state are used). A session whose process is not found by name is a helper of its own. In the order found,
    /// then the sessions; a session heard from later replaces one heard from earlier on the same process.
    /// </summary>
    public static IReadOnlyList<HelperInstance> Collect(TerminalReading reading, PageWindows page, TerminalTables tables)
    {
        var found = FindByName(reading.Processes, page, tables);
        var byPid = new Dictionary<int, HelperInstance>();
        var order = new List<long>();
        var instances = new Dictionary<long, HelperInstance>();

        foreach (var f in found)
        {
            var key = (long)f.ProcessId;
            instances[key] = new HelperInstance(key, f.HelperName, f.ProcessId, "", HelperState.Idle, false, f.Chain);
            order.Add(key);
            byPid[f.ProcessId] = instances[key];
        }

        var sessions = (reading.Sessions ?? []).Where(s => s is not null).OrderBy(s => s.LastHeard).ToList();
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var name = TerminalClassify.CleanName(s.HelperName);
            var project = TerminalClassify.CleanName(s.ProjectName);
            if (name.Length == 0) name = byPid.TryGetValue(s.HangsOnProcessId, out var named) ? named.HelperName : TerminalConstants.UnknownTerminalName;

            if (s.HangsOnProcessId > 0 && byPid.TryGetValue(s.HangsOnProcessId, out var same))
            {
                instances[same.Key] = same with { HelperName = name, ProjectName = project, State = s.State, FromHook = true };
                continue;
            }

            var chain = s.HangsOnProcessId > 0 ? Normalize([s.HangsOnProcessId, .. s.Chain ?? []]) : Normalize(s.Chain);
            if (chain.Any(byPid.ContainsKey)) continue; // a helper inside a helper belongs to the outer one (the same exception as for a helper found by name)

            // No process: the first id of its chain stands in, negated, so the key stays the same from one reading to the next; with no chain either, the session's own id does.
            // A session that hangs on a process that is no helper of its own (the Claude program, which holds many conversations in one process) is its own helper: the session's id, not the process, is its key.
            var key = s.HangsOnProcessId > 0 ? SharedProcessKeyBase + s.LastHeard : chain.Count > 0 ? -(long)chain[0] : EmptyChainKeyBase - s.LastHeard;
            if (!instances.ContainsKey(key)) order.Add(key);
            instances[key] = new HelperInstance(key, name, Math.Max(s.HangsOnProcessId, 0), project, s.State, true, chain);
        }

        return [.. order.Select(k => instances[k])];
    }
}
