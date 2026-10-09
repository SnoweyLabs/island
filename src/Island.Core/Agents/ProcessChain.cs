namespace Island.Core;

/// <summary>Walks parent links into the chain of programs a process was started from, nearest first.</summary>
public static class ProcessChain
{
    /// <summary>
    /// The parent, the grandparent, and so on, from a map of process id to parent id. Stops at the top, at a
    /// process that is not in the map, at a repeated id (ids are reused, so a loop is possible) and after
    /// <see cref="AgentPipe.MaxChain"/> ids. The starting process itself is not included.
    /// </summary>
    public static IReadOnlyList<int> Build(int start, IReadOnlyDictionary<int, int> parentOf)
    {
        var chain = new List<int>();
        var seen = new HashSet<int> { start };
        var current = start;
        while (chain.Count < AgentPipe.MaxChain
               && parentOf.TryGetValue(current, out var parent)
               && parent > 0
               && seen.Add(parent))
        {
            chain.Add(parent);
            current = parent;
        }

        return chain;
    }
}
