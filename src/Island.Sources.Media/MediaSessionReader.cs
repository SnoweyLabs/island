using Island.Core;
using Windows.Foundation;
using Windows.Media.Control;

namespace Island.Sources.Media;

/// <summary>
/// Reads Windows' media sessions (GlobalSystemMediaTransportControlsSessionManager) on its own thread and keeps the
/// latest list in <see cref="Sessions"/>. Events (sessions changed, current session changed, each session's media
/// properties and playback info changed) only wake the thread; a slow poll also reconciles, because sessions
/// vanish between tracks and events get lost. It never throws into the caller; when Windows will not give a
/// session manager (it fails outside an interactive session) the list is simply empty. It sends nothing: that is
/// <see cref="OutsideMedia"/>. Nothing it reads is written anywhere.
/// </summary>
public sealed class MediaSessionReader : IMediaSessionSource, IDisposable
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEventSlim _firstRead = new(false);
    private readonly Dictionary<string, Subscription> _subscriptions = []; // reader thread only
    private IReadOnlyDictionary<string, GlobalSystemMediaTransportControlsSession> _live = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
    private IReadOnlyList<MediaSessionInfo> _sessions = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager; // reader thread only
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _managerAvailable;

    /// <summary>The latest reading; empty when none could be had. Safe to read from any thread.</summary>
    public IReadOnlyList<MediaSessionInfo> Sessions => Volatile.Read(ref _sessions);

    /// <summary>True when Windows gave a session manager at the last attempt.</summary>
    public bool ManagerAvailable => _managerAvailable;

    /// <summary>Raised on the reader's thread when something other than a playback position changed.</summary>
    public event Action? Changed;

    /// <summary>Starts the reader thread. Calling it again does nothing.</summary>
    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Island media reader" };
        _thread.Start();
    }

    /// <summary>Waits until the first reading is done (also when it found nothing). False on timeout.</summary>
    public bool WaitForFirstRead(TimeSpan timeout) => _firstRead.Wait(timeout);

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>The live session behind an id from <see cref="Sessions"/>, for <see cref="OutsideMedia"/>; null when it is gone.</summary>
    internal GlobalSystemMediaTransportControlsSession? Find(string sessionId) =>
        Volatile.Read(ref _live).GetValueOrDefault(sessionId);

    private void Run()
    {
        try
        {
            while (!_stop)
            {
                SafeRead();
                _firstRead.Set();
                _wake.WaitOne(PollEvery);
                if (_stop) break;
                Thread.Sleep(Debounce); // events come in bursts: read once after the burst
                _wake.Reset();
            }
        }
        finally
        {
            foreach (var s in _subscriptions.Values) s.Dispose();
            _subscriptions.Clear();
        }
    }

    private void SafeRead()
    {
        try
        {
            Read();
        }
        catch
        {
            _manager = null; // ask Windows again at the next round
            Publish([], new Dictionary<string, GlobalSystemMediaTransportControlsSession>());
        }
    }

    private void Read()
    {
        var manager = EnsureManager();
        _managerAvailable = manager is not null;
        if (manager is null)
        {
            Publish([], new Dictionary<string, GlobalSystemMediaTransportControlsSession>());
            return;
        }

        var infos = new List<MediaSessionInfo>();
        var live = new Dictionary<string, GlobalSystemMediaTransportControlsSession>();
        foreach (var session in manager.GetSessions())
        {
            try
            {
                var id = UniqueId(session, live);
                infos.Add(Describe(id, session));
                live[id] = session;
            }
            catch
            {
                // A session that closes while it is read is skipped; the next round sees the truth.
            }
        }

        SyncSubscriptions(live);
        Publish(infos, live);
    }

    private GlobalSystemMediaTransportControlsSessionManager? EnsureManager()
    {
        if (_manager is not null) return _manager;
        try
        {
            var task = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            if (!task.Wait(CallTimeout)) return null;
            var manager = task.Result;
            manager.SessionsChanged += (_, _) => _wake.Set();
            manager.CurrentSessionChanged += (_, _) => _wake.Set();
            return _manager = manager;
        }
        catch
        {
            return null; // 0x80070424 outside an interactive session, among others
        }
    }

    private static string UniqueId(GlobalSystemMediaTransportControlsSession session, Dictionary<string, GlobalSystemMediaTransportControlsSession> taken)
    {
        var baseId = string.IsNullOrWhiteSpace(session.SourceAppUserModelId) ? "unknown" : session.SourceAppUserModelId;
        var id = baseId;
        for (var n = 2; taken.ContainsKey(id); n++) id = $"{baseId}#{n}";
        return id;
    }

    private static MediaSessionInfo Describe(string id, GlobalSystemMediaTransportControlsSession session)
    {
        var app = session.SourceAppUserModelId ?? string.Empty;
        var props = Wait(session.TryGetMediaPropertiesAsync());
        var state = MapState(session.GetPlaybackInfo().PlaybackStatus);
        var (position, length) = ReadTimeline(session, state);
        return new MediaSessionInfo(id, app, MediaNames.IsBrowserApp(app), NullIfEmpty(props?.Title), NullIfEmpty(props?.Artist), state, position, length, RawReport(session, state));
    }

    private static PlaybackState MapState(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState.Paused, // between tracks
        _ => PlaybackState.Stopped,
    };

    /// <summary>Position as of now (the timeline is a snapshot, so a playing session's is moved on) and length; null where the session does not say.</summary>
    private static (double? Position, double? Length) ReadTimeline(GlobalSystemMediaTransportControlsSession session, PlaybackState state)
    {
        var t = session.GetTimelineProperties();
        var length = (t.EndTime - t.StartTime).TotalSeconds;
        if (length <= 0) return (null, null);

        var position = t.Position.TotalSeconds;
        var age = (DateTimeOffset.UtcNow - t.LastUpdatedTime).TotalSeconds;
        if (state == PlaybackState.Playing && age is > 0 and < 86400) position += age;
        return (Math.Clamp(position, 0, length), length);
    }

    /// <summary>
    /// The timeline as the player gave it, unaltered, for the pill's ring: the position as of LastUpdatedTime (which is Windows' zero date when
    /// there is no timeline), the length, and the playing speed (null counts as 1). The ring's own rules (<see cref="LitShare"/>) decide
    /// whether it can be drawn; nothing is clamped or moved on here.
    /// </summary>
    private static ProgressReport RawReport(GlobalSystemMediaTransportControlsSession session, PlaybackState state)
    {
        var t = session.GetTimelineProperties();
        var rate = session.GetPlaybackInfo().PlaybackRate;
        return new ProgressReport(t.Position.TotalSeconds, (t.EndTime - t.StartTime).TotalSeconds, t.LastUpdatedTime, state == PlaybackState.Playing, rate ?? 1);
    }

    private void SyncSubscriptions(Dictionary<string, GlobalSystemMediaTransportControlsSession> live)
    {
        foreach (var gone in _subscriptions.Keys.Where(id => !live.ContainsKey(id)).ToList())
        {
            _subscriptions[gone].Dispose();
            _subscriptions.Remove(gone);
        }

        foreach (var (id, session) in live)
        {
            if (_subscriptions.TryGetValue(id, out var existing) && existing.Is(session)) continue;
            existing?.Dispose();
            _subscriptions[id] = new Subscription(session, () => _wake.Set());
        }
    }

    private void Publish(List<MediaSessionInfo> infos, IReadOnlyDictionary<string, GlobalSystemMediaTransportControlsSession> live)
    {
        var old = Volatile.Read(ref _sessions);
        Volatile.Write(ref _live, live);
        Volatile.Write(ref _sessions, infos);
        // A moving position alone is not a change: it would fire at every poll.
        if (SameIgnoringPosition(old, infos)) return;

        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // A listener's failure is the listener's own.
        }
    }

    private static bool SameIgnoringPosition(IReadOnlyList<MediaSessionInfo> a, IReadOnlyList<MediaSessionInfo> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First with { PositionSeconds = null } == p.Second with { PositionSeconds = null });

    private static T? Wait<T>(IAsyncOperation<T> operation) where T : class
    {
        var task = operation.AsTask();
        return task.Wait(CallTimeout) ? task.Result : null;
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private sealed class Subscription : IDisposable
    {
        private readonly GlobalSystemMediaTransportControlsSession _session;
        private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _properties;
        private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _playback;

        public Subscription(GlobalSystemMediaTransportControlsSession session, Action wake)
        {
            _session = session;
            _properties = (_, _) => wake();
            _playback = (_, _) => wake();
            session.MediaPropertiesChanged += _properties;
            session.PlaybackInfoChanged += _playback;
        }

        public bool Is(GlobalSystemMediaTransportControlsSession session) => ReferenceEquals(_session, session);

        public void Dispose()
        {
            try
            {
                _session.MediaPropertiesChanged -= _properties;
                _session.PlaybackInfoChanged -= _playback;
            }
            catch
            {
                // The session is already gone.
            }
        }
    }
}
