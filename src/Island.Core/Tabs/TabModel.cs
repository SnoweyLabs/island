namespace Island.Core;

/// <summary>Where a tab lives: which connection carries it, and its ids inside that browser.</summary>
public readonly record struct TabAddress(string ConnectionId, int TabId, int WindowId);

/// <summary>
/// Everything the add-on connections have reported, in memory only. Keys are "&lt;profile&gt;:&lt;tabId&gt;",
/// so two profiles with the same tab id never clash. Safe to call from several threads: every connection
/// reads on its own thread. Nothing here touches a file (EVALS I8).
/// </summary>
public sealed class TabModel
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Connection> _connections = [];
    private long _order;
    private long _playOrder;
    private IReadOnlyList<TabInfo> _tabs = [];

    /// <summary>True while at least one connection has said hello and is open.</summary>
    public bool Connected
    {
        get { lock (_lock) return _connections.Count > 0; }
    }

    /// <summary>How many connections have said hello and are open (a number only: the settings row "Browser add-on").</summary>
    public int ConnectionCount
    {
        get { lock (_lock) return _connections.Count; }
    }

    /// <summary>All tabs of all connections, newest (highest <see cref="TabInfo.LastActiveOrder"/>) first.</summary>
    public IReadOnlyList<TabInfo> Tabs
    {
        get { lock (_lock) return _tabs; }
    }

    public byte[]? IconPng(string tabKey)
    {
        lock (_lock) return Find(tabKey)?.Icon;
    }

    public TabAddress? Locate(string tabKey)
    {
        lock (_lock)
        {
            foreach (var (id, c) in _connections)
                if (Find(c, tabKey) is { } e)
                    return new TabAddress(id, e.Tab.Id, e.Tab.WindowId);
            return null;
        }
    }

    /// <summary>
    /// A connection said a good hello. A connection of the same profile that is still open is replaced (a
    /// restarted service worker dials again before the island notices the old socket died); its id is
    /// returned so the caller can close it.
    /// </summary>
    public string? Open(string connectionId, string profile)
    {
        lock (_lock)
        {
            var old = _connections.FirstOrDefault(p => p.Value.Profile == profile && p.Key != connectionId).Key;
            if (old is not null) _connections.Remove(old);
            _connections[connectionId] = new Connection(profile);
            Rebuild();
            return old;
        }
    }

    /// <summary>The connection closed: all of its tabs disappear. True when anything changed.</summary>
    public bool Close(string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.Remove(connectionId)) return false;
            Rebuild();
            return true;
        }
    }

    /// <summary>Applies one validated frame. True when the list of tabs, their media or their icons changed.</summary>
    public bool Apply(string connectionId, AddonMessage message)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(connectionId, out var c)) return false;
            var changed = message switch
            {
                SnapshotMessage s => Snapshot(c, s.Tabs),
                TabMessage t => Upsert(c, t.Tab),
                TabRemovedMessage r => c.Tabs.Remove(r.Id),
                TabActivatedMessage a => Activate(c, a.Id),
                IconMessage i => SetIcon(c, i),
                MediaMessage m => SetMedia(c, m),
                _ => false,
            };
            if (changed) Rebuild();
            return changed;
        }
    }

    /// <summary>
    /// Of the tabs whose host <paramref name="counts"/> (picked media sites, EVALS N7), the one whose page most
    /// recently started playing, whatever its state now (EVALS N2, N3); null when none has played.
    /// </summary>
    public TabInfo? LastStarted(Func<string, bool> counts)
    {
        lock (_lock)
        {
            var best = _connections.Values
                .SelectMany(c => c.Tabs.Values.Select(e => (c, e)))
                .Where(x => x.e.Media is not null && x.e.PlayOrder > 0 && counts(x.e.Tab.Host))
                .OrderByDescending(x => x.e.PlayOrder)
                .FirstOrDefault();
            return best.e is null ? null : Info(best.c, best.e);
        }
    }

    private bool Snapshot(Connection c, IReadOnlyList<TabObject> tabs)
    {
        var before = c.Tabs;
        c.Tabs = [];
        // Tabs already known keep their order; new ones get the next numbers, the active ones last (newest).
        foreach (var t in tabs.OrderBy(t => t.Active).Take(TabProtocol.MaxTabsPerConnection))
            c.Tabs[t.Id] = new Entry(t, before.TryGetValue(t.Id, out var old) && !(t.Active && !old.Tab.Active) ? old.Order : ++_order);
        return true;
    }

    private bool Upsert(Connection c, TabObject tab)
    {
        if (!c.Tabs.TryGetValue(tab.Id, out var e))
        {
            if (c.Tabs.Count >= TabProtocol.MaxTabsPerConnection) return false;
            c.Tabs[tab.Id] = new Entry(tab, ++_order);
            if (tab.Active) DeactivateSiblings(c, tab);
            return true;
        }

        if (e.Tab == tab) return false;
        if (e.Tab.Host != tab.Host)
        {
            // A tab that went to another site must not keep the old site's icon or track (EVALS I7).
            e.Icon = null;
            e.Media = null;
            e.PlayOrder = 0;
        }

        if (tab.Active && !e.Tab.Active)
        {
            e.Order = ++_order;
            DeactivateSiblings(c, tab);
        }

        e.Tab = tab;
        return true;
    }

    private bool Activate(Connection c, int id)
    {
        if (!c.Tabs.TryGetValue(id, out var e)) return false;
        e.Tab = e.Tab with { Active = true };
        e.Order = ++_order;
        DeactivateSiblings(c, e.Tab);
        return true;
    }

    private static void DeactivateSiblings(Connection c, TabObject active)
    {
        foreach (var other in c.Tabs.Values)
            if (other.Tab.Id != active.Id && other.Tab.WindowId == active.WindowId && other.Tab.Active)
                other.Tab = other.Tab with { Active = false };
    }

    private static bool SetIcon(Connection c, IconMessage i)
    {
        if (!c.Tabs.TryGetValue(i.Id, out var e)) return false;
        if (e.Icon is not null && e.Icon.AsSpan().SequenceEqual(i.Png)) return false;
        e.Icon = i.Png;
        return true;
    }

    private bool SetMedia(Connection c, MediaMessage m)
    {
        // Only the five media sites report what they play; anything else claiming to is ignored.
        if (!c.Tabs.TryGetValue(m.Id, out var e) || !TabProtocol.IsMediaHost(e.Tab.Host)) return false;
        if (e.Media == m.Media) return false;
        if (m.Media.State == PlaybackState.Playing && e.Media?.State != PlaybackState.Playing) e.PlayOrder = ++_playOrder;
        e.Media = m.Media;
        return true;
    }

    private Entry? Find(string tabKey)
    {
        foreach (var c in _connections.Values)
            if (Find(c, tabKey) is { } e) return e;
        return null;
    }

    private static Entry? Find(Connection c, string tabKey)
    {
        var prefix = c.Profile + ":";
        return tabKey.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(tabKey.AsSpan(prefix.Length), System.Globalization.NumberStyles.None, null, out var id)
               && c.Tabs.TryGetValue(id, out var e)
            ? e
            : null;
    }

    private void Rebuild() =>
        _tabs = [.. _connections.Values.SelectMany(c => c.Tabs.Values.Select(e => Info(c, e))).OrderByDescending(t => t.LastActiveOrder)];

    private static TabInfo Info(Connection c, Entry e) =>
        new($"{c.Profile}:{e.Tab.Id}", e.Tab.WindowId, e.Tab.Id, e.Tab.Title, e.Tab.Host, e.Tab.Audible, e.Tab.Active, e.Order, e.Media);

    private sealed class Connection(string profile)
    {
        public string Profile { get; } = profile;

        public Dictionary<int, Entry> Tabs { get; set; } = [];
    }

    private sealed class Entry(TabObject tab, long order)
    {
        public TabObject Tab { get; set; } = tab;

        public long Order { get; set; } = order;

        public TabMedia? Media { get; set; }

        public byte[]? Icon { get; set; }

        /// <summary>When the page last went from not playing to playing; 0 when it never did.</summary>
        public long PlayOrder { get; set; }
    }
}
