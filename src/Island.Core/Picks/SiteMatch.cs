namespace Island.Core;

/// <summary>
/// A website pick means the whole site (youtube.com), not one page of it. music.youtube.com is a different
/// site from youtube.com. The leading "www." is not part of the site.
/// </summary>
public static class SiteMatch
{
    /// <summary>The host in lower case without a leading "www." and without a trailing dot.</summary>
    public static string Normalize(string host)
    {
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }

    /// <summary>The normalized host of an address such as "https://www.youtube.com/watch?v=1", or null when it has none.</summary>
    public static string? HostOf(string? address)
    {
        return SiteAddress.HostOf(address) is { } host ? Normalize(host) : null;
    }

    /// <summary>True when a tab on <paramref name="tabHost"/> is a tab of the picked site.</summary>
    public static bool Matches(string pickHost, string? tabHost) =>
        tabHost is not null && Normalize(pickHost) == Normalize(tabHost);
}

/// <summary>
/// Which of a pick's windows or tabs a click goes to: the newest first, and clicking again goes to the next one that this
/// round has not visited, then round to the newest. A window is remembered by what it IS (its handle, a tab's key), not by
/// where it stands in a list: bringing a window forward changes the order, and activating a tab changes its number, but
/// neither changes who was already visited. The memory is per pick and in memory only.
/// </summary>
public sealed class ClickCycler
{
    private readonly Dictionary<string, HashSet<string>> _visited = [];

    /// <param name="pickId">The pick being clicked.</param>
    /// <param name="targetsNewestFirst">Its windows or tabs, newest first. Empty gives <c>null</c>.</param>
    public long? Next(string pickId, IReadOnlyList<long> targetsNewestFirst) =>
        Next(pickId, targetsNewestFirst, [.. targetsNewestFirst.Select(t => t.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

    /// <param name="identities">What each target is, parallel to <paramref name="targetsNewestFirst"/> (the same value for the same window or tab however its number changes).</param>
    public long? Next(string pickId, IReadOnlyList<long> targetsNewestFirst, IReadOnlyList<string> identities)
    {
        if (targetsNewestFirst.Count == 0 || identities.Count != targetsNewestFirst.Count)
        {
            _visited.Remove(pickId);
            return null;
        }

        if (!_visited.TryGetValue(pickId, out var seen)) _visited[pickId] = seen = [];
        seen.IntersectWith(identities); // windows that are gone are forgotten

        var at = -1;
        for (var i = 0; i < identities.Count; i++)
        {
            if (seen.Contains(identities[i])) continue;
            at = i;
            break;
        }

        if (at < 0)
        {
            seen.Clear(); // everything was visited: a new round, from the newest
            at = 0;
        }

        seen.Add(identities[at]);
        return targetsNewestFirst[at];
    }

    /// <summary>Forgets where a pick's cycle was, so the next click goes to the newest again.</summary>
    public void Reset(string pickId) => _visited.Remove(pickId);
}
