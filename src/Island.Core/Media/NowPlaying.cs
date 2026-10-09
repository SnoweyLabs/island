namespace Island.Core;

/// <summary>
/// Which item is "now playing" (EVALS N1 to N7, N11). Fed with the whole picture each time something is reported
/// (desktop sessions, browser tabs, the policy of which sources count) and the time as a value; it never reads
/// the clock and never sends anything. The item that most recently started or resumed playing wins; when all
/// are paused the last one stays, marked paused; a source that vanishes for a moment keeps the line for
/// <see cref="VanishGrace"/>. All data stays in memory. Safe to call from any thread.
/// </summary>
public sealed class NowPlaying
{
    /// <summary>A session that is gone for less than this (between tracks) does not blank the line.</summary>
    public static readonly TimeSpan VanishGrace = TimeSpan.FromSeconds(4);

    /// <summary>A pause this short before playing again is a player changing track, not a person resuming: it does not take the line.</summary>
    public static readonly TimeSpan ResumeBlink = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// When a report notices a source missing, the grace counts from that source's last report if it came lately; after a
    /// longer silence (a steady player is reported rarely) the last report says nothing, so it counts from now.
    /// </summary>
    private static readonly TimeSpan QuietGap = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly Dictionary<string, Track> _tracks = [];
    private long _startCounter;
    private Held? _held;

    /// <summary>Takes the latest picture and returns what the line shows now (null: nothing has played, or it is long gone).</summary>
    public NowPlayingView? Update(
        DateTimeOffset now,
        IReadOnlyList<MediaSessionInfo> sessions,
        IReadOnlyList<TabInfo> tabs,
        bool tabsConnected,
        NowPlayingPolicy policy)
    {
        lock (_lock)
        {
            var candidates = Candidates(sessions, tabs, tabsConnected, policy);
            // Two reports with one key in one picture are one source (the one that plays counts).
            candidates = [.. candidates.GroupBy(c => c.Key).Select(g => g.FirstOrDefault(c => c.State == PlaybackState.Playing) ?? g.First())];
            var started = NoteStates(candidates, now);
            Choose(candidates, started, now);
            return ViewAt(now);
        }
    }

    /// <summary>The line as of <paramref name="now"/>; the progress moves on while it plays. Null when there is nothing to show.</summary>
    public NowPlayingView? Current(DateTimeOffset now)
    {
        lock (_lock) return ViewAt(now);
    }

    /// <summary>
    /// Previous, play/pause and next act on the Now-playing item and nothing else (EVALS N4). Null when there is
    /// no item or its source is gone at this moment.
    /// </summary>
    public MediaCommandPlan? PlanFor(MediaCommand command)
    {
        lock (_lock) return _held is { Present: true } h ? new MediaCommandPlan(h.Data.Target, command) : null;
    }

    private List<Candidate> Candidates(IReadOnlyList<MediaSessionInfo> sessions, IReadOnlyList<TabInfo> tabs, bool tabsConnected, NowPlayingPolicy policy)
    {
        var list = new List<Candidate>();
        var fallbackTab = tabsConnected && policy.BrowserSessionCounts ? SilentAudibleTab(tabs, policy) : null;
        // A tab does not say which browser it belongs to, and a session is never matched to a browser by a program's name: so the fallback needs
        // exactly one browser session that says it is playing. With two, Windows' words could belong to either: they are ignored.
        var oneBrowserPlaying = sessions.Count(s => s.IsBrowser && s.State == PlaybackState.Playing) == 1;
        foreach (var s in sessions)
        {
            if (s.IsBrowser && tabsConnected)
            {
                // WORK-ORDER-9 section 3: with the add-on connected a tab's own report is better (it knows which tab), so the browser's session is ignored;
                // except when a tab on a picked site is making sound and has sent no report at all, and exactly one browser session says it is playing:
                // then Windows' words fill the line, shown for the tab used most recently.
                if (fallbackTab is not null && oneBrowserPlaying && s.State == PlaybackState.Playing)
                {
                    list.Add(new Candidate($"tab:{fallbackTab.Key}", new MediaTarget(MediaTargetKind.Session, s.SessionId), MediaNames.Site(fallbackTab.Host),
                        s.SourceApp, fallbackTab.Host, true, s.Title, s.Artist, s.State, Position(s.PositionSeconds), Finite(s.LengthSeconds), fallbackTab.LastActiveOrder + 1, s.Timeline, null,
                        fallbackTab.Key));
                }

                continue;
            }

            var counts = s.IsBrowser ? policy.BrowserSessionCounts && !tabsConnected : policy.DesktopPlayersCount;
            if (!counts) continue;
            list.Add(new Candidate($"session:{s.SessionId}", new MediaTarget(MediaTargetKind.Session, s.SessionId), MediaNames.App(s.SourceApp),
                s.SourceApp, null, s.IsBrowser, s.Title, s.Artist, s.State, Position(s.PositionSeconds), Finite(s.LengthSeconds), 0, s.Timeline, null));
        }

        foreach (var t in tabs)
        {
            if (t.Media is not { } m || !policy.PickedHosts.Any(h => SiteMatch.Matches(h, t.Host))) continue;
            list.Add(new Candidate($"tab:{t.Key}", new MediaTarget(MediaTargetKind.Tab, t.Key), MediaNames.Site(t.Host),
                null, t.Host, false, m.Title ?? t.Title, m.Artist, m.State, Position(m.PositionSeconds), Finite(m.LengthSeconds), t.LastActiveOrder + 1, null,
                new TabTiming(m.PositionSeconds, m.LengthSeconds, m.Rate, m.ReadAtMs)));
        }

        return list;
    }

    /// <summary>
    /// The tab the fallback speaks for: the most recently used tab on a picked site that is making sound and has sent no report about what it plays at all.
    /// A tab that has reported (playing, paused or stopped) speaks for itself and is never a reason for the fallback. Null when there is none.
    /// </summary>
    private static TabInfo? SilentAudibleTab(IReadOnlyList<TabInfo> tabs, NowPlayingPolicy policy) =>
        tabs.Where(t => t.Audible && t.Media is null && policy.PickedHosts.Any(h => SiteMatch.Matches(h, t.Host))).MaxBy(t => t.LastActiveOrder);

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    /// <summary>A position is a number of seconds from the start: below zero it is no position.</summary>
    private static double? Position(double? value) => Finite(value) is { } v && v >= 0 ? v : null;

    /// <summary>Notes each source's state; returns the ones that started or resumed in this report, oldest first.</summary>
    private List<Candidate> NoteStates(List<Candidate> candidates, DateTimeOffset now)
    {
        var started = new List<Candidate>();
        foreach (var c in candidates.OrderBy(c => c.Order))
        {
            if (!_tracks.TryGetValue(c.Key, out var track)) _tracks[c.Key] = track = new Track();
            if (c.State == PlaybackState.Playing && track.State != PlaybackState.Playing)
            {
                // A pause that lasted only a moment is a player changing track: not a new start.
                var blink = track.PausedAt is { } at && now - at < ResumeBlink;
                if (!blink)
                {
                    track.StartOrder = ++_startCounter;
                    started.Add(c);
                }
            }

            if (c.State != PlaybackState.Playing && track.State == PlaybackState.Playing) track.PausedAt = now;
            // When the numbers of a tab's report change, a new report has arrived: that is the moment its reading time is counted from.
            if (c.Timing is { } timing && (track.Timing != timing || track.TimingState != c.State))
            {
                track.Timing = timing;
                track.TimingState = c.State;
                track.TimingArrived = now;
            }

            track.State = c.State;
            track.Seen = now;
            track.MissingSince = null;
        }

        // A source that is not in this picture is gone from the moment this picture noticed it, not from its last
        // report: a steady player is reported rarely, so the time since its last report says nothing.
        foreach (var track in _tracks.Where(p => candidates.All(c => c.Key != p.Key)).Select(p => p.Value))
            track.MissingSince ??= now - track.Seen <= QuietGap ? track.Seen : now;
        foreach (var key in _tracks.Where(p => p.Value.MissingSince is { } since && now - since > VanishGrace).Select(p => p.Key).ToList())
            _tracks.Remove(key);
        return started;
    }

    private void Choose(List<Candidate> candidates, List<Candidate> started, DateTimeOffset now)
    {
        var current = _held is null ? null : candidates.FirstOrDefault(c => c.Key == _held.Data.Key);
        if (_held is not null)
        {
            _held.Present = current is not null;
            if (_held.Present) _held.MissingSince = null;
            else
            {
                _held.MissingSince ??= now - _held.Seen <= QuietGap ? _held.Seen : now;
                if (now - _held.MissingSince > VanishGrace) _held = null;
            }
        }

        var pick = started.Count > 0 ? started[^1] : null;
        // The line is idle (nothing held, or the held item is paused while another plays): follow what plays now.
        var idle = _held is null || (current is not null && current.State != PlaybackState.Playing);
        pick ??= idle ? candidates.Where(c => c.State == PlaybackState.Playing).MaxBy(c => _tracks[c.Key].StartOrder) : null;

        // Windows' words shown for a tab that is no longer the one used most recently: they follow the tab that is (going back to a tab used a moment ago
        // must not leave the line on the other one, with its buttons dead, for the whole grace period).
        if (pick is null && _held is { Data.FallbackTabKey: not null } && candidates.FirstOrDefault(c => c.FallbackTabKey is not null) is { } following && following.Key != _held.Data.Key)
            pick = following;

        if (pick is not null)
            _held = new Held(pick, now) { Present = true };
        else if (current is not null)
            _held!.Refresh(current, now);
    }

    private NowPlayingView? ViewAt(DateTimeOffset now)
    {
        if (_held is not { } h) return null;
        if (!h.Present && h.MissingSince is { } gone && now - gone > VanishGrace) return null;

        var d = h.Data;
        var paused = d.State != PlaybackState.Playing;
        var position = d.Position is { } p ? p + (paused ? 0 : Math.Max(0, (now - h.AsOf).TotalSeconds)) : (double?)null;
        var length = d.Length is > 0 ? d.Length : null;
        if (position is { } pos && length is { } len) position = Math.Clamp(pos, 0, len);
        double? progress = position is { } q && length is { } l ? q / l : null;
        var title = string.IsNullOrWhiteSpace(d.Title) ? d.Where : d.Title;

        // The raw report for the pill's ring: a session's own, or a tab's numbers with the moment they arrived (so the position can be
        // worked out between reports without ever being invented).
        var report = d.Timeline;
        if (report is null && d.Timing is { } t && _tracks.TryGetValue(d.Key, out var track))
            report = TabMediaTiming.ToReport(t.Position, t.Length, !paused, t.Rate, t.ReadAtMs, track.TimingArrived);

        return new NowPlayingView(title, d.Artist, d.Where, paused ? $"{d.Where} - paused" : d.Where, d.State, paused,
            position, length, progress, d.Target, h.Present, d.SourceApp, d.Host, d.IsBrowserSession, report, d.FallbackTabKey);
    }

    private sealed class Track
    {
        public PlaybackState State { get; set; } = PlaybackState.Stopped;
        public long StartOrder { get; set; }
        public DateTimeOffset Seen { get; set; }
        public DateTimeOffset? PausedAt { get; set; }
        public DateTimeOffset? MissingSince { get; set; }
        public TabTiming? Timing { get; set; }
        public PlaybackState TimingState { get; set; }
        public DateTimeOffset TimingArrived { get; set; }
    }

    private sealed class Held(Candidate data, DateTimeOffset now)
    {
        public Candidate Data { get; private set; } = data;
        public DateTimeOffset Seen { get; private set; } = now;
        public DateTimeOffset AsOf { get; private set; } = now;
        public bool Present { get; set; }
        public DateTimeOffset? MissingSince { get; set; }

        public void Refresh(Candidate fresh, DateTimeOffset at)
        {
            Data = fresh;
            Seen = at;
            AsOf = at;
        }
    }

    private sealed record Candidate(
        string Key,
        MediaTarget Target,
        string Where,
        string? SourceApp,
        string? Host,
        bool IsBrowserSession,
        string? Title,
        string? Artist,
        PlaybackState State,
        double? Position,
        double? Length,
        long Order,
        ProgressReport? Timeline,
        TabTiming? Timing,
        string? FallbackTabKey = null);

    /// <summary>The numbers of a tab's media report that tell where it is and how fast it goes.</summary>
    private sealed record TabTiming(double? Position, double? Length, double? Rate, double? ReadAtMs);
}
