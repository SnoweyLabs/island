namespace Island.Core;

/// <summary>
/// One visible top-level window as a plain fact. <paramref name="Handle"/> is opaque here (the app fills it with the
/// window handle); <paramref name="RecencyRank"/> is 0 for the most recently used window, 1 for the next, and so on.
/// </summary>
public sealed record WindowFact(long Handle, int OwnerProcessId, string Title, int RecencyRank);

/// <summary>Which window comes forward when the notice is clicked (A2). Titles are read in memory for this and nothing else.</summary>
public static class TerminalChoice
{
    /// <summary>
    /// The nearest program in the chain that owns a visible window; among its windows the one whose title contains
    /// the project's name if exactly one does, otherwise its most recently used window. Null when nothing is found.
    /// </summary>
    public static WindowFact? Choose(IReadOnlyList<int> chain, IReadOnlyList<WindowFact> windows, string projectName)
    {
        foreach (var pid in chain)
        {
            var own = windows.Where(w => w.OwnerProcessId == pid).ToList();
            if (own.Count == 0) continue;

            if (projectName.Length > 0)
            {
                var named = own.Where(w => w.Title.Contains(projectName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (named.Count == 1) return named[0];
            }

            return own.MinBy(w => w.RecencyRank);
        }

        return null;
    }
}
