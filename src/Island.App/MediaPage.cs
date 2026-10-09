using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// What the Media page says is playing, and what its buttons do. Feeds <see cref="NowPlaying"/> from Windows' media
/// sessions and the browser tabs, with the sites picked on the Media page deciding what counts; the buttons act on
/// the now-playing item only, through the media door (sessions) or the tab door (add-on). Lives on the UI thread.
/// </summary>
internal sealed class MediaPage
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(500);

    private readonly AppWorld _world;
    private readonly Func<PickStore> _store;
    private readonly NowPlaying _nowPlaying = new();
    private DateTimeOffset _lastPoll = DateTimeOffset.MinValue;

    public MediaPage(AppWorld world, Func<PickStore> store)
    {
        _world = world;
        _store = store;
    }

    private readonly List<string> _played = [];

    /// <summary>The services that have played since the app started, the latest last, by the names the line uses ("YouTube", "Spotify"). Search offers its last tile for them.</summary>
    public IReadOnlyList<string> PlayedServices => _played;

    /// <summary>What is playing now; null shows the selected pick as other pages do.</summary>
    public NowPlayingView? View { get; private set; }

    /// <summary>The id of the pick of the Media page that what is playing belongs to; null when it belongs to none (WORK-ORDER-5 §7).</summary>
    public string? PlayingPickId => PlayingTile.PickIdFor(View, _store().ForPage(PageIds.Media));

    /// <summary>Raised on the UI thread when <see cref="View"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Reads the sessions and tabs again. Call on the UI thread, at a change and about twice a second while the island is shown.</summary>
    public void Refresh(DateTimeOffset now)
    {
        _lastPoll = now;
        var policy = NowPlayingPolicy.ForMediaPagePicks(_store().ForPage(PageIds.Media));
        var view = _nowPlaying.Update(now, _world.Media.Sessions, _world.Tabs.Tabs, _world.Tabs.Connected, policy);
        if (view is { IsPaused: false } && !view.IsBrowserSession && (_played.Count == 0 || _played[^1] != view.Where))
        {
            _played.RemoveAll(p => p == view.Where);
            _played.Add(view.Where);
        }

        if (Equals(view, View)) return;
        View = view;
        Changed?.Invoke();
    }

    /// <summary>Called on every drawn frame: refreshes at most twice a second (the vanish grace and the progress move with time).</summary>
    public void Poll(DateTimeOffset now)
    {
        if (now - _lastPoll >= PollEvery) Refresh(now);
    }

    /// <summary>Previous, play/pause or next on the now-playing item; nothing when nothing is playing.</summary>
    public bool Send(MediaCommand command) => _nowPlaying.PlanFor(command)?.Send(_world.MediaControl, _world.TabControl) ?? false;

    /// <summary>A click on the line brings the player (or the tab) forward.</summary>
    public bool BringLineForward()
    {
        if (View is not { } view) return false;
        if (view.Target.Kind == MediaTargetKind.Tab) return _world.TabControl.Activate(view.Target.Id);
        if (view.FallbackTabKey is { } tab) return _world.TabControl.Activate(tab); // Windows' words, shown for a tab: the click goes to the tab

        // A desktop player: the window of the program that owns the session, matched by its file name, top-most first.
        var app = view.SourceApp;
        if (string.IsNullOrEmpty(app)) return false;
        var window = _world.Snapshot().Windows
            .Where(w => string.Equals(w.ExeName, app, StringComparison.OrdinalIgnoreCase)                                        // the session says the exe name
                        || w.ExeName is { } exe && string.Equals(Path.GetFileNameWithoutExtension(exe), Path.GetFileNameWithoutExtension(app), StringComparison.OrdinalIgnoreCase)
                        || w.PackageFamily is { Length: > 3 } pkg && app.StartsWith(pkg.Split('_')[0], StringComparison.OrdinalIgnoreCase)) // a Store app id begins with its package name
            .OrderBy(w => w.ZOrder)
            .FirstOrDefault();
        return window is not null && _world.Outside.BringForward(window.Handle);
    }
}
